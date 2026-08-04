namespace Kirana.Application.Audit;

/// <summary>Optional filters for the Audit Log screen (PRD §37). All null/empty fields are
/// unfiltered.</summary>
public sealed class AuditLogQuery
{
    public int? UserId { get; init; }
    public string? Action { get; init; }
    public string? Entity { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public int MaxResults { get; init; } = 200;
}
