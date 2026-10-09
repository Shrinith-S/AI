namespace Chitra.Domain.Common;

/// <summary>Base type for all persisted entities. Timestamps are always UTC.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void Touch() => UpdatedAt = DateTime.UtcNow;
}

/// <summary>Entities that belong to a single user.</summary>
public interface IUserOwned
{
    Guid UserId { get; set; }
}
