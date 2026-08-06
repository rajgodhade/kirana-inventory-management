namespace Kirana.Application.Maintenance;

public sealed class IntegrityCheckResult
{
    public bool IsHealthy { get; init; }

    /// <summary>SQLite returns the single row "ok" for a healthy file, or one row per problem found.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];
}

public sealed class DatabaseStatistics
{
    public required string DatabaseFilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required long PageCount { get; init; }
    public required long PageSize { get; init; }
    public required long FreePageCount { get; init; }
    public required IReadOnlyList<(string Table, long RowCount)> TableRowCounts { get; init; }
    public DateTime? LastBackupUtc { get; init; }
    public DateTime? LastVacuumUtc { get; init; }
    public DateTime? LastAnalyzeUtc { get; init; }

    /// <summary>Space SQLite is holding but not using — what a VACUUM would reclaim.</summary>
    public long ReclaimableBytes => FreePageCount * PageSize;
}

/// <summary>One class of dangling reference found by the read-only diagnostics. Reported for
/// manual review only — Kirana never auto-deletes business rows.</summary>
public sealed class OrphanRecordGroup
{
    public required string Description { get; init; }
    public required long Count { get; init; }

    /// <summary>A handful of offending row ids, enough to investigate without dumping the table.</summary>
    public required IReadOnlyList<int> SampleIds { get; init; }
}

public sealed class MaintenanceOperationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Details { get; init; }
}
