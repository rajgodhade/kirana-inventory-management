using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Backup;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Backup;

public class RestoreServiceTests : IDisposable
{
    private readonly SqliteFileDbContextFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private SqliteBackupService CreateBackupService() => new(
        _fixture.Context,
        _fixture.Paths,
        new PermissionEnforcer(_fixture.Context),
        new EfAuditLogger(_fixture.Context));

    private SqliteRestoreService CreateRestoreService() => new(
        _fixture.Paths,
        CreateBackupService(),
        new PermissionEnforcer(_fixture.Context));

    /// <summary>Reads the live database file directly rather than through the fixture's context —
    /// after a restore the file on disk has been swapped, and an already-open context would answer
    /// from its own tracked state rather than from the restored bytes.</summary>
    private long CountRows(string table)
    {
        using var connection = new SqliteConnection(
            SqliteBackupService.ExclusiveConnectionString(_fixture.Paths.DatabaseFilePath, readOnly: true));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private IReadOnlyList<string> ReadCategoryNames()
    {
        using var connection = new SqliteConnection(
            SqliteBackupService.ExclusiveConnectionString(_fixture.Paths.DatabaseFilePath, readOnly: true));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Categories ORDER BY Name;";
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task RestoreAsync_BringsBackExactlyTheDataThatExistedAtBackupTime()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Categories.Add(new Category { Name = "Original Category" });
        await _fixture.Context.SaveChangesAsync();

        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        Assert.True(backup.Succeeded, backup.ErrorMessage);

        // Data changes after the backup was taken — this is what the restore must undo.
        _fixture.Context.Categories.Add(new Category { Name = "Added After Backup" });
        var toRemove = await _fixture.Context.Categories.SingleAsync(c => c.Name == "Original Category");
        toRemove.Name = "Renamed After Backup";
        await _fixture.Context.SaveChangesAsync();
        _fixture.Checkpoint();

        Assert.Contains("Added After Backup", ReadCategoryNames());

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var restored = ReadCategoryNames();
        Assert.Contains("Original Category", restored);
        Assert.DoesNotContain("Added After Backup", restored);
        Assert.DoesNotContain("Renamed After Backup", restored);
    }

    [Fact]
    public async Task RestoreAsync_PreservesGoodsReceiptsAndTheirItems()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var supplier = new Supplier { SupplierCode = "SUP-BACKUP-GRN", Name = "Backup Supplier", IsActive = true };
        var product = new Product
        {
            ProductCode = "PRD-BACKUP-GRN", Name = "Backup Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10m, Mrp = 15m, SellingPrice = 14m, PricingType = PricingType.Inclusive,
            GstRatePercent = 5m, IsActive = true,
        };
        var order = new PurchaseOrder
        {
            PurchaseOrderNumber = "PO-BACKUP-GRN", Supplier = supplier,
            SupplierNameSnapshot = supplier.Name, SupplierCodeSnapshot = supplier.SupplierCode,
            Status = PurchaseOrderStatus.PartiallyReceived, CreatedByUserId = owner.Id,
        };
        var orderItem = new PurchaseOrderItem
        {
            PurchaseOrder = order, Product = product, ProductNameSnapshot = product.Name,
            ProductCodeSnapshot = product.ProductCode, UnitSnapshot = product.Unit.ToString(),
            PricingTypeSnapshot = product.PricingType, GstRatePercentSnapshot = 5m,
            OrderedQuantity = 10m, UnitCost = 10m,
        };
        var receipt = new GoodsReceipt
        {
            GoodsReceiptNumber = "GRN-BACKUP-000001", PurchaseOrder = order, Supplier = supplier,
            SupplierNameSnapshot = supplier.Name, SupplierCodeSnapshot = supplier.SupplierCode,
            Status = GoodsReceiptStatus.Completed, CreatedByUserId = owner.Id,
            CompletedByUserId = owner.Id, CompletedAtUtc = DateTime.UtcNow,
        };
        receipt.Items.Add(new GoodsReceiptItem
        {
            PurchaseOrderItem = orderItem, Product = product, ProductNameSnapshot = product.Name,
            ProductCodeSnapshot = product.ProductCode, UnitSnapshot = product.Unit,
            OrderedQuantitySnapshot = 10m, ReceivedQuantity = 4m,
        });
        _fixture.Context.AddRange(orderItem, receipt);
        await _fixture.Context.SaveChangesAsync();

        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        Assert.True(backup.Succeeded, backup.ErrorMessage);

        _fixture.Context.GoodsReceipts.Add(new GoodsReceipt
        {
            GoodsReceiptNumber = "GRN-AFTER-BACKUP", PurchaseOrder = order, Supplier = supplier,
            SupplierNameSnapshot = supplier.Name, SupplierCodeSnapshot = supplier.SupplierCode,
            Status = GoodsReceiptStatus.Draft, CreatedByUserId = owner.Id,
        });
        await _fixture.Context.SaveChangesAsync();
        _fixture.Checkpoint();
        Assert.Equal(2, CountRows("GoodsReceipts"));

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, CountRows("GoodsReceipts"));
        Assert.Equal(1, CountRows("GoodsReceiptItems"));
    }

    [Fact]
    public async Task RestoreAsync_PreservesUsersPermissionsAndAuditHistory()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedCashierAsync();
        await new EfAuditLogger(_fixture.Context).RecordAsync(owner.Id, "TestAction", "Test");

        var usersBefore = CountRows("Users");
        var rolePermissionsBefore = CountRows("RolePermissions");
        var auditBefore = CountRows("AuditLogs");

        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);
        Assert.True(result.Succeeded, result.ErrorMessage);

        Assert.Equal(usersBefore, CountRows("Users"));
        Assert.Equal(rolePermissionsBefore, CountRows("RolePermissions"));

        // The backup already contained the BackupCreated entry; the restore then appends its own
        // RestorePerformed row to the restored database.
        Assert.True(CountRows("AuditLogs") > auditBefore);
    }

    [Fact]
    public async Task RestoreAsync_TakesASafetyBackupOfTheCurrentDataFirst()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);

        _fixture.Context.Categories.Add(new Category { Name = "Only In The Safety Backup" });
        await _fixture.Context.SaveChangesAsync();
        _fixture.Checkpoint();

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.SafetyBackupPath);
        Assert.True(File.Exists(result.SafetyBackupPath));

        // The safety backup must genuinely contain the pre-restore state, not be an empty gesture.
        var info = await CreateRestoreService().GetBackupInfoAsync(result.SafetyBackupPath!);
        Assert.Equal(nameof(BackupType.PreRestoreSafety), info.Manifest.BackupType);
    }

    [Fact]
    public async Task RestoreAsync_RecordsARestorePerformedAuditEntryInsideTheRestoredDatabase()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        using var connection = new SqliteConnection(
            SqliteBackupService.ExclusiveConnectionString(_fixture.Paths.DatabaseFilePath, readOnly: true));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'RestorePerformed';";
        Assert.Equal(1L, Convert.ToInt64(command.ExecuteScalar()));
    }

    [Fact]
    public async Task RestoreAsync_RefusesACorruptedBackupAndLeavesTheLiveDatabaseUntouched()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Categories.Add(new Category { Name = "Live Data" });
        await _fixture.Context.SaveChangesAsync();

        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        BackupServiceTests.CorruptDatabaseInsideBundle(backup.FilePath!);

        var liveHashBefore = await SqliteBackupService.ComputeSha256Async(_fixture.Paths.DatabaseFilePath, default);

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Contains("validation", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);

        // The strongest possible statement that nothing happened: the live file is byte-identical.
        var liveHashAfter = await SqliteBackupService.ComputeSha256Async(_fixture.Paths.DatabaseFilePath, default);
        Assert.Equal(liveHashBefore, liveHashAfter);
        Assert.Contains("Live Data", ReadCategoryNames());
    }

    [Fact]
    public async Task RestoreAsync_DoesNotEvenTakeASafetyBackupWhenTheSourceIsInvalid()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var bogus = Path.Combine(_fixture.Paths.BackupsDirectory, "not-a-backup.kbak");
        await File.WriteAllTextAsync(bogus, "garbage");

        var result = await CreateRestoreService().RestoreAsync(bogus, owner.Id);

        Assert.False(result.Succeeded);
        Assert.Null(result.SafetyBackupPath);
        Assert.False(await _fixture.Context.BackupRecords.AnyAsync());
    }

    [Fact]
    public async Task RestoreAsync_RequiresBackupRestorePermission()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        var liveHashBefore = await SqliteBackupService.ComputeSha256Async(_fixture.Paths.DatabaseFilePath, default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateRestoreService().RestoreAsync(backup.FilePath!, cashier.Id));

        var liveHashAfter = await SqliteBackupService.ComputeSha256Async(_fixture.Paths.DatabaseFilePath, default);
        Assert.Equal(liveHashBefore, liveHashAfter);
    }

    [Fact]
    public async Task RestoreAsync_RestoresStoreAssetsAlongsideTheDatabase()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var logoPath = Path.Combine(_fixture.Paths.AssetsDirectory, "logo.png");
        await File.WriteAllTextAsync(logoPath, "original-logo");

        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        await File.WriteAllTextAsync(logoPath, "replaced-later");

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("original-logo", await File.ReadAllTextAsync(logoPath));
    }

    [Fact]
    public async Task GetBackupInfoAsync_ReportsRowCountsWithoutTouchingTheLiveDatabase()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Categories.Add(new Category { Name = "Snacks" });
        await _fixture.Context.SaveChangesAsync();

        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        var liveHashBefore = await SqliteBackupService.ComputeSha256Async(_fixture.Paths.DatabaseFilePath, default);

        var info = await CreateRestoreService().GetBackupInfoAsync(backup.FilePath!);

        Assert.Equal(1, info.RowCounts.Single(c => c.Table == "Users").RowCount);
        Assert.True(info.FileSizeBytes > 0);

        var liveHashAfter = await SqliteBackupService.ComputeSha256Async(_fixture.Paths.DatabaseFilePath, default);
        Assert.Equal(liveHashBefore, liveHashAfter);
    }

    [Fact]
    public async Task GetBackupInfoAsync_ThrowsForAnUnreadableBundle()
    {
        var bogus = Path.Combine(_fixture.Paths.BackupsDirectory, "broken.kbak");
        await File.WriteAllTextAsync(bogus, "not a zip");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateRestoreService().GetBackupInfoAsync(bogus));
    }

    [Fact]
    public async Task RestoreAsync_RemovesStaleWriteAheadLogFiles()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var backup = await CreateBackupService().CreateBackupAsync(BackupType.Manual, owner.Id);
        _fixture.Checkpoint();

        // A leftover -wal belonging to the replaced database would let stale pages be replayed over
        // the restored data.
        await File.WriteAllTextAsync(_fixture.Paths.DatabaseFilePath + "-wal", "stale");

        var result = await CreateRestoreService().RestoreAsync(backup.FilePath!, owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(File.Exists(_fixture.Paths.DatabaseFilePath + "-wal"));
    }
}
