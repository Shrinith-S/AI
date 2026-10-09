using Chitra.Domain.Common;

namespace Chitra.Domain.Entities;

/// <summary>A saved prompt with optional {subject}-style placeholders.</summary>
public class PromptTemplate : Entity
{
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Image";
    public string Template { get; set; } = string.Empty;
    public string? NegativePrompt { get; set; }
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
}
