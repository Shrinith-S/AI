using Chitra.Domain.Common;

namespace Chitra.Domain.Entities;

/// <summary>A reusable visual style applied around the user's prompt.</summary>
public class Style : Entity
{
    /// <summary>Null for built-in styles that every user can see.</summary>
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PromptPrefix { get; set; }
    public string? PromptSuffix { get; set; }
    public string? NegativePrompt { get; set; }
    public bool IsBuiltIn { get; set; }
    public Guid? PreviewAssetId { get; set; }

    public string Apply(string prompt)
    {
        var parts = new[] { PromptPrefix, prompt, PromptSuffix }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim().Trim(',').Trim());
        return string.Join(", ", parts);
    }
}
