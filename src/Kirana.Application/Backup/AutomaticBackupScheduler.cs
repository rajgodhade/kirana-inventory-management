using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Backup;

public sealed class AutomaticBackupScheduler(IKiranaDbContext db, IBackupService backupService) : IAutomaticBackupScheduler
{
    public const string DailyFrequency = "Daily";
    public const string WeeklyFrequency = "Weekly";

    public async Task<ScheduledBackupOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            return ScheduledBackupOutcome.Skipped("Application settings are not initialised yet.");
        }

        if (!settings.AutomaticBackupEnabled)
        {
            return ScheduledBackupOutcome.Skipped("Automatic backup is turned off.");
        }

        // Compared against the last backup of ANY type, not just scheduled ones: a manual backup
        // taken an hour ago already protects the same data, so firing a scheduled one on top of it
        // would just burn a retention slot.
        var lastBackupUtc = await db.BackupRecords
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => (DateTime?)b.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var interval = IntervalFor(settings.AutomaticBackupFrequency);
        if (lastBackupUtc is { } last && DateTime.UtcNow - last < interval)
        {
            return ScheduledBackupOutcome.Skipped($"Last backup was taken {last:u}; next is due after {interval.TotalDays:0} day(s).");
        }

        var result = await backupService.CreateBackupAsync(
            BackupType.Scheduled, performedByUserId: null, notes: null, cancellationToken);

        return new ScheduledBackupOutcome { WasDue = true, Result = result };
    }

    private static TimeSpan IntervalFor(string frequency) =>
        string.Equals(frequency, WeeklyFrequency, StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromDays(7)
            : TimeSpan.FromDays(1);
}
