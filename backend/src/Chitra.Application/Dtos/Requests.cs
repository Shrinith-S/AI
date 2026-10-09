using Chitra.Domain.Enums;

namespace Chitra.Application.Dtos;

public sealed record CreateImageGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string? NegativePrompt { get; init; }
    public string? ModelId { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Steps { get; init; }
    public double? CfgScale { get; init; }

    /// <summary>Null or negative means random.</summary>
    public long? Seed { get; init; }
    public int? BatchSize { get; init; }
    public string? Sampler { get; init; }
    public string? Scheduler { get; init; }
    public Guid? StyleId { get; init; }
    public IReadOnlyList<Guid>? CharacterIds { get; init; }
    public bool EnhancePrompt { get; init; }
    public Guid? ProjectId { get; init; }
}

public sealed record CreateVideoGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string? NegativePrompt { get; init; }
    public string? ModelId { get; init; }
    public double DurationSeconds { get; init; } = 5;
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Fps { get; init; }
    public int? Steps { get; init; }
    public double? CfgScale { get; init; }
    public long? Seed { get; init; }

    /// <summary>0 = almost still, 1 = very dynamic.</summary>
    public double MotionStrength { get; init; } = 0.5;
    public string? CameraMovement { get; init; }
    public Guid? StyleId { get; init; }
    public IReadOnlyList<Guid>? CharacterIds { get; init; }
    public bool EnhancePrompt { get; init; }
    public Guid? ProjectId { get; init; }

    /// <summary>Required for image-to-video.</summary>
    public Guid? InputAssetId { get; init; }
}

public sealed record GenerationQuery
{
    /// <summary>images | videos | stories | failed | completed | processing</summary>
    public string? Filter { get; init; }
    public GenerationType? Type { get; init; }
    public GenerationStatus? Status { get; init; }
    public Guid? ProjectId { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 24;
}

public sealed record RegenerateRequest
{
    /// <summary>When true a new random seed is used (a variation); otherwise the original seed is reused.</summary>
    public bool NewSeed { get; init; }
}

public sealed record UpsertCharacterRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? Age { get; init; }
    public string? Gender { get; init; }
    public string? Clothing { get; init; }
    public string? Appearance { get; init; }
    public string? Personality { get; init; }
    public string? PromptOverride { get; init; }
    public long? PreferredSeed { get; init; }
    public Guid? ReferenceAssetId { get; init; }
}

public sealed record UpsertStyleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? PromptPrefix { get; init; }
    public string? PromptSuffix { get; init; }
    public string? NegativePrompt { get; init; }
}

public sealed record UpsertPromptTemplateRequest
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = "Image";
    public string Template { get; init; } = string.Empty;
    public string? NegativePrompt { get; init; }
    public string? Description { get; init; }
}

public sealed record UpsertProjectRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? StoryText { get; init; }
    public Guid? StyleId { get; init; }
    public string? ImageModelId { get; init; }
    public string? VideoModelId { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Fps { get; init; }
    public IReadOnlyList<Guid>? CharacterIds { get; init; }
}

public sealed record UpsertSceneRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Location { get; init; }
    public string? Action { get; init; }
    public string? Mood { get; init; }
    public string ImagePrompt { get; init; } = string.Empty;
    public string VideoPrompt { get; init; } = string.Empty;
    public string? NegativePrompt { get; init; }
    public string? CameraMovement { get; init; }
    public double DurationSeconds { get; init; } = 5;
    public string? Narration { get; init; }
    public IReadOnlyList<Guid>? CharacterIds { get; init; }

    /// <summary>Optional position; appended at the end when null.</summary>
    public int? Order { get; init; }
}

public sealed record ReorderScenesRequest(IReadOnlyList<Guid> SceneIds);

public sealed record SetSceneMediaRequest(Guid? ImageAssetId, Guid? VideoAssetId);

/// <summary>image = keyframe, video = text-to-video, animate = image-to-video from the scene keyframe.</summary>
public sealed record GenerateSceneRequest
{
    public string Kind { get; init; } = "image";
    public string? ModelId { get; init; }
    public long? Seed { get; init; }
    public int? Steps { get; init; }
    public double? CfgScale { get; init; }
    public double MotionStrength { get; init; } = 0.5;
    public bool EnhancePrompt { get; init; }
}

public sealed record GenerateStoryRequest
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SceneCount { get; init; } = 5;
    public double SceneDurationSeconds { get; init; } = 5;
    public Guid? StyleId { get; init; }
    public IReadOnlyList<Guid>? CharacterIds { get; init; }

    /// <summary>Existing project to add scenes to. A new project is created when null.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>When true, existing scenes of the project are replaced.</summary>
    public bool ReplaceExistingScenes { get; init; }
}

public sealed record AttachAssetRequest(Guid? AssetId, Guid? GenerationId);

public sealed record CreateExportRequest
{
    public int Resolution { get; init; } = 720;
    public int Fps { get; init; } = 24;
    public string Transition { get; init; } = "none";
    public double TransitionSeconds { get; init; } = 0.5;
    public bool IncludeNarration { get; init; } = true;
    public bool IncludeSubtitles { get; init; }
    public Guid? MusicAssetId { get; init; }
    public double MusicVolume { get; init; } = 0.25;
}

public sealed record EnhancePromptRequest
{
    public string Prompt { get; init; } = string.Empty;
    public GenerationType Type { get; init; } = GenerationType.TextToImage;
    public string? StyleHint { get; init; }
    public int Variation { get; init; }
}

public sealed record SynthesizeSpeechRequest
{
    public string Text { get; init; } = string.Empty;
    public string? VoiceId { get; init; }
    public double Speed { get; init; } = 1.0;
    public Guid? ProjectId { get; init; }
}

public sealed record ModelDownloadRequest
{
    /// <summary>Must be true: downloads are never started without explicit confirmation of the sizes shown.</summary>
    public bool Confirm { get; init; }
}
