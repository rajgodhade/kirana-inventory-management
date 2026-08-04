namespace Kirana.Application.Abstractions;

/// <summary>
/// Append-only audit trail writer (PRD §37). Application services call this after a
/// sensitive action succeeds; nothing in the app should ever update/delete an AuditLog row.
/// </summary>
public interface IAuditLogger
{
    Task RecordAsync(
        int? userId,
        string action,
        string entity,
        string? entityId = null,
        string? previousValue = null,
        string? newValue = null,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
