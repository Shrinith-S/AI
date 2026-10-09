using Chitra.Domain.Common;
using Chitra.Domain.Enums;

namespace Chitra.Domain.Entities;

/// <summary>Metadata for a file stored on disk. The database never stores file contents.</summary>
public class MediaAsset : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? GenerationId { get; set; }

    public MediaType Type { get; set; }
    public MediaSource Source { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }

    /// <summary>Path relative to the configured storage root, using forward slashes.</summary>
    public string RelativePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSeconds { get; set; }

    /// <summary>Relative path of a JPEG thumbnail, when one could be produced.</summary>
    public string? ThumbnailPath { get; set; }

    public Project? Project { get; set; }
    public Generation? Generation { get; set; }
}
