using Chitra.Domain.Common;
using Chitra.Domain.Enums;

namespace Chitra.Domain.Entities;

/// <summary>One execution attempt of a <see cref="Generation"/> on an AI provider.</summary>
public class GenerationJob : Entity, IUserOwned
{
    public Guid GenerationId { get; set; }
    public Generation? Generation { get; set; }

    public Guid UserId { get; set; }
    public GenerationType Type { get; set; }

    /// <summary>Exact prompt submitted to the provider.</summary>
    public string Prompt { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Identifier assigned by the provider (e.g. ComfyUI prompt_id).</summary>
    public string? ProviderJobId { get; set; }

    public GenerationStatus Status { get; set; } = GenerationStatus.Queued;
    public int Progress { get; set; }
    public int Attempt { get; set; } = 1;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
}
