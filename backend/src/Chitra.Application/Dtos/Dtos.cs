using Chitra.Domain.Enums;

namespace Chitra.Application.Dtos;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record MediaAssetDto(
    Guid Id,
    MediaType Type,
    MediaSource Source,
    string FileName,
    string? OriginalFileName,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    double? DurationSeconds,
    string Url,
    string DownloadUrl,
    string? ThumbnailUrl,
    Guid? ProjectId,
    Guid? GenerationId,
    DateTime CreatedAt);

public sealed record GenerationDto(
    Guid Id,
    GenerationType Type,
    GenerationStatus Status,
    int Progress,
    string? StatusMessage,
    string Prompt,
    string FinalPrompt,
    string? NegativePrompt,
    bool PromptEnhanced,
    string ModelId,
    string ProviderName,
    Guid? StyleId,
    IReadOnlyList<Guid> CharacterIds,
    int Width,
    int Height,
    int Steps,
    double CfgScale,
    long Seed,
    int BatchSize,
    string? Sampler,
    string? Scheduler,
    double? DurationSeconds,
    int? Fps,
    int? FrameCount,
    double? MotionStrength,
    string? CameraMovement,
    Guid? InputAssetId,
    Guid? ProjectId,
    Guid? SceneId,
    Guid? ParentGenerationId,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<MediaAssetDto> Outputs);

public sealed record CharacterDto(
    Guid Id,
    string Name,
    string? Description,
    int? Age,
    string? Gender,
    string? Clothing,
    string? Appearance,
    string? Personality,
    string? PromptOverride,
    long? PreferredSeed,
    MediaAssetDto? ReferenceAsset,
    string GeneratedPrompt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record StyleDto(
    Guid Id,
    string Name,
    string? Description,
    string? PromptPrefix,
    string? PromptSuffix,
    string? NegativePrompt,
    bool IsBuiltIn);

public sealed record PromptTemplateDto(
    Guid Id,
    string Name,
    string Category,
    string Template,
    string? NegativePrompt,
    string? Description,
    bool IsBuiltIn);

public sealed record SceneGenerationState(Guid Id, GenerationStatus Status, int Progress, string? StatusMessage, string? ErrorMessage);

public sealed record SceneDto(
    Guid Id,
    Guid ProjectId,
    int Order,
    string Title,
    string? Description,
    string? Location,
    string? Action,
    string? Mood,
    string ImagePrompt,
    string VideoPrompt,
    string? NegativePrompt,
    string? CameraMovement,
    double DurationSeconds,
    string? Narration,
    IReadOnlyList<Guid> CharacterIds,
    MediaAssetDto? Image,
    MediaAssetDto? Video,
    MediaAssetDto? NarrationAudio,
    SceneGenerationState? ImageGeneration,
    SceneGenerationState? VideoGeneration);

public sealed record ProjectSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    int SceneCount,
    int CharacterCount,
    int AssetCount,
    string? CoverUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProjectDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? StoryText,
    Guid? StyleId,
    string? ImageModelId,
    string? VideoModelId,
    int Width,
    int Height,
    int Fps,
    MediaAssetDto? FinalVideo,
    IReadOnlyList<SceneDto> Scenes,
    IReadOnlyList<CharacterDto> Characters,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record VideoExportDto(
    Guid Id,
    Guid ProjectId,
    GenerationStatus Status,
    int Progress,
    string? StatusMessage,
    int Resolution,
    int Fps,
    string Transition,
    double TransitionSeconds,
    bool IncludeNarration,
    bool IncludeSubtitles,
    Guid? MusicAssetId,
    double MusicVolume,
    MediaAssetDto? Output,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public sealed record ModelFileStatusDto(
    string Role,
    string FileName,
    string Folder,
    long SizeBytes,
    string? DownloadUrl,
    bool? Installed,
    string? InstalledAs);

public sealed record ModelDto(
    string Id,
    string Name,
    string Family,
    string Provider,
    string? Description,
    IReadOnlyList<GenerationType> Capabilities,
    long TotalSizeBytes,
    double MinVramGb,
    double RecommendedVramGb,
    string InstallState,
    IReadOnlyList<ModelFileStatusDto> Files,
    string Readiness,
    string ReadinessReason,
    IReadOnlyList<string> RequiredCustomNodes,
    string? Notes,
    string? License,
    string? Homepage,
    ModelDefaultsDto Defaults,
    ModelDownloadStatusDto? Download);

public sealed record ModelDefaultsDto(int Width, int Height, int Steps, double Cfg, int NativeFps, int MaxFrames);

public sealed record ModelDownloadStatusDto(
    string ModelId,
    string State,
    string? CurrentFile,
    long BytesDownloaded,
    long TotalBytes,
    int Progress,
    string? Error);

public sealed record ModelDownloadPlanDto(
    string ModelId,
    string ModelName,
    string? TargetDirectory,
    bool CanDownload,
    string? Reason,
    long TotalBytesToDownload,
    long FreeDiskBytes,
    IReadOnlyList<ModelDownloadFilePlanDto> Files);

public sealed record ModelDownloadFilePlanDto(string FileName, string Folder, string? Url, long SizeBytes, bool SizeIsExact, bool AlreadyPresent, string? TargetPath);

public sealed record GpuInfoDto(string Name, long VramTotalMb, long? VramFreeMb, string? DriverVersion, string Source);

public sealed record DiskInfoDto(string Path, long TotalBytes, long FreeBytes);

public sealed record SystemInfoDto(
    string Os,
    string MachineName,
    string CpuName,
    int CpuCores,
    long TotalRamBytes,
    long? AvailableRamBytes,
    IReadOnlyList<GpuInfoDto> Gpus,
    DiskInfoDto? Disk,
    bool NvidiaSmiAvailable,
    IReadOnlyList<string> Notes);

public sealed record ComponentStatusDto(string Name, bool IsAvailable, string Message, string? Detail = null);

public sealed record SystemStatusDto(
    bool LocalOnly,
    string CostNotice,
    ComponentStatusDto AiProvider,
    ComponentStatusDto Ffmpeg,
    ComponentStatusDto Tts,
    ComponentStatusDto Storage,
    SystemInfoDto System,
    string HardwareRating,
    string HardwareRatingReason,
    int QueuedGenerations,
    int ProcessingGenerations);

public sealed record PromptEnhancementDto(string Original, string Prompt, string? NegativePrompt, IReadOnlyList<string> Additions, string Enhancer);

public sealed record TtsVoiceDto(string Id, string Name, string? Language);

public sealed record TtsStatusDto(string Provider, bool IsAvailable, string Message, IReadOnlyList<TtsVoiceDto> Voices);
