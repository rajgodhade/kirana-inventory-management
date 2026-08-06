using System.IO.Compression;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Backup;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private readonly SqliteFileDbContextFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private SqliteBackupService CreateService() => new(
        _fixture.Context,
        _fixture.Paths,
        new PermissionEnforcer(_fixture.Context),
        new EfAuditLogger(_fixture.Context));

    [Fact]
    public async Task CreateBackupAsync_WritesAVerifiedBundleAndRecordsIt()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var service = CreateService();

        var result = await service.CreateBackupAsync(BackupType.Manual, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(File.Exists(result.FilePath));

        using (var archive = ZipFile.OpenRead(result.FilePath!))
        {
            Assert.NotNull(archive.GetEntry("database.db"));
            Assert.NotNull(archive.GetEntry("manifest.json"));
        }

        var record = await _fixture.Context.BackupRecords.SingleAsync();
        Assert.Equal(BackupType.Manual, record.BackupType);
        Assert.Equal(owner.Id, record.CreatedByUserId);
        Assert.True(record.IsVerified);
        Assert.Equal(64, record.ChecksumSha256.Length);
        Assert.Equal(new FileInfo(result.FilePath!).Length, record.FileSizeBytes);
    }

    [Fact]
    public async Task CreateBackupAsync_BundlesStoreAssets()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await File.WriteAllTextAsync(Path.Combine(_fixture.Paths.AssetsDirectory, "logo.png"), "fake-logo-bytes");

        var result = await CreateService().CreateBackupAsync(BackupType.Manual, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        using var archive = ZipFile.OpenRead(result.FilePath!);
        var asset = archive.GetEntry("assets/logo.png");
        Assert.NotNull(asset);
    }

    [Fact]
    public async Task CreateBackupAsync_CapturesTheDataThatExistedAtBackupTime()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Categories.Add(new Category { Name = "Snacks" });
        await _fixture.Context.SaveChangesAsync();

        var result = await CreateService().CreateBackupAsync(BackupType.Manual, owner.Id);
        Assert.True(result.Succeeded, result.ErrorMessage);

        var info = await new SqliteRestoreService(
            _fixture.Paths, CreateService(), new PermissionEnforcer(_fixture.Context))
            .GetBackupInfoAsync(result.FilePath!);

        Assert.Equal("Test Store", info.Manifest.StoreName);
        Assert.Equal(1, info.RowCounts.Single(c => c.Table == "Users").RowCount);
    }

    [Fact]
    public async Task CreateBackupAsync_RequiresBackupRestorePermission_ForManualBackups()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.CreateBackupAsync(BackupType.Manual, cashier.Id));

        Assert.False(await _fixture.Context.BackupRecords.AnyAsync());
    }

    [Fact]
    public async Task CreateBackupAsync_DoesNotRequirePermission_ForScheduledBackups()
    {
        await _fixture.SeedOwnerAsync();

        // Scheduled runs have no interactive user at all — requiring a permission would make
        // automatic backup impossible rather than secure.
        var result = await CreateService().CreateBackupAsync(BackupType.Scheduled, performedByUserId: null);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var record = await _fixture.Context.BackupRecords.SingleAsync();
        Assert.Equal(BackupType.Scheduled, record.BackupType);
        Assert.Null(record.CreatedByUserId);
    }

    [Fact]
    public async Task CreateBackupAsync_WritesAnAuditEntry()
    {
        var owner = await _fixture.SeedOwnerAsync();

        await CreateService().CreateBackupAsync(BackupType.Manual, owner.Id);

        var audit = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "BackupCreated");
        Assert.Equal(owner.Id, audit.UserId);
        Assert.Equal(nameof(BackupRecord), audit.Entity);
    }

    [Fact]
    public async Task CreateBackupAsync_EnforcesRetentionCount()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var settings = await _fixture.Context.AppSettings.FirstAsync();
        settings.BackupRetentionCount = 2;
        await _fixture.Context.SaveChangesAsync();

        var service = CreateService();
        var paths = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var result = await service.CreateBackupAsync(BackupType.Manual, owner.Id);
            Assert.True(result.Succeeded, result.ErrorMessage);
            paths.Add(result.FilePath!);

            // The filename carries a whole-second timestamp; without this the four backups would
            // collide on both name and CreatedAtUtc ordering.
            await Task.Delay(1100);
        }

        var remaining = await _fixture.Context.BackupRecords.OrderBy(b => b.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, remaining.Count);

        // The two oldest files are gone from disk, the two newest survive.
        Assert.False(File.Exists(paths[0]));
        Assert.False(File.Exists(paths[1]));
        Assert.True(File.Exists(paths[2]));
        Assert.True(File.Exists(paths[3]));
    }

    [Fact]
    public async Task CleanupNeverTouchesFilesItHasNoRecordFor()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var settings = await _fixture.Context.AppSettings.FirstAsync();
        settings.BackupRetentionCount = 1;
        await _fixture.Context.SaveChangesAsync();

        // Something the operator put in the backup folder themselves.
        var unrelated = Path.Combine(_fixture.Paths.BackupsDirectory, "important-notes.txt");
        await File.WriteAllTextAsync(unrelated, "do not delete me");

        var service = CreateService();
        await service.CreateBackupAsync(BackupType.Manual, owner.Id);
        await Task.Delay(1100);
        await service.CreateBackupAsync(BackupType.Manual, owner.Id);

        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task ValidateBackupAsync_AcceptsAFreshlyWrittenBundle()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var service = CreateService();
        var result = await service.CreateBackupAsync(BackupType.Manual, owner.Id);

        var validation = await service.ValidateBackupAsync(result.FilePath!);

        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        Assert.NotNull(validation.Manifest);
        Assert.Equal("Manual", validation.Manifest!.BackupType);
    }

    [Fact]
    public async Task ValidateBackupAsync_RejectsABundleWhoseDatabaseWasTamperedWith()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var service = CreateService();
        var result = await service.CreateBackupAsync(BackupType.Manual, owner.Id);

        CorruptDatabaseInsideBundle(result.FilePath!);

        var validation = await service.ValidateBackupAsync(result.FilePath!);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("checksum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateBackupAsync_RejectsAFileThatIsNotAnArchive()
    {
        var notABackup = Path.Combine(_fixture.Paths.BackupsDirectory, "random.kbak");
        await File.WriteAllTextAsync(notABackup, "this is just text, not a zip");

        var validation = await CreateService().ValidateBackupAsync(notABackup);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task ValidateBackupAsync_RejectsAnArchiveWithNoManifest()
    {
        var bogus = Path.Combine(_fixture.Paths.BackupsDirectory, "no-manifest.kbak");
        using (var archive = ZipFile.Open(bogus, ZipArchiveMode.Create))
        {
            archive.CreateEntry("database.db");
        }

        var validation = await CreateService().ValidateBackupAsync(bogus);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Contains("manifest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateBackupAsync_ReportsAMissingFile()
    {
        var validation = await CreateService().ValidateBackupAsync(
            Path.Combine(_fixture.Paths.BackupsDirectory, "does-not-exist.kbak"));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task GetHistoryAsync_FlagsRecordsWhoseFileHasBeenDeletedOutsideTheApp()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var service = CreateService();
        var result = await service.CreateBackupAsync(BackupType.Manual, owner.Id);

        var beforeDelete = await service.GetHistoryAsync();
        Assert.True(beforeDelete.Single().FileExists);

        File.Delete(result.FilePath!);

        var afterDelete = await service.GetHistoryAsync();
        Assert.False(afterDelete.Single().FileExists);
    }

    [Fact]
    public async Task DeleteBackupAsync_RemovesTheFileAndTheRecord_AndRequiresPermission()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        var service = CreateService();
        var result = await service.CreateBackupAsync(BackupType.Manual, owner.Id);
        var recordId = result.BackupRecordId!.Value;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.DeleteBackupAsync(recordId, cashier.Id));
        Assert.True(File.Exists(result.FilePath));

        await service.DeleteBackupAsync(recordId, owner.Id);

        Assert.False(File.Exists(result.FilePath));
        Assert.False(await _fixture.Context.BackupRecords.AnyAsync());
    }

    /// <summary>Rewrites the bundle with a corrupted database entry, leaving the manifest (and so
    /// the recorded checksum) untouched — exactly what bit-rot or hand-editing looks like.</summary>
    internal static void CorruptDatabaseInsideBundle(string bundlePath)
    {
        var rebuilt = bundlePath + ".tmp";

        using (var source = ZipFile.OpenRead(bundlePath))
        using (var destination = ZipFile.Open(rebuilt, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var copy = destination.CreateEntry(entry.FullName);
                using var writer = copy.Open();

                if (entry.FullName == "database.db")
                {
                    writer.Write("corrupted-not-a-database"u8);
                    continue;
                }

                using var reader = entry.Open();
                reader.CopyTo(writer);
            }
        }

        File.Move(rebuilt, bundlePath, overwrite: true);
    }
}
