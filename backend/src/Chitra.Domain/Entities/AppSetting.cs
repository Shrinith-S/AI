namespace Chitra.Domain.Entities;

/// <summary>Key/value store for user-editable application settings.</summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
