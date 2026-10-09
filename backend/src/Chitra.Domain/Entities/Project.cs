using Chitra.Domain.Common;

namespace Chitra.Domain.Entities;

/// <summary>Groups characters, scenes, generated media, audio and the final exported video.</summary>
public class Project : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The story synopsis used by the story generator.</summary>
    public string? StoryText { get; set; }

    public Guid? StyleId { get; set; }
    public Style? Style { get; set; }

    public string? ImageModelId { get; set; }
    public string? VideoModelId { get; set; }
    public int Width { get; set; } = 768;
    public int Height { get; set; } = 432;
    public int Fps { get; set; } = 16;

    public Guid? FinalVideoAssetId { get; set; }
    public Guid? CoverAssetId { get; set; }

    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();
    public ICollection<Character> Characters { get; set; } = new List<Character>();
    public ICollection<MediaAsset> Assets { get; set; } = new List<MediaAsset>();
}
