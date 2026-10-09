using Chitra.Application.Dtos;
using Chitra.Domain.Enums;

namespace Chitra.Application.Abstractions;

/// <summary>Stores files under the configured storage root. Paths returned are relative to that root.</summary>
public interface IMediaStorage
{
    string RootPath { get; }

    /// <summary>Saves a stream into a category folder (images, videos, audio, uploads, ...) and returns the relative path.</summary>
    Task<StoredFile> SaveAsync(Stream content, string category, string extension, CancellationToken cancellationToken);

    /// <summary>Reserves a new relative path in a category folder without writing anything.</summary>
    string CreatePath(string category, string extension);

    string GetFullPath(string relativePath);
    bool Exists(string relativePath);
    void Delete(string relativePath);

    /// <summary>Throws a DiskFull error when less than the configured free space remains.</summary>
    void EnsureFreeSpace(long requiredBytes = 0);
}

public sealed record StoredFile(string RelativePath, string FileName, long SizeBytes);

public interface IRealtimeNotifier
{
    Task GenerationUpdatedAsync(Guid userId, GenerationDto generation, CancellationToken cancellationToken = default);
    Task ExportUpdatedAsync(Guid userId, VideoExportDto export, CancellationToken cancellationToken = default);
    Task ModelDownloadUpdatedAsync(ModelDownloadStatusDto status, CancellationToken cancellationToken = default);
}

/// <summary>In-process queue feeding the background generation worker.</summary>
public interface IGenerationQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);

    /// <summary>Signals a running job to stop. Returns false if the job is not currently running.</summary>
    bool TryCancelRunning(Guid generationId);
    CancellationTokenSource RegisterRunning(Guid generationId, CancellationToken stoppingToken);
    void UnregisterRunning(Guid generationId);
}

public interface IExportQueue
{
    ValueTask EnqueueAsync(Guid exportId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
    bool TryCancelRunning(Guid exportId);
    CancellationTokenSource RegisterRunning(Guid exportId, CancellationToken stoppingToken);
    void UnregisterRunning(Guid exportId);
}

/// <summary>
/// Downloads model files into the ComfyUI models folder. Sizes are always shown first and a download only starts
/// after explicit confirmation.
/// </summary>
public interface IModelDownloadService
{
    Task<ModelDownloadPlanDto> GetPlanAsync(string modelId, CancellationToken cancellationToken);
    Task<ModelDownloadStatusDto> StartAsync(string modelId, CancellationToken cancellationToken);
    bool Cancel(string modelId);
    ModelDownloadStatusDto? GetStatus(string modelId);
}

public interface ISystemInfoService
{
    Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Local FFmpeg wrapper. All operations fail with FfmpegUnavailable when FFmpeg cannot be found.</summary>
public interface IFfmpegService
{
    Task<FfmpegStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<MediaProbe?> ProbeAsync(string path, CancellationToken cancellationToken);
    Task<bool> TryCreateThumbnailAsync(string inputPath, string outputPath, MediaType type, CancellationToken cancellationToken);
    Task ExtractLastFrameAsync(string videoPath, string outputPath, CancellationToken cancellationToken);
    Task ComposeAsync(VideoCompositionRequest request, IProgress<int> progress, CancellationToken cancellationToken);
}

public sealed record FfmpegStatus(bool IsAvailable, string? Version, string? Path, string Message);

public sealed record MediaProbe(int? Width, int? Height, double? DurationSeconds, bool HasAudio);

public sealed record VideoCompositionRequest
{
    public required IReadOnlyList<CompositionClip> Clips { get; init; }
    public required string OutputPath { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int Fps { get; init; } = 24;

    /// <summary>"none", "fade", "dissolve", "wipeleft", "slideleft", ... (FFmpeg xfade names).</summary>
    public string Transition { get; init; } = "none";
    public double TransitionSeconds { get; init; } = 0.5;
    public string? MusicPath { get; init; }
    public double MusicVolume { get; init; } = 0.25;
    public bool BurnSubtitles { get; init; }
}

public sealed record CompositionClip(string MediaPath, bool IsStillImage, double DurationSeconds, string? NarrationPath, string? SubtitleText);
