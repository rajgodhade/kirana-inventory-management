using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Backup;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Backup;

public class AutomaticBackupSchedulerTests : IDisposable
{
    private readonly SqliteFileDbContextFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private AutomaticBackupScheduler CreateScheduler() => new(
        _fixture.Context,
        new SqliteBackupService(
            _fixture.Context,
            _fixture.Paths,
            new PermissionEnforcer(_fixture.Context),
            new EfAuditLogger(_fixture.Context)));

    private async Task ConfigureAsync(bool enabled, string frequency)
    {
        var settings = await _fixture.Context.AppSettings.FirstAsync();
        settings.AutomaticBackupEnabled = enabled;
        settings.AutomaticBackupFrequency = frequency;
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task RecordExistingBackupAsync(DateTime createdAtUtc)
    {
        _fixture.Context.BackupRecords.Add(new BackupRecord
        {
            FileName = "existing.kbak",
            FilePath = Path.Combine(_fixture.Paths.BackupsDirectory, "existing.kbak"),
            FileSizeBytes = 1,
            BackupType = BackupType.Scheduled,
            ChecksumSha256 = new string('a', 64),
            IsVerified = true,
            CreatedAtUtc = createdAtUtc,
        });
        await _fixture.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task RunIfDueAsync_TakesTheFirstBackupWhenNoneHasEverBeenTaken()
    {
        await _fixture.SeedOwnerAsync();
        await ConfigureAsync(enabled: true, AutomaticBackupScheduler.DailyFrequency);

        var outcome = await CreateScheduler().RunIfDueAsync();

        Assert.True(outcome.WasDue);
        Assert.True(outcome.Result!.Succeeded, outcome.Result.ErrorMessage);
        Assert.Equal(BackupType.Scheduled, (await _fixture.Context.BackupRecords.SingleAsync()).BackupType);
    }

    [Fact]
    public async Task RunIfDueAsync_SkipsWhenAutomaticBackupIsTurnedOff()
    {
        await _fixture.SeedOwnerAsync();
        await ConfigureAsync(enabled: false, AutomaticBackupScheduler.DailyFrequency);

        var outcome = await CreateScheduler().RunIfDueAsync();

        Assert.False(outcome.WasDue);
        Assert.Contains("turned off", outcome.SkipReason!, StringComparison.OrdinalIgnoreCase);
        Assert.False(await _fixture.Context.BackupRecords.AnyAsync());
    }

    [Fact]
    public async Task RunIfDueAsync_SkipsWhenTheDailyIntervalHasNotElapsed()
    {
        await _fixture.SeedOwnerAsync();
        await ConfigureAsync(enabled: true, AutomaticBackupScheduler.DailyFrequency);
        await RecordExistingBackupAsync(DateTime.UtcNow.AddHours(-2));

        var outcome = await CreateScheduler().RunIfDueAsync();

        Assert.False(outcome.WasDue);
        Assert.Equal(1, await _fixture.Context.BackupRecords.CountAsync());
    }

    [Fact]
    public async Task RunIfDueAsync_RunsOnceTheDailyIntervalHasElapsed()
    {
        await _fixture.SeedOwnerAsync();
        await ConfigureAsync(enabled: true, AutomaticBackupScheduler.DailyFrequency);
        await RecordExistingBackupAsync(DateTime.UtcNow.AddDays(-1).AddMinutes(-1));

        var outcome = await CreateScheduler().RunIfDueAsync();

        Assert.True(outcome.WasDue);
        Assert.Equal(2, await _fixture.Context.BackupRecords.CountAsync());
    }

    [Fact]
    public async Task RunIfDueAsync_UsesASevenDayIntervalForWeekly()
    {
        await _fixture.SeedOwnerAsync();
        await ConfigureAsync(enabled: true, AutomaticBackupScheduler.WeeklyFrequency);

        // Three days old: due under Daily, not yet due under Weekly.
        await RecordExistingBackupAsync(DateTime.UtcNow.AddDays(-3));

        Assert.False((await CreateScheduler().RunIfDueAsync()).WasDue);

        await ConfigureAsync(enabled: true, AutomaticBackupScheduler.DailyFrequency);
        _fixture.Context.ChangeTracker.Clear();

        Assert.True((await CreateScheduler().RunIfDueAsync()).WasDue);
    }

    [Fact]
    public async Task RunIfDueAsync_CountsAManualBackupAsSatisfyingTheSchedule()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await ConfigureAsync(enabled: true, AutomaticBackupScheduler.DailyFrequency);

        var manual = await new SqliteBackupService(
            _fixture.Context, _fixture.Paths,
            new PermissionEnforcer(_fixture.Context),
            new EfAuditLogger(_fixture.Context))
            .CreateBackupAsync(BackupType.Manual, owner.Id);
        Assert.True(manual.Succeeded, manual.ErrorMessage);

        // A manual backup an instant ago already protects the same data; firing a scheduled one on
        // top of it would just consume a retention slot for nothing.
        var outcome = await CreateScheduler().RunIfDueAsync();

        Assert.False(outcome.WasDue);
        Assert.Equal(1, await _fixture.Context.BackupRecords.CountAsync());
    }
}
