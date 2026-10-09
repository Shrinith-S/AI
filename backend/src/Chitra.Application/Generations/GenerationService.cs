using Chitra.Application.Abstractions;
using Chitra.Application.Abstractions.Ai;
using Chitra.Application.Abstractions.Prompting;
using Chitra.Application.Common;
using Chitra.Application.Dtos;
using Chitra.Application.Media;
using Chitra.Application.Prompting;
using Chitra.Application.Settings;
using Chitra.Domain.Entities;
using Chitra.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chitra.Application.Generations;

/// <summary>Normalized description of a generation, independent of which endpoint created it.</summary>
public sealed record GenerationSpec
{
    public required GenerationType Type { get; init; }
    public required string Prompt { get; init; }
    public string? NegativePrompt { get; init; }
    public string? ModelId { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Steps { get; init; }
    public double? CfgScale { get; init; }
    public long? Seed { get; init; }
    public int? BatchSize { get; init; }
    public string? Sampler { get; init; }
    public string? Scheduler { get; init; }
    public Guid? StyleId { get; init; }
    public IReadOnlyList<Guid>? CharacterIds { get; init; }
    public bool EnhancePrompt { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? SceneId { get; init; }
    public Guid? ParentGenerationId { get; init; }
    public double? DurationSeconds { get; init; }
    public int? Fps { get; init; }
    public double? MotionStrength { get; init; }
    public string? CameraMovement { get; init; }
    public Guid? InputAssetId { get; init; }
}

public sealed class GenerationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IModelCatalog catalog,
    IAiProviderRegistry providers,
    IRuntimeSettings settings,
    IGenerationQueue queue,
    IRealtimeNotifier notifier,
    PromptService prompts,
    MediaService media,
    IMediaStorage storage,
    ILogger<GenerationService> logger)
{
    public Task<GenerationDto> CreateImageAsync(CreateImageGenerationRequest r, CancellationToken ct) =>
        CreateAsync(new GenerationSpec
        {
            Type = GenerationType.TextToImage,
            Prompt = r.Prompt,
            NegativePrompt = r.NegativePrompt,
            ModelId = r.ModelId,
            Width = r.Width,
            Height = r.Height,
            Steps = r.Steps,
            CfgScale = r.CfgScale,
            Seed = r.Seed,
            BatchSize = r.BatchSize,
            Sampler = r.Sampler,
            Scheduler = r.Scheduler,
            StyleId = r.StyleId,
            CharacterIds = r.CharacterIds,
            EnhancePrompt = r.EnhancePrompt,
            ProjectId = r.ProjectId,
        }, ct);

    public Task<GenerationDto> CreateVideoAsync(CreateVideoGenerationRequest r, GenerationType type, CancellationToken ct) =>
        CreateAsync(new GenerationSpec
        {
            Type = type,
            Prompt = r.Prompt,
            NegativePrompt = r.NegativePrompt,
            ModelId = r.ModelId,
            Width = r.Width,
            Height = r.Height,
            Steps = r.Steps,
            CfgScale = r.CfgScale,
            Seed = r.Seed,
            StyleId = r.StyleId,
            CharacterIds = r.CharacterIds,
            EnhancePrompt = r.EnhancePrompt,
            ProjectId = r.ProjectId,
            DurationSeconds = r.DurationSeconds,
            Fps = r.Fps,
            MotionStrength = r.MotionStrength,
            CameraMovement = r.CameraMovement,
            InputAssetId = r.InputAssetId,
        }, ct);

    public async Task<GenerationDto> CreateAsync(GenerationSpec spec, CancellationToken ct)
    {
        var current = settings.Current;
        Validate(spec);

        var model = ResolveModel(spec.Type, spec.ModelId, current);
        var provider = providers.GetForModel(model);
        await EnsureProviderReadyAsync(provider, model, ct);

        var userId = currentUser.UserId;
        var style = spec.StyleId is null
            ? null
            : await db.Styles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == spec.StyleId && (s.UserId == null || s.UserId == userId), ct)
              ?? throw new NotFoundException("Style", spec.StyleId);

        var characterIds = spec.CharacterIds?.Distinct().ToList() ?? [];
        var characters = characterIds.Count == 0
            ? []
            : await db.Characters.AsNoTracking().Where(c => characterIds.Contains(c.Id) && c.UserId == userId).ToListAsync(ct);
        if (characters.Count != characterIds.Count)
        {
            throw new NotFoundException("Character", string.Join(", ", characterIds.Except(characters.Select(c => c.Id))));
        }

        MediaAsset? input = null;
        if (spec.Type == GenerationType.ImageToVideo)
        {
            input = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == spec.InputAssetId && a.UserId == userId, ct)
                    ?? throw new NotFoundException("Input image", spec.InputAssetId!);
            if (input.Type != MediaType.Image) throw new ValidationException("inputAssetId", "Image-to-video needs an image as input.");
            if (!storage.Exists(input.RelativePath)) throw new ValidationException("inputAssetId", "The input image file no longer exists on disk.");
        }

        if (spec.ProjectId is { } projectId && !await db.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId, ct))
        {
            throw new NotFoundException("Project", projectId);
        }

        var userPrompt = spec.Prompt.Trim();
        var basePrompt = userPrompt;
        string? enhancedNegative = null;
        if (spec.EnhancePrompt)
        {
            var enhanced = await prompts.Resolve().EnhanceAsync(
                new PromptEnhancementRequest(userPrompt, spec.Type, style?.Name), ct);
            basePrompt = enhanced.Prompt;
            enhancedNegative = enhanced.NegativePrompt;
        }

        var composed = PromptComposer.Compose(
            spec.Type,
            basePrompt,
            string.IsNullOrWhiteSpace(spec.NegativePrompt) ? enhancedNegative : spec.NegativePrompt,
            style,
            characters,
            spec.CameraMovement,
            spec.Type.ProducesVideo() ? spec.MotionStrength ?? 0.5 : null,
            model.Defaults.NegativePrompt);

        var warnings = new List<string>();
        var (defaultW, defaultH) = spec.Type == GenerationType.TextToImage
            ? StudioSettings.ParseResolution(current.DefaultImageResolution, (model.Defaults.Width, model.Defaults.Height))
            : StudioSettings.ParseResolution(current.DefaultVideoResolution, (model.Defaults.Width, model.Defaults.Height));
        var size = GenerationPlanning.PlanSize(model, spec.Width ?? defaultW, spec.Height ?? defaultH);
        warnings.AddRange(size.Warnings);

        var seed = spec.Seed is null or < 0
            ? characters.FirstOrDefault(c => c.PreferredSeed is not null)?.PreferredSeed ?? GenerationPlanning.RandomSeed()
            : spec.Seed.Value;

        var generation = new Generation
        {
            UserId = userId,
            ProjectId = spec.ProjectId,
            SceneId = spec.SceneId,
            ParentGenerationId = spec.ParentGenerationId,
            Type = spec.Type,
            Status = GenerationStatus.Queued,
            StatusMessage = "Queued",
            Prompt = userPrompt,
            FinalPrompt = composed.Prompt,
            NegativePrompt = composed.NegativePrompt,
            PromptEnhanced = spec.EnhancePrompt,
            ModelId = model.Id,
            ProviderName = provider.Name,
            StyleId = style?.Id,
            CharacterIds = characters.Count == 0 ? null : string.Join(',', characters.Select(c => c.Id)),
            Width = size.Width,
            Height = size.Height,
            Steps = Math.Clamp(spec.Steps ?? model.Defaults.Steps, 1, 150),
            CfgScale = Math.Clamp(spec.CfgScale ?? model.Defaults.Cfg, 0, 30),
            Seed = seed,
            BatchSize = spec.Type == GenerationType.TextToImage ? Math.Clamp(spec.BatchSize ?? 1, 1, 4) : 1,
            Sampler = spec.Sampler ?? model.Defaults.Sampler,
            Scheduler = spec.Scheduler ?? model.Defaults.Scheduler,
            InputAssetId = input?.Id,
        };

        if (spec.Type.ProducesVideo())
        {
            var fps = spec.Fps ?? current.DefaultFps;
            var frames = GenerationPlanning.PlanFrames(model, spec.DurationSeconds ?? 5, fps);
            warnings.AddRange(frames.Warnings);
            generation.Fps = fps;
            generation.FrameCount = frames.FrameCount;
            generation.DurationSeconds = frames.ActualDurationSeconds;
            generation.MotionStrength = Math.Clamp(spec.MotionStrength ?? 0.5, 0, 1);
            generation.CameraMovement = spec.CameraMovement;
        }

        generation.Warnings = warnings.Count == 0 ? null : string.Join('\n', warnings);

        var job = new GenerationJob
        {
            GenerationId = generation.Id,
            UserId = userId,
            Type = generation.Type,
            Prompt = generation.FinalPrompt,
            Model = model.Id,
            ProviderName = provider.Name,
        };

        db.Generations.Add(generation);
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync(ct);

        await queue.EnqueueAsync(job.Id, ct);
        var dto = generation.ToDto();
        await notifier.GenerationUpdatedAsync(userId, dto, ct);
        logger.LogInformation("Queued {Type} generation {GenerationId} on {Provider}/{Model}", generation.Type, generation.Id, provider.Name, model.Id);
        return dto;
    }

    public async Task<GenerationDto> RegenerateAsync(Guid id, bool newSeed, CancellationToken ct)
    {
        var source = await FindOwnedAsync(id, ct);
        return await CreateAsync(new GenerationSpec
        {
            Type = source.Type,
            Prompt = source.Prompt,
            NegativePrompt = source.NegativePrompt,
            ModelId = source.ModelId,
            Width = source.Width,
            Height = source.Height,
            Steps = source.Steps,
            CfgScale = source.CfgScale,
            Seed = newSeed ? null : source.Seed,
            BatchSize = source.BatchSize,
            Sampler = source.Sampler,
            Scheduler = source.Scheduler,
            StyleId = source.StyleId,
            CharacterIds = source.GetCharacterIds(),
            EnhancePrompt = source.PromptEnhanced,
            ProjectId = source.ProjectId,
            SceneId = source.SceneId,
            ParentGenerationId = source.Id,
            DurationSeconds = source.FrameCount is { } frames && source.Fps is { } fps ? frames / (double)fps : source.DurationSeconds,
            Fps = source.Fps,
            MotionStrength = source.MotionStrength,
            CameraMovement = source.CameraMovement,
            InputAssetId = source.InputAssetId,
        }, ct);
    }

    public async Task<PagedResult<GenerationDto>> ListAsync(GenerationQuery q, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var query = db.Generations.AsNoTracking().Where(g => g.UserId == userId);

        query = q.Filter?.ToLowerInvariant() switch
        {
            "images" => query.Where(g => g.Type == GenerationType.TextToImage),
            "videos" => query.Where(g => g.Type == GenerationType.TextToVideo || g.Type == GenerationType.ImageToVideo),
            "stories" => query.Where(g => g.SceneId != null),
            "failed" => query.Where(g => g.Status == GenerationStatus.Failed),
            "completed" => query.Where(g => g.Status == GenerationStatus.Completed),
            "processing" => query.Where(g => g.Status == GenerationStatus.Queued || g.Status == GenerationStatus.Processing),
            _ => query,
        };

        if (q.Type is { } type) query = query.Where(g => g.Type == type);
        if (q.Status is { } status) query = query.Where(g => g.Status == status);
        if (q.ProjectId is { } projectId) query = query.Where(g => g.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            query = query.Where(g => g.Prompt.ToLower().Contains(term));
        }

        var pageSize = Math.Clamp(q.PageSize, 1, 100);
        var page = Math.Max(1, q.Page);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(g => g.Outputs)
            .ToListAsync(ct);

        return new PagedResult<GenerationDto>(items.Select(g => g.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<GenerationDto> GetAsync(Guid id, CancellationToken ct) => (await FindOwnedAsync(id, ct)).ToDto();

    public async Task<GenerationDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var generation = await FindOwnedAsync(id, ct, tracking: true);
        if (generation.Status.IsTerminal()) return generation.ToDto();

        generation.Status = GenerationStatus.Cancelled;
        generation.StatusMessage = "Cancelled by user";
        generation.CompletedAt = DateTime.UtcNow;
        generation.Touch();
        var jobs = await db.GenerationJobs.Where(j => j.GenerationId == id && j.Status != GenerationStatus.Completed).ToListAsync(ct);
        foreach (var job in jobs)
        {
            job.Status = GenerationStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // A running job is interrupted by the worker, which also cancels it on the provider.
        if (!queue.TryCancelRunning(id))
        {
            var providerJobId = jobs.Select(j => j.ProviderJobId).FirstOrDefault(p => p is not null);
            if (providerJobId is not null)
            {
                try
                {
                    await providers.Get(generation.ProviderName).CancelAsync(providerJobId, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not cancel provider job {ProviderJobId}", providerJobId);
                }
            }
        }

        var dto = generation.ToDto();
        await notifier.GenerationUpdatedAsync(generation.UserId, dto, ct);
        return dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var generation = await FindOwnedAsync(id, ct, tracking: true);
        if (!generation.Status.IsTerminal())
        {
            await CancelAsync(id, ct);
        }

        foreach (var output in generation.Outputs.ToList())
        {
            await media.DeleteAssetAsync(output, ct);
        }

        await db.Scenes.Where(s => s.LastImageGenerationId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastImageGenerationId, (Guid?)null), ct);
        await db.Scenes.Where(s => s.LastVideoGenerationId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastVideoGenerationId, (Guid?)null), ct);
        db.Generations.Remove(generation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<(int Queued, int Processing)> GetQueueCountsAsync(CancellationToken ct)
    {
        var counts = await db.Generations
            .Where(g => g.Status == GenerationStatus.Queued || g.Status == GenerationStatus.Processing)
            .GroupBy(g => g.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return (counts.FirstOrDefault(c => c.Key == GenerationStatus.Queued)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Key == GenerationStatus.Processing)?.Count ?? 0);
    }

    private async Task<Generation> FindOwnedAsync(Guid id, CancellationToken ct, bool tracking = false)
    {
        var query = tracking ? db.Generations : db.Generations.AsNoTracking();
        return await query.Include(g => g.Outputs).FirstOrDefaultAsync(g => g.Id == id && g.UserId == currentUser.UserId, ct)
               ?? throw new NotFoundException("Generation", id);
    }

    private AiModelDefinition ResolveModel(GenerationType type, string? modelId, StudioSettings current)
    {
        var id = !string.IsNullOrWhiteSpace(modelId)
            ? modelId
            : type switch
            {
                GenerationType.TextToImage => current.DefaultImageModel,
                GenerationType.TextToVideo => current.DefaultVideoModel,
                _ => current.DefaultImageToVideoModel,
            };

        var model = catalog.Find(id) ?? throw new ValidationException("modelId", $"Unknown model '{id}'. See the Models page for available models.");
        if (!model.Supports(type))
        {
            var alternatives = string.Join(", ", catalog.All.Where(m => m.Supports(type)).Select(m => m.Name));
            throw new ValidationException("modelId", $"{model.Name} does not support {type}. Models that do: {alternatives}.");
        }

        return model;
    }

    private static async Task EnsureProviderReadyAsync(IAiGenerationProvider provider, AiModelDefinition model, CancellationToken ct)
    {
        var health = await provider.CheckHealthAsync(ct);
        if (!health.IsAvailable)
        {
            throw new AppException(ErrorCode.ProviderUnavailable, health.Message);
        }

        var inventory = await provider.GetModelInventoryAsync(ct);
        if (inventory is null) return;

        var missing = model.Files.Where(f => inventory.Find(f.Folder, f.FileName) is null).ToList();
        if (missing.Count > 0)
        {
            var list = string.Join(", ", missing.Select(f => $"{f.FileName} (models/{f.Folder})"));
            throw new AppException(ErrorCode.ModelMissing,
                $"{model.Name} is not fully installed in {provider.Name}. Missing: {list}. Install it from the Models page or copy the files into ComfyUI and press Refresh in ComfyUI.");
        }
    }

    private static void Validate(GenerationSpec spec)
    {
        var v = new Validator()
            .NotEmpty(spec.Prompt, "prompt", 4000)
            .Require(spec.NegativePrompt is null || spec.NegativePrompt.Length <= 2000, "negativePrompt", "Negative prompt must be at most 2000 characters.")
            .Range(spec.Width, 64, 4096, "width")
            .Range(spec.Height, 64, 4096, "height")
            .Range(spec.Steps, 1, 150, "steps")
            .Range(spec.CfgScale, 0, 30, "cfgScale")
            .Range(spec.BatchSize, 1, 4, "batchSize");

        if (spec.Type.ProducesVideo())
        {
            v.Range(spec.DurationSeconds, 1, 20, "durationSeconds")
                .Require(spec.Fps is null or 8 or 12 or 16 or 24 or 25 or 30, "fps", "FPS must be one of 8, 12, 16, 24, 25 or 30.")
                .Range(spec.MotionStrength, 0, 1, "motionStrength");
        }

        if (spec.Type == GenerationType.ImageToVideo)
        {
            v.Require(spec.InputAssetId is not null, "inputAssetId", "Upload or choose an input image for image-to-video.");
        }

        v.ThrowIfInvalid();
    }
}
