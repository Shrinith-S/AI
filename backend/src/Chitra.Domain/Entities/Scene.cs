using Chitra.Domain.Common;

namespace Chitra.Domain.Entities;

/// <summary>One shot of a story video. Every field is user-editable before generation.</summary>
public class Scene : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Zero-based position on the timeline.</summary>
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? Action { get; set; }
    public string? Mood { get; set; }

    public string ImagePrompt { get; set; } = string.Empty;
    public string VideoPrompt { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public string? CameraMovement { get; set; }
    public double DurationSeconds { get; set; } = 5;

    /// <summary>Narration text used for voice-over and subtitles.</summary>
    public string? Narration { get; set; }

    public Guid? ImageAssetId { get; set; }
    public Guid? VideoAssetId { get; set; }
    public Guid? NarrationAssetId { get; set; }

    public Guid? LastImageGenerationId { get; set; }
    public Guid? LastVideoGenerationId { get; set; }

    public ICollection<Character> Characters { get; set; } = new List<Character>();
}
