using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Maintenance;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Maintenance;

public class DatabaseMaintenanceServiceTests : IDisposable
{
    private readonly SqliteFileDbContextFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private SqliteDatabaseMaintenanceService CreateService() => new(
        _fixture.Context,
        _fixture.Context,
        _fixture.Paths,
        new PermissionEnforcer(_fixture.Context),
        new EfAuditLogger(_fixture.Context));

    private async Task<(int ProductCount, int CategoryCount)> SeedCatalogueAsync()
    {
        var category = new Category { Name = "Staples" };
        _fixture.Context.Categories.Add(category);
        await _fixture.Context.SaveChangesAsync();

        for (var i = 1; i <= 3; i++)
        {
            _fixture.Context.Products.Add(new Product
            {
                ProductCode = $"PRD-00000{i}",
                Name = $"Product {i}",
                SellingPrice = 10 * i,
                CategoryId = category.Id,
            });
        }

        await _fixture.Context.SaveChangesAsync();
        return (3, 1);
    }

    [Fact]
    public async Task RunIntegrityCheckAsync_ReportsAHealthyDatabase()
    {
        var owner = await _fixture.SeedOwnerAsync();

        var result = await CreateService().RunIntegrityCheckAsync(owner.Id);

        Assert.True(result.IsHealthy);
        Assert.Equal(["ok"], result.Messages);
    }

    [Fact]
    public async Task VacuumAsync_ReclaimsSpaceWithoutChangingAnyRowData()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var (productCount, categoryCount) = await SeedCatalogueAsync();

        var namesBefore = await _fixture.Context.Products.AsNoTracking()
            .OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();

        var result = await CreateService().VacuumAsync(owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        _fixture.Context.ChangeTracker.Clear();

        Assert.Equal(productCount, await _fixture.Context.Products.CountAsync());
        Assert.Equal(categoryCount, await _fixture.Context.Categories.CountAsync());
        Assert.Equal(namesBefore, await _fixture.Context.Products.AsNoTracking()
            .OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());
    }

    [Fact]
    public async Task AnalyzeAsync_LeavesRowDataUntouched()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var (productCount, _) = await SeedCatalogueAsync();

        var result = await CreateService().AnalyzeAsync(owner.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(productCount, await _fixture.Context.Products.CountAsync());
    }

    [Fact]
    public async Task VacuumAndAnalyze_AreAuditLogged()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var service = CreateService();

        await service.VacuumAsync(owner.Id);
        await service.AnalyzeAsync(owner.Id);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "DatabaseVacuumed" && a.UserId == owner.Id));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "DatabaseAnalyzed" && a.UserId == owner.Id));
    }

    [Fact]
    public async Task GetStatisticsAsync_ReportsFileAndTableFigures()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await SeedCatalogueAsync();

        var stats = await CreateService().GetStatisticsAsync(owner.Id);

        Assert.Equal(_fixture.Paths.DatabaseFilePath, stats.DatabaseFilePath);
        Assert.True(stats.FileSizeBytes > 0);
        Assert.True(stats.PageCount > 0);
        Assert.True(stats.PageSize > 0);
        Assert.Equal(3, stats.TableRowCounts.Single(t => t.Table == "Products").RowCount);
        Assert.Equal(1, stats.TableRowCounts.Single(t => t.Table == "Users").RowCount);
    }

    [Fact]
    public async Task GetStatisticsAsync_SurfacesTheLastMaintenanceTimestamps()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var service = CreateService();

        Assert.Null((await service.GetStatisticsAsync(owner.Id)).LastVacuumUtc);

        await service.VacuumAsync(owner.Id);

        Assert.NotNull((await service.GetStatisticsAsync(owner.Id)).LastVacuumUtc);
    }

    [Fact]
    public async Task FindOrphanRecordsAsync_FindsNothingInAConsistentDatabase()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await SeedCatalogueAsync();

        var orphans = await CreateService().FindOrphanRecordsAsync(owner.Id);

        Assert.NotEmpty(orphans);
        Assert.All(orphans, group => Assert.Equal(0, group.Count));
    }

    [Fact]
    public async Task FindOrphanRecordsAsync_DetectsADanglingRowAndChangesNothing()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await SeedCatalogueAsync();

        // Inserted with foreign keys off so a genuinely dangling row exists to find — this is the
        // corruption shape the diagnostic is meant to surface.
        await _fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await _fixture.Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Inventories (ProductId, QuantityOnHand, CreatedAtUtc) VALUES (99999, 5, '2026-01-01T00:00:00');");

        var inventoriesBefore = await _fixture.Context.Inventories.CountAsync();
        var productsBefore = await _fixture.Context.Products.CountAsync();

        var orphans = await CreateService().FindOrphanRecordsAsync(owner.Id);

        var group = orphans.Single(g => g.Description.Contains("Stock levels", StringComparison.Ordinal));
        Assert.Equal(1, group.Count);
        Assert.Single(group.SampleIds);

        // Read-only proof: the diagnostic reports, it never cleans up.
        Assert.Equal(inventoriesBefore, await _fixture.Context.Inventories.CountAsync());
        Assert.Equal(productsBefore, await _fixture.Context.Products.CountAsync());
    }

    [Fact]
    public async Task EveryOperation_RequiresBackupRestorePermission()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RunIntegrityCheckAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VacuumAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AnalyzeAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetStatisticsAsync(cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.FindOrphanRecordsAsync(cashier.Id));
    }
}
