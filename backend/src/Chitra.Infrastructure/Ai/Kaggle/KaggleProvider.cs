using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Common;
using Chitra.Application.Settings;
using Chitra.Domain.Enums;
using Chitra.Infrastructure.Ai.ComfyUi;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.Ai.Kaggle;

/// <summary>
/// Runs Chitra's ComfyUI workflows on Kaggle's free GPUs in batch mode. Jobs are queued locally and executed by
/// <see cref="KaggleRunCoordinator"/> as private notebook runs (official Kaggle API: kernels push / status / output).
/// There is no live connection to the notebook, so there is no step-level progress and a started run cannot be
/// stopped from here; both limits are reported honestly through status messages.
/// </summary>
public sealed class KaggleProvider(
    KaggleJobStore store,
    KaggleRunCoordinator coordinator,
    KaggleAccount account,
    IWorkflowTemplateStore workflows,
    IRuntimeSettings settings,
    ILogger<KaggleProvider> logger) : IAiGenerationProvider
{
    public const string ProviderName = "Kaggle";

    /// <summary>Per-GPU memory of Kaggle accelerators. Two T4s are two separate 15 GB GPUs, not 30 GB.</summary>
    private static readonly Dictionary<string, (string Name, double VramGb, int Count)> Accelerators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NvidiaTeslaT4"] = ("Tesla T4 (Kaggle)", 15, 2),
        ["NvidiaTeslaP100"] = ("Tesla P100 (Kaggle)", 16, 1),
        ["NvidiaL4"] = ("L4 (Kaggle)", 22, 1),
        ["NvidiaL4X1"] = ("L4 (Kaggle)", 22, 1),
    };

    private const long MaxInputBytes = 3 * 1024 * 1024;

    public string Name => ProviderName;

    /// <summary>Inference runs on Kaggle's servers; prompts and input images are uploaded there.</summary>
    public bool IsLocal => false;

    /// <summary>Free within Kaggle's weekly GPU quota; no per-request billing.</summary>
    public bool IncursApiCharges => false;

    public bool DownloadsModelsOnDemand => true;

    public bool CanCancelRunningJobs => false;

    public bool ManagesOwnQueue => true;

    /// <summary>Jobs may wait for an earlier batch, then for Kaggle's queue, setup and model downloads.</summary>
    public TimeSpan? MaxJobDuration => TimeSpan.FromMinutes(settings.Current.KaggleRunTimeoutMinutes * 2 + 90);

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var current = settings.Current;
        var (username, error, _) = await account.GetAsync(cancellationToken);
        if (username is null)
        {
            return new ProviderHealth(false, $"Kaggle is not ready: {error}");
        }

        var gpu = Accelerators.GetValueOrDefault(current.KaggleAccelerator, (current.KaggleAccelerator, 15, 1));
        var devices = Enumerable.Range(0, gpu.Count)
            .Select(_ => new ProviderDevice(gpu.Name, "cuda", (long)(gpu.VramGb * 1024 * 1024 * 1024), (long)(gpu.VramGb * 1024 * 1024 * 1024)))
            .ToList();
        var active = store.ActiveRun;
        var activeText = active is null ? string.Empty : $" Run in progress: {DescribeRun(active)}.";
        return new ProviderHealth(true,
            $"Kaggle batch mode as @{username} on {gpu.Count}× {gpu.Name}, notebook {current.KaggleKernelSlug}. " +
            $"Each run needs several minutes of setup; free within your weekly Kaggle GPU quota.{activeText}",
            "batch", devices);
    }

    public Task<ProviderSubmission> GenerateImageAsync(ImageGenerationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Enqueue(request, GenerationType.TextToImage, null));

    public Task<ProviderSubmission> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Enqueue(request, GenerationType.TextToVideo, null));

    public Task<ProviderSubmission> GenerateImageToVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Enqueue(request, GenerationType.ImageToVideo, request.InputImagePath));

    public Task<ProviderJobStatus> GetGenerationStatusAsync(string providerJobId, CancellationToken cancellationToken)
    {
        var job = store.Get(providerJobId);
        if (job is null)
        {
            return Task.FromResult(new ProviderJobStatus(ProviderJobState.Unknown, 0, "Kaggle job record not found", []));
        }

        var run = job.RunId is null ? null : store.GetRun(job.RunId);
        var status = job.State switch
        {
            KaggleJobState.Pending => new ProviderJobStatus(ProviderJobState.Pending, 0,
                job.Error is null
                    ? store.ActiveRun is { } busy ? $"Waiting for the current Kaggle run to finish ({DescribeRun(busy)})" : "Collecting jobs for the next Kaggle run"
                    : $"Retrying Kaggle push: {job.Error}", []),
            KaggleJobState.Submitted => new ProviderJobStatus(ProviderJobState.Pending, 0,
                $"Kaggle run queued — waiting for a GPU session{(run?.Url is { } url ? $" ({url})" : string.Empty)}", []),
            KaggleJobState.Running => new ProviderJobStatus(ProviderJobState.Running, 0,
                $"Running on Kaggle for {Elapsed(run?.RunningSince)} — setup, model download and generation; no step progress in batch mode", []),
            KaggleJobState.Completed => new ProviderJobStatus(ProviderJobState.Completed, 100, "Downloaded from Kaggle",
                job.Outputs.Select(path => new ProviderOutputFile(Path.GetFileName(path), Classify(path), path)).ToList()),
            KaggleJobState.Failed => new ProviderJobStatus(ProviderJobState.Failed, 0, "Failed", [],
                Enum.TryParse<ErrorCode>(job.ErrorCode, out var code) ? code : ErrorCode.GenerationFailed, job.Error),
            _ => new ProviderJobStatus(ProviderJobState.Cancelled, 0, "Discarded", []),
        };
        return Task.FromResult(status);
    }

    public Task<Stream> OpenOutputAsync(ProviderOutputFile output, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(output.Locator);
        if (!path.StartsWith(Path.GetFullPath(store.Root), StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new AppException(ErrorCode.GenerationFailed, "The downloaded Kaggle output file is missing.");
        }

        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    /// <summary>
    /// Jobs not yet pushed are dropped. A job already running on Kaggle cannot be stopped through the public API;
    /// it finishes remotely and its result is discarded.
    /// </summary>
    public Task CancelAsync(string providerJobId, CancellationToken cancellationToken)
    {
        var job = store.Get(providerJobId);
        if (job is not null && job.State is not (KaggleJobState.Completed or KaggleJobState.Failed))
        {
            if (job.State is KaggleJobState.Submitted or KaggleJobState.Running)
            {
                logger.LogInformation("Kaggle job {Key} is already part of a run; it will finish remotely and its result is discarded", job.Key);
            }

            store.UpdateJob(providerJobId, j =>
            {
                j.State = KaggleJobState.Discarded;
                j.FinishedAt = DateTime.UtcNow;
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>Models are downloaded inside each Kaggle session, so there is nothing to report as installed.</summary>
    public Task<ModelInventory?> GetModelInventoryAsync(CancellationToken cancellationToken) => Task.FromResult<ModelInventory?>(null);

    private ProviderSubmission Enqueue(ProviderGenerationRequest request, GenerationType type, string? inputPath)
    {
        var current = settings.Current;
        var model = request.Model;
        var gpu = Accelerators.GetValueOrDefault(current.KaggleAccelerator, (current.KaggleAccelerator, 15, 1));
        if (model.MinVramGb > gpu.VramGb)
        {
            throw new AppException(ErrorCode.InsufficientVram,
                $"{model.Name} needs at least {model.MinVramGb:0.#} GB of GPU memory, but each Kaggle {gpu.Name} GPU has {gpu.VramGb:0.#} GB. " +
                "GPU memory is not combined across the two GPUs. Choose a smaller model or run it on local hardware.");
        }

        var missingUrl = model.Files.Where(f => f.DownloadUrl is null).Select(f => f.FileName).ToList();
        if (missingUrl.Count > 0)
        {
            throw new AppException(ErrorCode.ModelMissing,
                $"{model.Name} cannot run on Kaggle because these files have no download URL: {string.Join(", ", missingUrl)}.");
        }

        var key = $"{request.GenerationId:N}-{Guid.NewGuid().ToString("N")[..6]}";
        var inputs = new List<KaggleInputFile>();
        string? inputName = null;
        if (inputPath is not null)
        {
            var info = new FileInfo(inputPath);
            if (info.Length > MaxInputBytes)
            {
                throw new AppException(ErrorCode.Validation,
                    $"The input image is {info.Length / 1024 / 1024.0:0.#} MB; Kaggle batch jobs accept images up to 3 MB. Use a smaller (or JPEG) image.");
            }

            inputName = $"chitra_{key}{info.Extension.ToLowerInvariant()}";
            inputs.Add(new KaggleInputFile { Name = inputName, LocalPath = inputPath });
        }

        var workflow = ComfyWorkflowBuilder.Build(workflows, request, type, inventory: null, inputImageName: inputName);
        store.Add(new KaggleJob
        {
            Key = key,
            GenerationId = request.GenerationId,
            Type = type.ToString(),
            ModelId = model.Id,
            Workflow = workflow,
            Files = model.Files.Select(f => new KaggleModelFile { Folder = f.Folder, FileName = f.FileName, SizeBytes = f.SizeBytes, Url = f.DownloadUrl }).ToList(),
            Inputs = inputs,
            TimeoutSeconds = current.KaggleRunTimeoutMinutes * 60,
        });
        coordinator.Signal();
        logger.LogInformation("Queued Kaggle job {Key} ({Type}, {Model})", key, type, model.Id);

        return new ProviderSubmission(key,
        [
            $"Runs on Kaggle in batch mode: each run spends several minutes installing ComfyUI and downloading {model.Name} " +
            $"({model.TotalSizeBytes / 1024d / 1024 / 1024:0.#} GB) before generating, there is no step-by-step progress, and a run cannot be stopped once it has started.",
        ]);
    }

    private static string DescribeRun(KaggleRun run) => run.Status switch
    {
        "RUNNING" => $"running for {Elapsed(run.RunningSince)}",
        _ => $"{run.Status.ToLowerInvariant()} since {Elapsed(run.PushedAt)} ago",
    };

    private static string Elapsed(DateTime? since)
    {
        if (since is null) return "a moment";
        var span = DateTime.UtcNow - since.Value;
        return span.TotalMinutes < 1 ? $"{span.Seconds}s" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m {span.Seconds:00}s" : $"{(int)span.TotalHours}h {span.Minutes:00}m";
    }

    private static MediaType Classify(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" or ".webm" or ".mov" or ".mkv" or ".gif" => MediaType.Video,
        ".wav" or ".mp3" or ".flac" => MediaType.Audio,
        _ => MediaType.Image,
    };
}
