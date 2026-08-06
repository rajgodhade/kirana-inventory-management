using Kirana.Application.Backup;

namespace Kirana.Application.Restore;

/// <summary>What the restore screen shows the operator before they commit — the bundle's own
/// manifest plus a row-count summary read from the backup's database copy, so "am I about to
/// restore the right one?" is answerable without guessing from the filename.</summary>
public sealed class BackupInfo
{
    public required string FilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required BackupManifest Manifest { get; init; }
    public required IReadOnlyList<(string Table, long RowCount)> RowCounts { get; init; }
}

public sealed class RestoreResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Path of the safety backup taken of the pre-restore database. Present on success and
    /// on any failure that happened after the safety backup was written, so the operator always
    /// knows where their previous data went.</summary>
    public string? SafetyBackupPath { get; init; }

    /// <summary>Non-fatal problems — currently only asset-file copy failures, which never abort a
    /// restore because transactional data matters more than a store logo.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static RestoreResult Failed(string message, string? safetyBackupPath = null) =>
        new() { Succeeded = false, ErrorMessage = message, SafetyBackupPath = safetyBackupPath };
}
