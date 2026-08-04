using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Immutable record of a sensitive action (PRD §37). Application code must never expose
/// an update/delete path for this entity — only inserts.
/// </summary>
public class AuditLog : Entity
{
    public int? UserId { get; set; }
    public User? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
}
