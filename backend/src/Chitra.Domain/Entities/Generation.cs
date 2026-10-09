using Chitra.Domain.Common;
using Chitra.Domain.Enums;

namespace Chitra.Domain.Entities;

/// <summary>
/// A user's generation request and its result. Execution attempts are tracked by <see cref="GenerationJob"/>.
/// </summary>
public class Generation : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? SceneId { get; set; }

    /// <summary>Source generation for regenerations and variations.</summary>
    public Guid? ParentGenerationId { get; set; }

    public GenerationType Type { get; set; }
    public GenerationStatus Status { get; set; } = GenerationStatus.Queued;
    public int Progress { get; set; }
    public string? StatusMessage { get; set; }

    /// <summary>The prompt as typed by the user.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>The final prompt sent to the model after enhancement, style and character injection.</summary>
    public string FinalPrompt { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public bool PromptEnhanced { get; set; }

    public string ModelId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public Guid? StyleId { get; set; }

    /// <summary>Comma-separated character ids whose descriptions were injected.</summary>
    public string? CharacterIds { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public int Steps { get; set; }
    public double CfgScale { get; set; }
    public long Seed { get; set; }
    public int BatchSize { get; set; } = 1;
    public string? Sampler { get; set; }
    public string? Scheduler { get; set; }

    // Video-only parameters
    public double? DurationSeconds { get; set; }
    public int? Fps { get; set; }
    public int? FrameCount { get; set; }
    public double? MotionStrength { get; set; }
    public string? CameraMovement { get; set; }
    public Guid? InputAssetId { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Relative path of the primary output file.</summary>
    public string? OutputPath { get; set; }
    public Guid? OutputAssetId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Non-fatal adjustments, e.g. a frame count clamped to the model maximum.</summary>
    public string? Warnings { get; set; }

    public ICollection<MediaAsset> Outputs { get; set; } = new List<MediaAsset>();
    public ICollection<GenerationJob> Jobs { get; set; } = new List<GenerationJob>();

    public IReadOnlyList<Guid> GetCharacterIds() =>
        string.IsNullOrWhiteSpace(CharacterIds)
            ? []
            : CharacterIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
}
