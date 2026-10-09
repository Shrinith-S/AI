using Chitra.Domain.Common;
using Chitra.Domain.Enums;

namespace Chitra.Domain.Entities;

/// <summary>A request to render a project's scenes into a final video with FFmpeg.</summary>
public class VideoExport : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public GenerationStatus Status { get; set; } = GenerationStatus.Queued;
    public int Progress { get; set; }
    public string? StatusMessage { get; set; }

    /// <summary>Output height: 480, 720 or 1080.</summary>
    public int Resolution { get; set; } = 720;
    public int Fps { get; set; } = 24;
    public string Transition { get; set; } = "none";
    public double TransitionSeconds { get; set; } = 0.5;
    public bool IncludeNarration { get; set; } = true;
    public bool IncludeSubtitles { get; set; }
    public Guid? MusicAssetId { get; set; }
    public double MusicVolume { get; set; } = 0.25;

    public Guid? OutputAssetId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
