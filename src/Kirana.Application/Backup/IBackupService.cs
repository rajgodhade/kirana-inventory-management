using Kirana.Domain.Entities;

namespace Kirana.Application.Backup;

/// <summary>
/// Creates, validates and prunes backup bundles (PRD §38). A bundle is a zip containing
/// <c>database.db</c>, <c>manifest.json</c> and an <c>assets/</c> folder — everything needed for a
/// complete recovery, since store profile, settings, users and permissions all live inside the
/// SQLite database itself.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Snapshots the live database using SQLite's online backup API (never a raw file copy, so an
    /// in-flight POS sale is neither interrupted nor captured half-written), bundles it, verifies
    /// the result, records it, and applies the retention policy.
    /// </summary>
    /// <param name="performedByUserId">Required and permission-checked for
    /// <see cref="BackupType.Manual"/>; ignored for scheduled and pre-restore safety backups, which
    /// have no interactive user in context.</param>
    Task<BackupResult> CreateBackupAsync(
        BackupType backupType, int? performedByUserId, string? notes = null, CancellationToken cancellationToken = default);

    /// <summary>Read-only integrity check of an existing bundle: structure, manifest, SHA-256 of the
    /// contained database, and <c>PRAGMA integrity_check</c> against it. Never mutates the file.</summary>
    Task<BackupValidationResult> ValidateBackupAsync(string filePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes a backup's file and its history row. Requires
    /// <see cref="PermissionKeys.BackupRestore"/>.</summary>
    Task DeleteBackupAsync(int backupRecordId, int? performedByUserId, CancellationToken cancellationToken = default);
}
