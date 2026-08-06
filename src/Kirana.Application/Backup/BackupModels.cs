using Kirana.Domain.Entities;

namespace Kirana.Application.Backup;

/// <summary>
/// The <c>manifest.json</c> written inside every backup bundle. Read back during validation to
/// prove the bundle's database file is byte-for-byte what was written, and shown to the operator
/// on the restore screen before they commit to overwriting live data.
/// </summary>
public sealed class BackupManifest
{
    /// <summary>Bumped only if the bundle's internal layout changes incompatibly; validation
    /// refuses anything it doesn't recognise rather than guessing.</summary>
    public int SchemaVersion { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; }
    public string BackupType { get; set; } = string.Empty;
    public string DatabaseSha256 { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? StoreName { get; set; }
    public int AssetFileCount { get; set; }
}

public sealed class BackupResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }
    public long FileSizeBytes { get; init; }
    public int? BackupRecordId { get; init; }
    public int DeletedByRetentionCount { get; init; }
}

public sealed class BackupValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public BackupManifest? Manifest { get; init; }

    public static BackupValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}

/// <summary>A <see cref="BackupRecord"/> plus whether its file is still actually on disk — a
/// user can delete or move a backup outside the app, and history must show that honestly rather
/// than offering a restore that would fail.</summary>
public sealed class BackupHistoryEntry
{
    public required BackupRecord Record { get; init; }
    public required bool FileExists { get; init; }
}
