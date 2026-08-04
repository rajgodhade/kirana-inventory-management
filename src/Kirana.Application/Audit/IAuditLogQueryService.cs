using Kirana.Domain.Entities;

namespace Kirana.Application.Audit;

/// <summary>
/// Read-side of the audit trail (PRD §37) — deliberately separate from
/// <see cref="Abstractions.IAuditLogger"/>, which is write-only by design (audit rows are
/// append-only and never updated/deleted through normal application functionality). Requires
/// <see cref="PermissionKeys.AuditLogView"/>.
/// </summary>
public interface IAuditLogQueryService
{
    Task<IReadOnlyList<AuditLog>> SearchAsync(AuditLogQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Distinct action strings seen so far, for populating a filter dropdown.</summary>
    Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken cancellationToken = default);
}
