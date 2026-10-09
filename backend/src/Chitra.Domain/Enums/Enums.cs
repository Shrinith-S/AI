namespace Chitra.Domain.Enums;

public enum GenerationType
{
    TextToImage = 1,
    TextToVideo = 2,
    ImageToVideo = 3,
}

public enum GenerationStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

public enum MediaType
{
    Image = 1,
    Video = 2,
    Audio = 3,
}

public enum MediaSource
{
    Generated = 1,
    Uploaded = 2,
    Narration = 3,
    Export = 4,
}

public enum ProviderKind
{
    ImageVideo = 1,
    TextToSpeech = 2,
    PromptLlm = 3,
}

public static class GenerationStatusExtensions
{
    public static bool IsTerminal(this GenerationStatus status) =>
        status is GenerationStatus.Completed or GenerationStatus.Failed or GenerationStatus.Cancelled;
}

public static class GenerationTypeExtensions
{
    public static bool ProducesVideo(this GenerationType type) =>
        type is GenerationType.TextToVideo or GenerationType.ImageToVideo;
}
