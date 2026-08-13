using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Inventories;

/// <summary>
/// An adjustment must compute against stock as it is NOW, not as the screen remembers it.
///
/// <para>These tests mutate stock through a genuinely SEPARATE <see cref="DbContext"/>, because
/// that is the only way to reproduce EF's identity map handing back a cached quantity. Phase 13C
/// shipped exactly this bug: every same-context test passed while the protection was a no-op
/// against a real database.</para>
/// </summary>
public class InventoryAdjustmentConcurrencyTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly InventoryAdjustmentService _sut;
    private readonly int _ownerId;
    private readonly int _productId;

    public InventoryAdjustmentConcurrencyTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        _sut = new InventoryAdjustmentService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context));

        var product = new Product
        {
            ProductCode = "PRD-CONCADJ", Name = "Concurrent Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10m, Mrp = 15m, SellingPrice = 14m, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        _fixture.Context.SaveChanges();
        _productId = product.Id;
    }

    /// <summary>A context sharing only the database connection — the realistic stand-in for a POS
    /// sale happening while an adjustment screen is open.</summary>
    private KiranaDbContext SeparateContext() => new(
        new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite(_fixture.Context.Database.GetDbConnection()).Options);

    private async Task SetStockFromAnotherContextAsync(decimal quantity)
    {
        using var other = SeparateContext();
        var inventory = await other.Inventories.FirstAsync(i => i.ProductId == _productId);
        inventory.QuantityOnHand = quantity;
        await other.SaveChangesAsync();
    }

    private CreateInventoryAdjustmentRequest Request(
        InventoryAdjustmentDirection direction = InventoryAdjustmentDirection.Decrease,
        decimal quantity = 5m) => new()
        {
            ProductId = _productId,
            Direction = direction,
            Quantity = quantity,
            Reason = InventoryAdjustmentReason.Damaged,
            PerformedByUserId = _ownerId,
        };

    private Task<decimal> StockAsync() =>
        _fixture.Context.Inventories.Where(i => i.ProductId == _productId)
            .Select(i => i.QuantityOnHand).FirstAsync();

    /// <summary>
    /// The headline scenario from §14. The screen was opened at 100, a sale took stock to 98, and
    /// the operator submits "decrease 5". The result must be 93 — computed from live stock — not 95,
    /// which would silently restore the two units the sale removed.
    /// </summary>
    [Fact]
    public async Task Decrease_ComputesAgainstLiveStock_NotTheStaleScreenValue()
    {
        // The service's context reads 100 and caches it, exactly as an open screen would.
        Assert.Equal(100m, await _sut.GetCurrentStockAsync(_productId));
        await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == _productId);

        await SetStockFromAnotherContextAsync(98m);

        var adjustment = await _sut.CreateAsync(Request(InventoryAdjustmentDirection.Decrease, 5m));

        Assert.Equal(93m, adjustment.NewQuantity);
        Assert.NotEqual(95m, adjustment.NewQuantity);
        Assert.Equal(98m, adjustment.PreviousQuantity);
        Assert.Equal(93m, await StockAsync());
    }

    [Fact]
    public async Task Increase_ComputesAgainstLiveStock()
    {
        await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == _productId);
        await SetStockFromAnotherContextAsync(98m);

        var adjustment = await _sut.CreateAsync(Request(InventoryAdjustmentDirection.Increase, 5m));

        Assert.Equal(103m, adjustment.NewQuantity);
        Assert.Equal(98m, adjustment.PreviousQuantity);
    }

    /// <summary>The recorded PreviousQuantity must be the live figure, so the ledger reconciles.
    /// A movement claiming it started from 100 when stock was 98 would break any audit that adds
    /// movements up.</summary>
    [Fact]
    public async Task MovementRecordsTheLivePreviousQuantity()
    {
        await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == _productId);
        await SetStockFromAnotherContextAsync(98m);

        await _sut.CreateAsync(Request(InventoryAdjustmentDirection.Decrease, 5m));

        var movement = Assert.Single(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Equal(98m, movement.PreviousQuantity);
        Assert.Equal(93m, movement.NewQuantity);
        Assert.Equal(-5m, movement.QuantityChange);
    }

    /// <summary>Negative-stock protection must also use live stock: a decrease that looked safe
    /// against the stale value can be unsafe against the real one.</summary>
    [Fact]
    public async Task NegativeStockGuard_UsesLiveStock()
    {
        await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == _productId);

        // Screen thinks 100 so "decrease 10" looks fine, but stock has since dropped to 4.
        await SetStockFromAnotherContextAsync(4m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(Request(InventoryAdjustmentDirection.Decrease, 10m)));

        Assert.Equal(4m, await StockAsync());
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Empty(await _fixture.Context.InventoryAdjustments.ToListAsync());
    }

    /// <summary>
    /// §15: two managers adjusting the same product. Both must land — 100 − 5 − 3 = 92 — with
    /// neither silently lost. Each service has its own context, as two open screens would.
    /// </summary>
    [Fact]
    public async Task TwoSequentialAdjustmentsFromDifferentContexts_BothApply()
    {
        using var contextA = SeparateContext();
        using var contextB = SeparateContext();

        var serviceA = new InventoryAdjustmentService(
            contextA, new EfSequenceGenerator(contextA), new EfAuditLogger(contextA),
            new PermissionEnforcer(contextA));
        var serviceB = new InventoryAdjustmentService(
            contextB, new EfSequenceGenerator(contextB), new EfAuditLogger(contextB),
            new PermissionEnforcer(contextB));

        // Both read 100 first, so each holds a cached quantity that the other is about to invalidate.
        Assert.Equal(100m, await serviceA.GetCurrentStockAsync(_productId));
        Assert.Equal(100m, await serviceB.GetCurrentStockAsync(_productId));

        await serviceA.CreateAsync(Request(InventoryAdjustmentDirection.Decrease, 5m));
        await serviceB.CreateAsync(Request(InventoryAdjustmentDirection.Decrease, 3m));

        // 92, not 95 or 97 — neither adjustment overwrote the other.
        Assert.Equal(92m, await StockAsync());

        using var verify = SeparateContext();
        var movements = await verify.StockMovements.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(100m, movements[0].PreviousQuantity);
        Assert.Equal(95m, movements[0].NewQuantity);
        Assert.Equal(95m, movements[1].PreviousQuantity);  // picked up A's change
        Assert.Equal(92m, movements[1].NewQuantity);
        Assert.Equal(2, await verify.InventoryAdjustments.CountAsync());
    }

    /// <summary>The ledger must reconcile: summing the movements from the starting quantity has to
    /// land exactly on current stock, with no adjustment unaccounted for.</summary>
    [Fact]
    public async Task LedgerReconcilesAfterInterleavedChanges()
    {
        await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == _productId);

        await _sut.CreateAsync(Request(InventoryAdjustmentDirection.Decrease, 5m));   // 100 -> 95
        await SetStockFromAnotherContextAsync(90m);                                    // a sale
        await _sut.CreateAsync(Request(InventoryAdjustmentDirection.Increase, 4m));    // 90 -> 94

        var movements = await _fixture.Context.StockMovements.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(95m, movements[0].NewQuantity);
        Assert.Equal(90m, movements[1].PreviousQuantity);
        Assert.Equal(94m, movements[1].NewQuantity);
        Assert.Equal(94m, await StockAsync());
    }

    public void Dispose() => _fixture.Dispose();
}
