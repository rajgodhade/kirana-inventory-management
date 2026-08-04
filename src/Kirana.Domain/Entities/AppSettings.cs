using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Single-row application configuration: session locking, backup schedule (PRD §8, §38).
/// </summary>
public class AppSettings : Entity
{
    public int AutoLockMinutes { get; set; } = 10;
    public bool AutomaticBackupEnabled { get; set; } = true;
    public string AutomaticBackupFrequency { get; set; } = "Daily";
    public string? BackupDirectory { get; set; }
    public int BackupRetentionCount { get; set; } = 14;
}
