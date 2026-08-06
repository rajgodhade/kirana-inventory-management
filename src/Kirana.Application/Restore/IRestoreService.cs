namespace Kirana.Application.Restore;

/// <summary>
/// Replaces the live database with the contents of a backup bundle (PRD §38). Ordered so that every
/// step that could fail runs <em>before</em> the live file is touched — a failed restore therefore
/// leaves the existing database intact by construction, not by unwinding.
/// </summary>
public interface IRestoreService
{
    /// <summary>Read-only inspection of a bundle for the confirmation screen. Never touches the
    /// live database.</summary>
    Task<BackupInfo> GetBackupInfoAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the bundle, takes a mandatory safety backup of the current database, then swaps the
    /// restored file in. The caller is expected to restart the application afterwards — every
    /// already-open <c>DbContext</c> in the process is pointing at the replaced file.
    /// </summary>
    Task<RestoreResult> RestoreAsync(string filePath, int? performedByUserId, CancellationToken cancellationToken = default);
}
