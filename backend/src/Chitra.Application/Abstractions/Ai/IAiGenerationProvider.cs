using Chitra.Domain.Enums;

namespace Chitra.Application.Abstractions.Ai;

/// <summary>
/// An engine that turns prompts into images and videos. Business logic only talks to this interface,
/// so ComfyUI can be replaced or complemented by other local or hosted engines.
/// </summary>
public interface IAiGenerationProvider
{
    /// <summary>Stable name used in configuration, e.g. "ComfyUI".</summary>
    string Name { get; }

    /// <summary>True when inference runs on hardware the user controls.</summary>
    bool IsLocal { get; }

    /// <summary>True when a third party bills per request. Such providers are blocked in Local-Only mode.</summary>
    bool IncursApiCharges { get; }

    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken);

    Task<ProviderSubmission> GenerateImageAsync(ImageGenerationRequest request, CancellationToken cancellationToken);

    Task<ProviderSubmission> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken cancellationToken);

    Task<ProviderSubmission> GenerateImageToVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken);

    Task<ProviderJobStatus> GetGenerationStatusAsync(string providerJobId, CancellationToken cancellationToken);

    Task<Stream> OpenOutputAsync(ProviderOutputFile output, CancellationToken cancellationToken);

    Task CancelAsync(string providerJobId, CancellationToken cancellationToken);

    /// <summary>Model files the engine reports as installed, or null when the engine cannot be queried.</summary>
    Task<ModelInventory?> GetModelInventoryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Longest time one job may take on this provider, including any queueing it does itself.
    /// Null uses the image/video timeouts from settings.
    /// </summary>
    TimeSpan? MaxJobDuration => null;

    /// <summary>True when the provider fetches model files itself for every run (e.g. a fresh Kaggle session).</summary>
    bool DownloadsModelsOnDemand => false;

    /// <summary>False when a job that already started cannot be stopped (cancellation only discards its result).</summary>
    bool CanCancelRunningJobs => true;

    /// <summary>
    /// True when the provider batches and schedules jobs itself (Kaggle). Such jobs mostly wait remotely, so they do
    /// not count against "Max concurrent generations" and can be submitted together.
    /// </summary>
    bool ManagesOwnQueue => false;
}

public interface IAiProviderRegistry
{
    IReadOnlyList<IAiGenerationProvider> All { get; }

    /// <summary>Returns the named provider, enforcing Local-Only mode.</summary>
    IAiGenerationProvider Get(string name);

    /// <summary>
    /// Provider that should execute a model: the model's own engine, or the configured remote executor for that
    /// engine (e.g. Kaggle runs ComfyUI workflows when Settings → AI provider is "Kaggle").
    /// </summary>
    IAiGenerationProvider GetForModel(AiModelDefinition model);
}

public sealed record ProviderHealth(bool IsAvailable, string Message, string? Version = null, IReadOnlyList<ProviderDevice>? Devices = null);

public sealed record ProviderDevice(string Name, string Type, long VramTotalBytes, long VramFreeBytes);

public sealed record ProviderSubmission(string ProviderJobId, IReadOnlyList<string> Warnings);

public enum ProviderJobState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Unknown,
}

public sealed record ProviderJobStatus(
    ProviderJobState State,
    int Progress,
    string? Message,
    IReadOnlyList<ProviderOutputFile> Outputs,
    Common.ErrorCode? ErrorCode = null,
    string? Error = null);

/// <summary>A provider output. <paramref name="Locator"/> is opaque provider-specific data used to download it.</summary>
public sealed record ProviderOutputFile(string FileName, MediaType MediaType, string Locator);

/// <summary>Model files keyed by model folder (checkpoints, diffusion_models, vae, ...).</summary>
public sealed record ModelInventory(IReadOnlyDictionary<string, IReadOnlyList<string>> FilesByFolder)
{
    /// <summary>Finds an installed file by name, returning the engine-relative path (which may include a subfolder).</summary>
    public string? Find(string folder, string fileName)
    {
        if (!FilesByFolder.TryGetValue(folder, out var files)) return null;
        return files.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase));
    }
}

public abstract record ProviderGenerationRequest
{
    public required Guid GenerationId { get; init; }
    public required AiModelDefinition Model { get; init; }
    public required string Prompt { get; init; }
    public string NegativePrompt { get; init; } = string.Empty;
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Steps { get; init; }
    public required double CfgScale { get; init; }
    public required long Seed { get; init; }
    public string? Sampler { get; init; }
    public string? Scheduler { get; init; }
}

public sealed record ImageGenerationRequest : ProviderGenerationRequest
{
    public int BatchSize { get; init; } = 1;
}

public record VideoGenerationRequest : ProviderGenerationRequest
{
    public required int FrameCount { get; init; }
    public required int Fps { get; init; }
    public double MotionStrength { get; init; } = 0.5;
}

public sealed record ImageToVideoRequest : VideoGenerationRequest
{
    /// <summary>Absolute path of the source image on local disk.</summary>
    public required string InputImagePath { get; init; }
}
