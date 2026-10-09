using Chitra.Domain.Common;

namespace Chitra.Domain.Entities;

/// <summary>A reusable character whose description is injected into prompts for visual consistency.</summary>
public class Character : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Clothing { get; set; }
    public string? Appearance { get; set; }
    public string? Personality { get; set; }

    /// <summary>Optional hand-written prompt that replaces the auto-generated one.</summary>
    public string? PromptOverride { get; set; }

    /// <summary>Optional fixed seed; reusing a seed helps keep a character's look stable.</summary>
    public long? PreferredSeed { get; set; }

    public Guid? ReferenceAssetId { get; set; }
    public MediaAsset? ReferenceAsset { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();

    /// <summary>
    /// Builds the visual description without the name, e.g.
    /// "65-year-old man, grey beard, thin face, wearing a simple beige kurta and worn sandals".
    /// Personality is deliberately excluded: it is not visual and tends to confuse image models.
    /// </summary>
    public string BuildDescription()
    {
        if (!string.IsNullOrWhiteSpace(PromptOverride))
        {
            return PromptOverride.Trim().TrimEnd('.');
        }

        var parts = new List<string>();
        var identity = string.Join(' ', new[]
        {
            Age is > 0 ? $"{Age}-year-old" : null,
            string.IsNullOrWhiteSpace(Gender) ? null : Gender.Trim().ToLowerInvariant(),
        }.Where(p => p is not null));

        if (!string.IsNullOrWhiteSpace(identity)) parts.Add(identity);
        if (!string.IsNullOrWhiteSpace(Appearance)) parts.Add(Appearance.Trim().TrimEnd('.'));
        if (!string.IsNullOrWhiteSpace(Clothing)) parts.Add($"wearing {Clothing.Trim().TrimEnd('.')}");
        if (!string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(Appearance))
        {
            parts.Add(Description.Trim().TrimEnd('.'));
        }

        return string.Join(", ", parts);
    }

    /// <summary>The reusable prompt including the name, e.g. "Ramu, 65-year-old man, grey beard, ...".</summary>
    public string BuildPrompt()
    {
        var description = BuildDescription();
        return string.IsNullOrEmpty(description) ? Name.Trim() : $"{Name.Trim()}, {description}";
    }
}
