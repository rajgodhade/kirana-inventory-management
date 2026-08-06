namespace Kirana.Application.Maintenance;

/// <summary>
/// SQLite housekeeping tools (PRD §39). Everything here is either a documented SQLite maintenance
/// command (which by definition rewrites storage without changing what the data means) or a
/// read-only diagnostic — no method on this interface edits business data.
/// </summary>
public interface IDatabaseMaintenanceService
{
    Task<IntegrityCheckResult> RunIntegrityCheckAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Rebuilds the database file, reclaiming free pages left by deletes. Row data is
    /// unchanged — asserted by tests, not just assumed.</summary>
    Task<MaintenanceOperationResult> VacuumAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes SQLite's query planner statistics. Affects plan selection only.</summary>
    Task<MaintenanceOperationResult> AnalyzeAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    Task<DatabaseStatistics> GetStatisticsAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Read-only diagnostics for rows whose parent no longer exists. Returns findings for
    /// manual review; never deletes or modifies anything.</summary>
    Task<IReadOnlyList<OrphanRecordGroup>> FindOrphanRecordsAsync(int? performedByUserId, CancellationToken cancellationToken = default);
}
