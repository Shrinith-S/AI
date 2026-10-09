using Chitra.Domain.Common;
using Chitra.Domain.Enums;

namespace Chitra.Domain.Entities;

/// <summary>Describes an AI/TTS provider endpoint and whether using it can incur charges.</summary>
public class ProviderConfiguration : Entity
{
    public string Name { get; set; } = string.Empty;
    public ProviderKind Kind { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>True when the provider runs on hardware the user controls.</summary>
    public bool IsLocal { get; set; } = true;

    /// <summary>True when calls are billed by a third party. Blocked while Local-Only mode is on.</summary>
    public bool IncursApiCharges { get; set; }
    public string? SettingsJson { get; set; }
}
