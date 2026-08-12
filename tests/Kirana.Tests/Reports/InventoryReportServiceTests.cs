using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>Inventory reports (PRD §51), most importantly inventory valuation — checked against a
/// hand-computed quantity×purchase-price total so the "estimate" can't silently drift.</summary>
public class InventoryReportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly InventoryReportService _sut;
    private readonly int _ownerId;

    public InventoryReportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);
        var inventoryService = new InventoryService(_fixture.Context, audit, enforcer);

        _sut = new InventoryReportService(_fixture.Context, inventoryService, enforcer);
    }

    private async Task<Product> SeedProductAsync(
        string name, decimal purchasePrice, decimal stock, decimal minimumStock = 0, decimal reorderQuantity = 0, bool tracksBatches = false)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = purchasePrice,
            Mrp = purchasePrice + 20,
            SellingPrice = purchasePrice + 15,
            MinimumStock = minimumStock,
            ReorderQuantity = reorderQuantity,
            TracksBatches = tracksBatches,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task Valuation_EqualsSumOfQuantityTimesPurchasePrice()
    {
        await SeedProductAsync("A", purchasePrice: 10, stock: 5);   // 50
        await SeedProductAsync("B", purchasePrice: 25, stock: 4);   // 100

        var valuation = await _sut.GetValuationAsync(_ownerId);

        Assert.Equal(150m, valuation.TotalStockValue);
        Assert.Equal(9m, valuation.TotalUnitsOnHand);
    }

    [Fact]
    public async Task Valuation_ExcludesInactiveProducts()
    {
        var product = await SeedProductAsync("Retired", purchasePrice: 10, stock: 100);
        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.IsActive = false;
        await _fixture.Context.SaveChangesAsync();

        var valuation = await _sut.GetValuationAsync(_ownerId);

        Assert.Equal(0m, valuation.TotalStockValue);
    }

    [Fact]
    public async Task Valuation_RequiresPricingPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetValuationAsync(cashier.Id));

        // Manager holds ReportsView but not PricingViewPurchasePrice — valuation reveals cost,
        // which is management-only data regardless of general report access.
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-inv", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetValuationAsync(manager.Id));
    }

    [Fact]
    public async Task CurrentInventory_HidesStockValue_WithoutPricingPermission()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-inv2", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        await SeedProductAsync("X", purchasePrice: 10, stock: 5);

        var rows = await _sut.GetCurrentInventoryAsync(filter: null, manager.Id);

        Assert.All(rows, r => Assert.Null(r.StockValue));
        Assert.Equal(5m, rows.Single().QuantityOnHand); // quantity itself is still visible
    }

    [Fact]
    public async Task LowStock_MatchesInventoryServiceRule()
    {
        await SeedProductAsync("Low", purchasePrice: 10, stock: 2, minimumStock: 5);
        await SeedProductAsync("Healthy", purchasePrice: 10, stock: 50, minimumStock: 5);

        var rows = await _sut.GetLowStockAsync(_ownerId);

        var row = Assert.Single(rows);
        Assert.Equal("Low", row.ProductName);
    }

    [Fact]
    public async Task OutOfStock_IncludesZeroQuantityProducts()
    {
        await SeedProductAsync("Empty", purchasePrice: 10, stock: 0);
        await SeedProductAsync("Stocked", purchasePrice: 10, stock: 1);

        var rows = await _sut.GetOutOfStockAsync(_ownerId);

        Assert.Single(rows);
        Assert.Equal("Empty", rows[0].ProductName);
    }

    [Fact]
    public async Task Overstock_FlagsQuantityWellAboveReorderLevel()
    {
        await SeedProductAsync("Overstocked", purchasePrice: 10, stock: 500, reorderQuantity: 10); // 50x reorder qty
        await SeedProductAsync("Normal", purchasePrice: 10, stock: 15, reorderQuantity: 10);

        var rows = await _sut.GetOverstockAsync(_ownerId);

        Assert.Single(rows);
        Assert.Equal("Overstocked", rows[0].ProductName);
    }

    [Fact]
    public async Task ExpiredBatches_OnlyReturnsBatchesPastTheirExpiryWithStock()
    {
        var product = await SeedProductAsync("Batched", purchasePrice: 10, stock: 0, tracksBatches: true);
        _fixture.Context.ProductBatches.AddRange(
            new ProductBatch { Product = product, BatchNumber = "OLD", Quantity = 5, ExpiryDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-10)) },
            new ProductBatch { Product = product, BatchNumber = "FRESH", Quantity = 5, ExpiryDate = DateOnly.FromDateTime(DateTime.Now.AddDays(30)) },
            new ProductBatch { Product = product, BatchNumber = "EXPIRED_BUT_EMPTY", Quantity = 0, ExpiryDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-5)) });
        await _fixture.Context.SaveChangesAsync();

        var rows = await _sut.GetExpiredBatchesAsync(_ownerId);

        var row = Assert.Single(rows);
        Assert.Equal("OLD", row.BatchNumber);
        Assert.True(row.IsExpired);
    }

    [Fact]
    public async Task ExpiringSoon_MatchesInventoryServiceWindow()
    {
        var product = await SeedProductAsync("Batched", purchasePrice: 10, stock: 0, tracksBatches: true);
        _fixture.Context.ProductBatches.Add(
            new ProductBatch { Product = product, BatchNumber = "SOON", Quantity = 5, ExpiryDate = DateOnly.FromDateTime(DateTime.Now.AddDays(3)) });
        await _fixture.Context.SaveChangesAsync();

        var rows = await _sut.GetExpiringSoonAsync(withinDays: 7, _ownerId);

        Assert.Single(rows);
        Assert.False(rows[0].IsExpired);
    }

    [Fact]
    public async Task DamagedStock_OnlyIncludesDamagedMovementType()
    {
        var product = await SeedProductAsync("Damaged Test", purchasePrice: 10, stock: 10);
        _fixture.Context.StockMovements.Add(new StockMovement
        {
            Product = product, MovementType = StockMovementType.Damaged, QuantityChange = -2, PreviousQuantity = 10, NewQuantity = 8,
        });
        _fixture.Context.StockMovements.Add(new StockMovement
        {
            Product = product, MovementType = StockMovementType.PositiveAdjustment, QuantityChange = 5, PreviousQuantity = 8, NewQuantity = 13,
        });
        await _fixture.Context.SaveChangesAsync();

        var rows = await _sut.GetDamagedStockAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId);

        var row = Assert.Single(rows);
        Assert.Equal("Damaged", row.MovementType);
    }

    // ---- Phase 13C: stock count history ----

    /// <summary>Runs a real count through the real service, so the report is checked against
    /// movements the finalizer actually wrote rather than hand-built rows that could disagree.</summary>
    private async Task<Kirana.Application.StockCounts.StockCountService> StockCountServiceAsync()
    {
        await Task.CompletedTask;
        return new Kirana.Application.StockCounts.StockCountService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context),
            new Kirana.Application.Barcodes.BarcodeLookupService(_fixture.Context));
    }

    [Fact]
    public async Task StockCountHistory_ReportsWhatTheCountActuallyAdjusted()
    {
        var shortage = await SeedProductAsync("Shortage", purchasePrice: 10, stock: 100);
        var surplus = await SeedProductAsync("Surplus", purchasePrice: 10, stock: 50);
        var exact = await SeedProductAsync("Exact", purchasePrice: 10, stock: 30);

        var counts = await StockCountServiceAsync();
        var count = await counts.StartAsync(null, _ownerId);
        foreach (var (product, physical) in new[] { (shortage, 97m), (surplus, 52m), (exact, 30m) })
        {
            var item = await counts.AddItemAsync(count.Id, product.Id, null, _ownerId);
            await counts.SetCountedQuantityAsync(item.Id, physical, null, _ownerId);
        }
        await counts.FinalizeAsync(count.Id, _ownerId);

        var row = Assert.Single(
            await _sut.GetStockCountHistoryAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId));

        Assert.Equal(count.CountNumber, row.CountNumber);
        Assert.Equal("Completed", row.Status);
        Assert.Equal(3, row.ProductsCounted);
        Assert.Equal(1, row.IncreasedCount);
        Assert.Equal(1, row.DecreasedCount);
        Assert.Equal(1, row.UnchangedCount);
        Assert.Equal(2, row.AdjustmentCount);
        Assert.Equal(2m, row.TotalIncreaseQuantity);
        Assert.Equal(3m, row.TotalDecreaseQuantity);
        Assert.Equal(-1m, row.NetQuantityChange);
    }

    [Fact]
    public async Task StockCountHistory_ShowsACancelledCountAsAdjustingNothing()
    {
        var product = await SeedProductAsync("Cancelled Count Product", purchasePrice: 10, stock: 100);
        var counts = await StockCountServiceAsync();
        var count = await counts.StartAsync(null, _ownerId);
        var item = await counts.AddItemAsync(count.Id, product.Id, null, _ownerId);
        await counts.SetCountedQuantityAsync(item.Id, 80m, null, _ownerId);
        await counts.CancelAsync(count.Id, "Recount needed", _ownerId);

        var row = Assert.Single(
            await _sut.GetStockCountHistoryAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId));

        Assert.Equal("Cancelled", row.Status);
        Assert.Equal(0, row.AdjustmentCount);
        Assert.Equal(0m, row.NetQuantityChange);
    }

    [Fact]
    public async Task StockCountHistory_RequiresReportsView()
    {
        await _fixture.SeedCashierAsync();
        var cashier = await _fixture.Context.Users.FirstAsync(u => u.Role.Name == "Cashier");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetStockCountHistoryAsync(ReportDateRange.Resolve(ReportDatePreset.Today), cashier.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
