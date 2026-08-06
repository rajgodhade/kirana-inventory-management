using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One row per backup bundle actually written to disk (PRD §11/§38) — the source of truth for
/// Backup History, retention cleanup, and restore's "pick a backup" screen. <see cref="CreatedAtUtc"/>
/// is inherited from <see cref="Entity"/>.
/// </summary>
public class BackupRecord : Entity
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public BackupType BackupType { get; set; }

    /// <summary>Null for <see cref="BackupType.Scheduled"/> and <see cref="BackupType.PreRestoreSafety"/>
    /// runs, which have no interactive user in context.</summary>
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public string ChecksumSha256 { get; set; } = string.Empty;
    public string? AppVersion { get; set; }

    /// <summary>Set true only once the bundle has been round-tripped through
    /// <c>IBackupService.ValidateBackupAsync</c> immediately after being written.</summary>
    public bool IsVerified { get; set; }

    public string? Notes { get; set; }
}
