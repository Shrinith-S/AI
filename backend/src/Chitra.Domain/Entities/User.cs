using Chitra.Domain.Common;

namespace Chitra.Domain.Entities;

public class User : Entity
{
    /// <summary>Well-known id of the built-in local user used when authentication is disabled.</summary>
    public static readonly Guid LocalUserId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Subject/object id from an external identity provider (null for the local user).</summary>
    public string? ExternalId { get; set; }
    public string DisplayName { get; set; } = "Local User";
    public string? Email { get; set; }
    public bool IsLocal { get; set; }
}
