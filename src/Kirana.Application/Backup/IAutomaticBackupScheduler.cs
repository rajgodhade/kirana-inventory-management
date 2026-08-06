namespace Kirana.Application.Backup;

public sealed class ScheduledBackupOutcome
{
    public bool WasDue { get; init; }
    public BackupResult? Result { get; init; }
    public string? SkipReason { get; init; }

    public static ScheduledBackupOutcome Skipped(string reason) => new() { WasDue = false, SkipReason = reason };
}

/// <summary>
/// Decides whether an automatic backup is due and runs it (PRD §38 "Daily/Weekly"). Kirana has no
/// background service or scheduler dependency — this is polled at launch and periodically while the
/// app is open, so a store that never restarts still gets its scheduled backup.
/// </summary>
public interface IAutomaticBackupScheduler
{
    Task<ScheduledBackupOutcome> RunIfDueAsync(CancellationToken cancellationToken = default);
}
