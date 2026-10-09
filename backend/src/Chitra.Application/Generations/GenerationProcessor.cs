using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Media;
using Chitra.Application.Settings;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chitra.Application.Generations;

/// <summary>
/// Runs one generation job: submits it to the provider, polls status, streams progress to the UI,
/// downloads the outputs into local storage and records the result.
/// </summary>
public sealed class GenerationProcessor(
    IAppDbContext db,
    IModelCatalog catalog,
    IAiProviderRegistry providers,
    IRuntimeSettings settings,
    IRealtimeNotifier notifier,
    IMediaStorage storage,
    MediaService media,
    ILogger<GenerationProcessor> logger)
{
    internal static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a job may be unknown to the provider before it is considered lost.</summary>
    private static readonly TimeSpan LostJobGrace = TimeSpan.FromSeconds(30);

    private Generation? _generation;
    private GenerationJob? _job;
    private DateTime _lastNotify = DateTime.MinValue;

    /// <param name="cancellation">Fires when the user cancels or the application stops.</param>
    /// <param name="shutdown">Fires only when the application stops.</param>
    public async Task ProcessAsync(Guid jobId, CancellationToken cancellation, CancellationToken shutdown)
    {
        _job = await db.GenerationJobs.Include(j => j.Generation).ThenInclude(g => g!.Outputs).FirstOrDefaultAsync(j => j.Id == jobId, shutdown);
        _generation = _job?.Generation;
        if (_job is null || _generation is null || _generation.Status.IsTerminal() || _job.Status.IsTerminal())
        {
            return;
        }

        var job = _job;
        var generation = _generation;
        var current = settings.Current;
        IAiGenerationProvider? provider = null;
        try
        {
            provider = providers.Get(job.ProviderName);
        }
        catch (AppException ex)
        {
            await FailAsync(ex);
            return;
        }

        // Batch providers (Kaggle) queue and set up remotely, so they define their own, longer limit.
        var timeout = provider.MaxJobDuration
                      ?? TimeSpan.FromMinutes(generation.Type.ProducesVideo() ? current.VideoTimeoutMinutes : current.ImageTimeoutMinutes);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeoutCts.Token);
        var ct = linked.Token;

        try
        {
            var model = catalog.Find(generation.ModelId)
                        ?? throw new AppException(ErrorCode.ModelMissing, $"Model '{generation.ModelId}' is no longer in the model catalog.");

            if (job.ProviderJobId is null)
            {
                job.Status = generation.Status = GenerationStatus.Processing;
                job.StartedAt = generation.StartedAt = DateTime.UtcNow;
                await UpdateProgressAsync(1, $"Submitting to {provider.Name}", force: true, ct);

                var submission = await SubmitAsync(provider, model, generation, ct);
                job.ProviderJobId = submission.ProviderJobId;
                if (submission.Warnings.Count > 0)
                {
                    generation.Warnings = string.Join('\n', new[] { generation.Warnings }.Concat(submission.Warnings).Where(w => !string.IsNullOrEmpty(w)));
                }

                await UpdateProgressAsync(3, $"Waiting for {provider.Name}", force: true, ct);
            }
            else
            {
                logger.LogInformation("Resuming generation {GenerationId} (provider job {ProviderJobId})", generation.Id, job.ProviderJobId);
                await UpdateProgressAsync(Math.Max(generation.Progress, 3), "Resuming after restart", force: true, ct);
            }

            var outputs = await WaitForCompletionAsync(provider, job.ProviderJobId, ct);

            await UpdateProgressAsync(92, "Saving output", force: true, ct);
            foreach (var output in outputs)
            {
                await using var stream = await provider.OpenOutputAsync(output, ct);
                var asset = await media.CreateAssetAsync(stream, Path.GetExtension(output.FileName), output.MediaType,
                    MediaSource.Generated, generation.UserId, generation.ProjectId, generation.Id, ct, output.FileName);
                if (output.MediaType == MediaType.Image && asset.Width is null)
                {
                    asset.Width = generation.Width;
                    asset.Height = generation.Height;
                }

                if (output.MediaType == MediaType.Video)
                {
                    asset.Width ??= generation.Width;
                    asset.Height ??= generation.Height;
                    asset.DurationSeconds ??= generation.DurationSeconds;
                }

                generation.Outputs.Add(asset);
            }

            var primary = generation.Outputs.FirstOrDefault(o => o.Type == (generation.Type.ProducesVideo() ? MediaType.Video : MediaType.Image))
                          ?? generation.Outputs.First();
            generation.OutputAssetId = primary.Id;
            generation.OutputPath = job.OutputPath = primary.RelativePath;
            generation.Status = job.Status = GenerationStatus.Completed;
            generation.Progress = job.Progress = 100;
            generation.StatusMessage = "Completed";
            generation.CompletedAt = job.CompletedAt = DateTime.UtcNow;
            await AttachToSceneAsync(generation, primary, ct);
            await SaveAndNotifyAsync(CancellationToken.None);
            logger.LogInformation("Generation {GenerationId} completed with {Count} output(s)", generation.Id, generation.Outputs.Count);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            // Leave the job as Processing; it is resumed (not resubmitted) on next start.
            logger.LogInformation("Generation {GenerationId} interrupted by shutdown; it will resume on restart", generation.Id);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellation.IsCancellationRequested)
        {
            await CancelOnProviderAsync(provider, job.ProviderJobId);
            await FailAsync(new AppException(ErrorCode.Timeout,
                $"Generation timed out after {timeout.TotalMinutes:0} minutes. Large video models can be slow on limited GPUs; " +
                "try fewer frames/steps, a lower resolution, or raise the timeout in Settings."));
        }
        catch (OperationCanceledException)
        {
            await CancelOnProviderAsync(provider, job.ProviderJobId);
            generation.Status = job.Status = GenerationStatus.Cancelled;
            generation.StatusMessage = "Cancelled by user";
            generation.CompletedAt = job.CompletedAt = DateTime.UtcNow;
            await SaveAndNotifyAsync(CancellationToken.None);
        }
        catch (AppException ex)
        {
            await FailAsync(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Generation {GenerationId} failed unexpectedly", generation.Id);
            await FailAsync(new AppException(ErrorCode.GenerationFailed, $"Generation failed: {ex.Message}", ex));
        }
    }

    private Task<ProviderSubmission> SubmitAsync(IAiGenerationProvider provider, AiModelDefinition model, Generation g, CancellationToken ct)
    {
        switch (g.Type)
        {
            case GenerationType.TextToImage:
                return provider.GenerateImageAsync(new ImageGenerationRequest
                {
                    GenerationId = g.Id,
                    Model = model,
                    Prompt = g.FinalPrompt,
                    NegativePrompt = g.NegativePrompt ?? string.Empty,
                    Width = g.Width,
                    Height = g.Height,
                    Steps = g.Steps,
                    CfgScale = g.CfgScale,
                    Seed = g.Seed,
                    Sampler = g.Sampler,
                    Scheduler = g.Scheduler,
                    BatchSize = g.BatchSize,
                }, ct);

            case GenerationType.TextToVideo:
                return provider.GenerateVideoAsync(new VideoGenerationRequest
                {
                    GenerationId = g.Id,
                    Model = model,
                    Prompt = g.FinalPrompt,
                    NegativePrompt = g.NegativePrompt ?? string.Empty,
                    Width = g.Width,
                    Height = g.Height,
                    Steps = g.Steps,
                    CfgScale = g.CfgScale,
                    Seed = g.Seed,
                    Sampler = g.Sampler,
                    Scheduler = g.Scheduler,
                    FrameCount = g.FrameCount ?? 49,
                    Fps = g.Fps ?? 24,
                    MotionStrength = g.MotionStrength ?? 0.5,
                }, ct);

            case GenerationType.ImageToVideo:
                return SubmitImageToVideoAsync(provider, model, g, ct);

            default:
                throw new AppException(ErrorCode.Validation, $"Unsupported generation type {g.Type}.");
        }
    }

    private async Task<ProviderSubmission> SubmitImageToVideoAsync(IAiGenerationProvider provider, AiModelDefinition model, Generation g, CancellationToken ct)
    {
        var input = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == g.InputAssetId, ct)
                    ?? throw new AppException(ErrorCode.Validation, "The input image for this generation was deleted.");
        if (!storage.Exists(input.RelativePath))
        {
            throw new AppException(ErrorCode.Validation, "The input image file is missing from storage.");
        }

        return await provider.GenerateImageToVideoAsync(new ImageToVideoRequest
        {
            GenerationId = g.Id,
            Model = model,
            Prompt = g.FinalPrompt,
            NegativePrompt = g.NegativePrompt ?? string.Empty,
            Width = g.Width,
            Height = g.Height,
            Steps = g.Steps,
            CfgScale = g.CfgScale,
            Seed = g.Seed,
            Sampler = g.Sampler,
            Scheduler = g.Scheduler,
            FrameCount = g.FrameCount ?? 49,
            Fps = g.Fps ?? 24,
            MotionStrength = g.MotionStrength ?? 0.5,
            InputImagePath = storage.GetFullPath(input.RelativePath),
        }, ct);
    }

    private async Task<IReadOnlyList<ProviderOutputFile>> WaitForCompletionAsync(IAiGenerationProvider provider, string providerJobId, CancellationToken ct)
    {
        DateTime? unknownSince = null;
        while (true)
        {
            await Task.Delay(PollInterval, ct);
            var status = await provider.GetGenerationStatusAsync(providerJobId, ct);

            switch (status.State)
            {
                case ProviderJobState.Pending:
                    unknownSince = null;
                    await UpdateProgressAsync(_generation!.Progress, status.Message ?? "Waiting in queue", force: false, ct);
                    break;

                case ProviderJobState.Running:
                    unknownSince = null;
                    var progress = 5 + (int)Math.Round(Math.Clamp(status.Progress, 0, 100) * 0.85);
                    await UpdateProgressAsync(Math.Max(progress, _generation!.Progress), status.Message ?? "Generating", force: false, ct);
                    break;

                case ProviderJobState.Completed:
                    if (status.Outputs.Count == 0)
                    {
                        throw new AppException(ErrorCode.InvalidWorkflow,
                            $"{provider.Name} finished but produced no image or video files. Check that the workflow ends with a Save node.");
                    }

                    return status.Outputs;

                case ProviderJobState.Failed:
                    throw new AppException(status.ErrorCode ?? ErrorCode.GenerationFailed, status.Error ?? $"{provider.Name} reported a failure.");

                case ProviderJobState.Cancelled:
                    throw new OperationCanceledException();

                default:
                    unknownSince ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - unknownSince > LostJobGrace)
                    {
                        throw new AppException(ErrorCode.GenerationFailed,
                            $"{provider.Name} no longer knows about this job (it may have been restarted or its queue cleared). Regenerate to try again.");
                    }

                    break;
            }
        }
    }

    private async Task AttachToSceneAsync(Generation generation, MediaAsset primary, CancellationToken ct)
    {
        if (generation.SceneId is null) return;
        var scene = await db.Scenes.FirstOrDefaultAsync(s => s.Id == generation.SceneId, ct);
        if (scene is null) return;

        if (primary.Type == MediaType.Image) scene.ImageAssetId = primary.Id;
        else if (primary.Type == MediaType.Video) scene.VideoAssetId = primary.Id;
        scene.Touch();
    }

    private async Task UpdateProgressAsync(int progress, string message, bool force, CancellationToken ct)
    {
        var generation = _generation!;
        var changed = progress != generation.Progress || message != generation.StatusMessage;
        if (!changed && !force) return;

        generation.Progress = _job!.Progress = Math.Clamp(progress, 0, 100);
        generation.StatusMessage = message;

        // Throttle database writes and push notifications for fast-moving progress.
        if (!force && DateTime.UtcNow - _lastNotify < TimeSpan.FromMilliseconds(400)) return;
        await SaveAndNotifyAsync(ct);
    }

    private async Task FailAsync(AppException ex)
    {
        var generation = _generation!;
        generation.Status = _job!.Status = GenerationStatus.Failed;
        generation.ErrorCode = ex.Code.ToString();
        generation.ErrorMessage = _job.ErrorMessage = ex.Message;
        generation.StatusMessage = "Failed";
        generation.CompletedAt = _job.CompletedAt = DateTime.UtcNow;
        logger.LogWarning("Generation {GenerationId} failed: {Code} {Message}", generation.Id, ex.Code, ex.Message);
        await SaveAndNotifyAsync(CancellationToken.None);
    }

    private async Task SaveAndNotifyAsync(CancellationToken ct)
    {
        var generation = _generation!;
        generation.Touch();
        _job!.Touch();

        // A user cancellation saved by another request wins over in-flight progress updates.
        var persistedStatus = await db.Generations.AsNoTracking().Where(g => g.Id == generation.Id).Select(g => g.Status).FirstOrDefaultAsync(ct);
        if (persistedStatus == GenerationStatus.Cancelled && generation.Status != GenerationStatus.Cancelled)
        {
            generation.Status = _job.Status = GenerationStatus.Cancelled;
            generation.StatusMessage = "Cancelled by user";
        }

        await db.SaveChangesAsync(ct);
        _lastNotify = DateTime.UtcNow;
        await notifier.GenerationUpdatedAsync(generation.UserId, generation.ToDto(), ct);
    }

    private async Task CancelOnProviderAsync(IAiGenerationProvider? provider, string? providerJobId)
    {
        if (provider is null || providerJobId is null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await provider.CancelAsync(providerJobId, cts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not cancel provider job {ProviderJobId}", providerJobId);
        }
    }
}
