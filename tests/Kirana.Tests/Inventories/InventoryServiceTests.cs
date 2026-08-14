using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Inventories;

public class InventoryServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly InventoryService _sut;
    private readonly int _ownerId;

    public InventoryServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _sut = new InventoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));
    }

    private async Task<Product> SeedProductAsync(decimal minimumStock = 0, decimal openingStock = 0, string name = "Test Product")
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            MinimumStock = minimumStock,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = openingStock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task AdjustStockAsync_IncreasesQuantity_ForPositiveAdjustment()
    {
        var product = await SeedProductAsync(openingStock: 10);

        await _sut.AdjustStockAsync(product.Id, 5, StockMovementType.PositiveAdjustment, "Found extra stock", _ownerId);

        Assert.Equal(15, await _sut.GetStockAsync(product.Id));
    }

    [Fact]
    public async Task AdjustStockAsync_DecreasesQuantity_ForNegativeAdjustment()
    {
        var product = await SeedProductAsync(openingStock: 10);

        await _sut.AdjustStockAsync(product.Id, -3, StockMovementType.NegativeAdjustment, "Damaged", _ownerId);

        Assert.Equal(7, await _sut.GetStockAsync(product.Id));
    }

    [Fact]
    public async Task AdjustStockAsync_WritesMovementWithPreviousAndNewQuantity()
    {
        var product = await SeedProductAsync(openingStock: 20);

        var movement = await _sut.AdjustStockAsync(product.Id, -5, StockMovementType.Damaged, "Broken bottle", _ownerId);

        Assert.Equal(20, movement.PreviousQuantity);
        Assert.Equal(15, movement.NewQuantity);
        Assert.Equal(-5, movement.QuantityChange);
    }

    [Fact]
    public async Task AdjustStockAsync_Throws_WhenSignDoesNotMatchMovementType()
    {
        var product = await SeedProductAsync(openingStock: 10);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.AdjustStockAsync(product.Id, 5, StockMovementType.NegativeAdjustment, null, _ownerId));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.AdjustStockAsync(product.Id, -5, StockMovementType.PositiveAdjustment, null, _ownerId));
    }

    [Fact]
    public async Task AdjustStockAsync_Throws_WhenQuantityChangeIsZero()
    {
        var product = await SeedProductAsync(openingStock: 10);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.AdjustStockAsync(product.Id, 0, StockMovementType.PositiveAdjustment, null, _ownerId));
    }

    [Fact]
    public async Task AdjustStockAsync_Throws_WhenProductDoesNotExist()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AdjustStockAsync(999, 5, StockMovementType.PositiveAdjustment, null, _ownerId));
    }

    [Fact]
    public async Task AdjustStockAsync_Throws_WhenPerformerLacksPermission()
    {
        var product = await SeedProductAsync(openingStock: 10);
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AdjustStockAsync(product.Id, 5, StockMovementType.PositiveAdjustment, "test", cashier.Id));
    }

    [Fact]
    public async Task GetMovementHistoryAsync_ReturnsMostRecentFirst()
    {
        var product = await SeedProductAsync(openingStock: 100);

        await _sut.AdjustStockAsync(product.Id, 5, StockMovementType.PositiveAdjustment, "First", _ownerId);
        await _sut.AdjustStockAsync(product.Id, -2, StockMovementType.NegativeAdjustment, "Second", _ownerId);

        var history = await _sut.GetMovementHistoryAsync(product.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal("Second", history[0].Reason);
    }

    [Fact]
    public async Task GetLowStockProductsAsync_ReturnsProductsAtOrBelowMinimumButAboveZero()
    {
        var low = await SeedProductAsync(minimumStock: 10, openingStock: 5, name: "Low Stock Item");
        await SeedProductAsync(minimumStock: 10, openingStock: 50, name: "Well Stocked Item");
        await SeedProductAsync(minimumStock: 10, openingStock: 0, name: "Out Of Stock Item");

        var result = await _sut.GetLowStockProductsAsync();

        Assert.Single(result);
        Assert.Equal(low.Id, result[0].Id);
    }

    [Fact]
    public async Task GetOutOfStockProductsAsync_ReturnsZeroOrNegativeStockProducts()
    {
        await SeedProductAsync(openingStock: 5, name: "In Stock");
        var outOfStock = await SeedProductAsync(openingStock: 0, name: "Out Of Stock");

        var result = await _sut.GetOutOfStockProductsAsync();

        Assert.Single(result);
        Assert.Equal(outOfStock.Id, result[0].Id);
    }

    [Fact]
    public async Task AddBatchAsync_CreatesBatch()
    {
        var product = await SeedProductAsync();

        var batch = await _sut.AddBatchAsync(
            product.Id, "BATCH-001", DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddDays(30)), 50, 8, 12, _ownerId);

        Assert.Equal("BATCH-001", batch.BatchNumber);
        Assert.Equal(50, batch.Quantity);
    }

    [Fact]
    public async Task UpdateBatchExpiryAsync_UpdatesOnlyExpiryAndWritesAuditEntry()
    {
        var product = await SeedProductAsync();
        var originalExpiry = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
        var revisedExpiry = DateOnly.FromDateTime(DateTime.Today.AddDays(60));
        var batch = await _sut.AddBatchAsync(
            product.Id, "BATCH-EXP", null, originalExpiry, 12, 8, 12, _ownerId);

        await _sut.UpdateBatchExpiryAsync(batch.Id, revisedExpiry, _ownerId);

        var persisted = await _fixture.Context.ProductBatches.SingleAsync(b => b.Id == batch.Id);
        Assert.Equal(revisedExpiry, persisted.ExpiryDate);
        Assert.Equal(12, persisted.Quantity);
        Assert.Contains(await _fixture.Context.AuditLogs.ToListAsync(),
            entry => entry.Action == "BatchExpiryUpdated" && entry.EntityId == batch.Id.ToString());
    }

    [Fact]
    public async Task GetExpiringBatchesAsync_ReturnsBatchesWithinThreshold()
    {
        var product = await SeedProductAsync();
        await _sut.AddBatchAsync(product.Id, "SOON", null, DateOnly.FromDateTime(DateTime.Today.AddDays(5)), 10, null, null, _ownerId);
        await _sut.AddBatchAsync(product.Id, "LATER", null, DateOnly.FromDateTime(DateTime.Today.AddDays(90)), 10, null, null, _ownerId);

        var expiring = await _sut.GetExpiringBatchesAsync(withinDays: 7);

        Assert.Single(expiring);
        Assert.Equal("SOON", expiring[0].BatchNumber);
    }

    public void Dispose() => _fixture.Dispose();
}
