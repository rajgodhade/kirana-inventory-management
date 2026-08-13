using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.StockCounts;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.StockCounts;

public class StockCountServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly StockCountService _sut;
    private readonly int _ownerId;

    public StockCountServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        _sut = new StockCountService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context),
            new BarcodeLookupService(_fixture.Context));
    }

    private async Task<Product> SeedProductAsync(
        string name = "Amul Butter 500g",
        decimal stock = 100m,
        UnitOfMeasure unit = UnitOfMeasure.Piece,
        bool isActive = true,
        params string[] barcodes)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..10],
            Name = name,
            Sku = $"SKU-{Guid.NewGuid():N}"[..10],
            Unit = unit,
            PurchasePrice = 10m,
            Mrp = 15m,
            SellingPrice = 14m,
            IsActive = isActive,
        };

        for (var i = 0; i < barcodes.Length; i++)
        {
            product.Barcodes.Add(new ProductBarcode
            {
                Value = barcodes[i],
                NormalizedValue = BarcodeNormalizer.Normalize(barcodes[i]),
                Symbology = BarcodeSymbology.Code128,
                IsPrimary = i == 0,
                IsActive = true,
            });
        }

        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<decimal> StockOfAsync(int productId) =>
        _fixture.Context.Inventories.Where(i => i.ProductId == productId)
            .Select(i => i.QuantityOnHand).FirstAsync();

    private Task<List<StockMovement>> MovementsOfAsync(int productId) =>
        _fixture.Context.StockMovements.Where(m => m.ProductId == productId).ToListAsync();

    /// <summary>Starts a count and records a physical quantity for one product, the setup almost
    /// every finalization test needs.</summary>
    private async Task<(StockCount Count, StockCountItem Item)> CountedAsync(Product product, decimal physical)
    {
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);
        item = await _sut.SetCountedQuantityAsync(item.Id, physical, null, _ownerId);
        return (count, item);
    }

    // ---- Creation ----

    [Fact]
    public async Task StartAsync_IssuesASequentialCountNumber()
    {
        var first = await _sut.StartAsync(null, _ownerId);
        await _sut.CancelAsync(first.Id, null, _ownerId);
        var second = await _sut.StartAsync(null, _ownerId);

        Assert.Equal("STK-COUNT-000001", first.CountNumber);
        Assert.Equal("STK-COUNT-000002", second.CountNumber);
    }

    [Fact]
    public async Task StartAsync_BeginsInProgress_AndTouchesNoStock()
    {
        var product = await SeedProductAsync(stock: 100m);

        var count = await _sut.StartAsync("Monthly count", _ownerId);

        Assert.Equal(StockCountStatus.InProgress, count.Status);
        Assert.Equal("Monthly count", count.Notes);
        Assert.Equal(100m, await StockOfAsync(product.Id));
        Assert.Empty(await MovementsOfAsync(product.Id));
    }

    [Fact]
    public async Task StartAsync_Throws_WhenACountIsAlreadyInProgress()
    {
        await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.StartAsync(null, _ownerId));
    }

    [Fact]
    public async Task StartAsync_IsAllowedAgain_AfterThepreviousCountCompletes()
    {
        var product = await SeedProductAsync();
        var (count, _) = await CountedAsync(product, 100m);
        await _sut.FinalizeAsync(count.Id, _ownerId);

        var next = await _sut.StartAsync(null, _ownerId);

        Assert.Equal(StockCountStatus.InProgress, next.Status);
    }

    [Fact]
    public async Task StartAsync_WritesAuditLog()
    {
        await _sut.StartAsync(null, _ownerId);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "StockCountStarted"));
    }

    // ---- Adding items / snapshots ----

    [Fact]
    public async Task AddItemAsync_SnapshotsSystemQuantityAndProductIdentity()
    {
        var product = await SeedProductAsync(name: "Tata Salt 1kg", stock: 120m);
        var count = await _sut.StartAsync(null, _ownerId);

        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        Assert.Equal(120m, item.SystemQuantity);
        Assert.Equal("Tata Salt 1kg", item.ProductNameSnapshot);
        Assert.Equal(product.ProductCode, item.ProductCodeSnapshot);
        Assert.Equal(product.Sku, item.SkuSnapshot);
        Assert.Equal(UnitOfMeasure.Piece, item.UnitSnapshot);
        Assert.Null(item.CountedQuantity);
        Assert.False(item.IsCounted);
    }

    /// <summary>The snapshot is the whole point of counting without freezing the shop: a sale after
    /// the item was added must not move the figure the counter is comparing against.</summary>
    [Fact]
    public async Task AddItemAsync_SnapshotDoesNotDrift_WhenStockChangesAfterwards()
    {
        var product = await SeedProductAsync(stock: 120m);
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 119m;
        await _fixture.Context.SaveChangesAsync();

        var reloaded = await _fixture.Context.StockCountItems.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(120m, reloaded.SystemQuantity);
    }

    [Fact]
    public async Task AddItemAsync_ReturnsTheExistingItem_RatherThanDuplicatingTheProduct()
    {
        var product = await SeedProductAsync();
        var count = await _sut.StartAsync(null, _ownerId);

        var first = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);
        var second = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await _fixture.Context.StockCountItems.Where(i => i.StockCountId == count.Id).ToListAsync());
    }

    [Fact]
    public async Task AddItemAsync_Throws_ForInactiveProduct()
    {
        var product = await SeedProductAsync(isActive: false);
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddItemAsync(count.Id, product.Id, null, _ownerId));
    }

    [Fact]
    public async Task AddItemAsync_Throws_WhenTheCountIsAlreadyCompleted()
    {
        var product = await SeedProductAsync();
        var other = await SeedProductAsync(name: "Second Product");
        var (count, _) = await CountedAsync(product, 100m);
        await _sut.FinalizeAsync(count.Id, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddItemAsync(count.Id, other.Id, null, _ownerId));
    }

    [Fact]
    public async Task AddItemAsync_UsesZeroSnapshot_ForAProductWithNoInventoryRow()
    {
        var product = new Product
        {
            ProductCode = "PRD-NOINV", Name = "Never Stocked", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 1m, Mrp = 2m, SellingPrice = 2m, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();
        var count = await _sut.StartAsync(null, _ownerId);

        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        Assert.Equal(0m, item.SystemQuantity);
    }

    // ---- Barcode entry (reuses the Phase 13B pipeline) ----

    [Fact]
    public async Task AddItemByBarcodeAsync_ResolvesThePrimaryBarcode()
    {
        var product = await SeedProductAsync(barcodes: ["8901030826501", "5012345678900"]);
        var count = await _sut.StartAsync(null, _ownerId);

        var item = await _sut.AddItemByBarcodeAsync(count.Id, "8901030826501", _ownerId);

        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal("8901030826501", item.BarcodeSnapshot);
    }

    /// <summary>Phase 13B's promise carried into counting: every code of a product reaches the same
    /// product, and therefore the same single count item.</summary>
    [Fact]
    public async Task AddItemByBarcodeAsync_ResolvesAnAlternateBarcode_ToTheSameSingleItem()
    {
        var product = await SeedProductAsync(barcodes: ["8901030826501", "5012345678900"]);
        var count = await _sut.StartAsync(null, _ownerId);

        var viaPrimary = await _sut.AddItemByBarcodeAsync(count.Id, "8901030826501", _ownerId);
        var viaAlternate = await _sut.AddItemByBarcodeAsync(count.Id, "5012345678900", _ownerId);

        Assert.Equal(viaPrimary.Id, viaAlternate.Id);
        Assert.Single(await _fixture.Context.StockCountItems.Where(i => i.StockCountId == count.Id).ToListAsync());
    }

    [Fact]
    public async Task AddItemByBarcodeAsync_Throws_ForUnknownBarcode()
    {
        await SeedProductAsync(barcodes: ["8901030826501"]);
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddItemByBarcodeAsync(count.Id, "NOSUCHCODE", _ownerId));
    }

    [Fact]
    public async Task AddItemByBarcodeAsync_Throws_ForRetiredBarcode()
    {
        var product = await SeedProductAsync(barcodes: ["ACTIVE-CODE", "RETIRED-CODE"]);
        var retired = await _fixture.Context.ProductBarcodes.FirstAsync(b => b.Value == "RETIRED-CODE");
        retired.IsActive = false;
        await _fixture.Context.SaveChangesAsync();
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddItemByBarcodeAsync(count.Id, "RETIRED-CODE", _ownerId));
    }

    [Fact]
    public async Task AddItemByBarcodeAsync_Throws_ForBarcodeOnAnInactiveProduct()
    {
        await SeedProductAsync(isActive: false, barcodes: ["INACTIVE-PROD"]);
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddItemByBarcodeAsync(count.Id, "INACTIVE-PROD", _ownerId));
    }

    // ---- Recording physical quantities ----

    [Fact]
    public async Task SetCountedQuantityAsync_RecordsTheQuantity_ButStillMovesNoStock()
    {
        var product = await SeedProductAsync(stock: 120m);
        var (_, item) = await CountedAsync(product, 118m);

        Assert.Equal(118m, item.CountedQuantity);
        Assert.True(item.IsCounted);
        Assert.NotNull(item.CountedAtUtc);
        Assert.Equal(120m, await StockOfAsync(product.Id));
        Assert.Empty(await MovementsOfAsync(product.Id));
    }

    [Theory]
    [InlineData(120, 118, -2)]
    [InlineData(50, 52, 2)]
    [InlineData(100, 100, 0)]
    public async Task VarianceQuantity_IsPhysicalMinusSystem(decimal system, decimal physical, decimal expected)
    {
        var product = await SeedProductAsync(stock: system);
        var (_, item) = await CountedAsync(product, physical);

        Assert.Equal(expected, item.VarianceQuantity);
    }

    [Fact]
    public async Task SetCountedQuantityAsync_AcceptsZero_AsAGenuineCountedValue()
    {
        var product = await SeedProductAsync(stock: 10m);
        var (_, item) = await CountedAsync(product, 0m);

        // "The shelf is empty" is a real answer, and must not read as uncounted.
        Assert.True(item.IsCounted);
        Assert.Equal(-10m, item.VarianceQuantity);
    }

    [Fact]
    public async Task SetCountedQuantityAsync_Throws_ForNegativeQuantity()
    {
        var product = await SeedProductAsync();
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SetCountedQuantityAsync(item.Id, -1m, null, _ownerId));
    }

    [Fact]
    public async Task SetCountedQuantityAsync_AcceptsDecimals_ForDecimalCapableUnits()
    {
        var rice = await SeedProductAsync(name: "Rice", stock: 20m, unit: UnitOfMeasure.Kilogram);
        var (_, item) = await CountedAsync(rice, 12.5m);

        Assert.Equal(12.5m, item.CountedQuantity);
        Assert.Equal(-7.5m, item.VarianceQuantity);
    }

    [Fact]
    public async Task SetCountedQuantityAsync_Rejects_FractionalQuantityForAWholeUnit()
    {
        var product = await SeedProductAsync(unit: UnitOfMeasure.Piece);
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        // A shelf holds 3 packets, never 3.5.
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SetCountedQuantityAsync(item.Id, 3.5m, null, _ownerId));
    }

    [Fact]
    public async Task SetCountedQuantityAsync_RoundsToTheSchemaQuantityScale()
    {
        var oil = await SeedProductAsync(name: "Oil", stock: 10m, unit: UnitOfMeasure.Litre);
        var (_, item) = await CountedAsync(oil, 8.7554m);

        // 18,3 is the schema's quantity precision; rounding on the way in keeps the stored value
        // and the variance arithmetic in agreement.
        Assert.Equal(8.755m, item.CountedQuantity);
    }

    [Fact]
    public async Task SetCountedQuantityAsync_CanBeCorrected_WhileTheCountIsOpen()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (_, item) = await CountedAsync(product, 97m);

        var corrected = await _sut.SetCountedQuantityAsync(item.Id, 99m, null, _ownerId);

        Assert.Equal(99m, corrected.CountedQuantity);
    }

    [Fact]
    public async Task SetCountedQuantityAsync_WritesAuditLog()
    {
        var product = await SeedProductAsync();
        await CountedAsync(product, 98m);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "StockCountProductCounted"));
    }

    // ---- Removing items / notes ----

    [Fact]
    public async Task RemoveItemAsync_DropsTheItem_WhileInProgress()
    {
        var product = await SeedProductAsync();
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        await _sut.RemoveItemAsync(item.Id, _ownerId);

        Assert.Empty(await _fixture.Context.StockCountItems.Where(i => i.StockCountId == count.Id).ToListAsync());
    }

    [Fact]
    public async Task SetNotesAsync_UpdatesNotes_WhileInProgress()
    {
        var count = await _sut.StartAsync(null, _ownerId);

        await _sut.SetNotesAsync(count.Id, "Counted by Ramesh", _ownerId);

        var reloaded = await _fixture.Context.StockCounts.FirstAsync(c => c.Id == count.Id);
        Assert.Equal("Counted by Ramesh", reloaded.Notes);
    }

    // ---- Cancellation ----

    [Fact]
    public async Task CancelAsync_ClosesTheCount_WithoutTouchingInventory()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 80m);

        await _sut.CancelAsync(count.Id, "Miscounted aisle", _ownerId);

        var reloaded = await _fixture.Context.StockCounts.FirstAsync(c => c.Id == count.Id);
        Assert.Equal(StockCountStatus.Cancelled, reloaded.Status);
        Assert.Equal(100m, await StockOfAsync(product.Id));
        Assert.Empty(await MovementsOfAsync(product.Id));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "StockCountCancelled"));
    }

    [Fact]
    public async Task CancelledCount_CannotBeFinalized()
    {
        var product = await SeedProductAsync();
        var (count, _) = await CountedAsync(product, 90m);
        await _sut.CancelAsync(count.Id, null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FinalizeAsync(count.Id, _ownerId));
    }

    // ---- Finalization ----

    [Fact]
    public async Task FinalizeAsync_DecreasesStock_ForAShortage()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 97m);

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(97m, await StockOfAsync(product.Id));
        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(StockMovementType.StockCountDecrease, movement.MovementType);
        Assert.Equal(-3m, movement.QuantityChange);
        Assert.Equal(100m, movement.PreviousQuantity);
        Assert.Equal(97m, movement.NewQuantity);
        Assert.Equal(1, result.DecreasedCount);
    }

    [Fact]
    public async Task FinalizeAsync_IncreasesStock_ForASurplus()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 103m);

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(103m, await StockOfAsync(product.Id));
        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(StockMovementType.StockCountIncrease, movement.MovementType);
        Assert.Equal(3m, movement.QuantityChange);
        Assert.Equal(100m, movement.PreviousQuantity);
        Assert.Equal(103m, movement.NewQuantity);
        Assert.Equal(1, result.IncreasedCount);
    }

    [Fact]
    public async Task FinalizeAsync_WritesNoMovement_ForZeroVariance()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 100m);

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(100m, await StockOfAsync(product.Id));
        // A ledger row saying "nothing changed" is noise that hides real shrinkage.
        Assert.Empty(await MovementsOfAsync(product.Id));
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(0, result.AdjustmentCount);
    }

    [Fact]
    public async Task FinalizeAsync_StampsTheCountNumberOnEveryMovement()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 95m);

        await _sut.FinalizeAsync(count.Id, _ownerId);

        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(nameof(StockCount), movement.ReferenceType);
        Assert.Equal(count.CountNumber, movement.ReferenceId);
        Assert.Equal("Physical stock count", movement.Reason);
        Assert.Equal(_ownerId, movement.UserId);
    }

    [Fact]
    public async Task FinalizeAsync_SkipsUncountedItems_RatherThanTreatingThemAsZero()
    {
        var counted = await SeedProductAsync(name: "Counted", stock: 100m);
        var skipped = await SeedProductAsync(name: "Never Counted", stock: 60m);
        var count = await _sut.StartAsync(null, _ownerId);
        var countedItem = await _sut.AddItemAsync(count.Id, counted.Id, null, _ownerId);
        await _sut.AddItemAsync(count.Id, skipped.Id, null, _ownerId);
        await _sut.SetCountedQuantityAsync(countedItem.Id, 98m, null, _ownerId);

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        // An item nobody counted must not be read as "found zero on the shelf".
        Assert.Equal(60m, await StockOfAsync(skipped.Id));
        Assert.Empty(await MovementsOfAsync(skipped.Id));
        Assert.Equal(1, result.ProductsCounted);
    }

    [Fact]
    public async Task FinalizeAsync_MarksTheCountCompleted()
    {
        var product = await SeedProductAsync();
        var (count, _) = await CountedAsync(product, 99m);

        await _sut.FinalizeAsync(count.Id, _ownerId);

        var reloaded = await _fixture.Context.StockCounts.FirstAsync(c => c.Id == count.Id);
        Assert.Equal(StockCountStatus.Completed, reloaded.Status);
        Assert.NotNull(reloaded.CompletedAtUtc);
        Assert.Equal(_ownerId, reloaded.CompletedByUserId);
    }

    [Fact]
    public async Task FinalizeAsync_Throws_WhenNothingHasBeenCounted()
    {
        var product = await SeedProductAsync();
        var count = await _sut.StartAsync(null, _ownerId);
        await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FinalizeAsync(count.Id, _ownerId));
    }

    [Fact]
    public async Task FinalizeAsync_CannotRunTwice()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 97m);
        await _sut.FinalizeAsync(count.Id, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FinalizeAsync(count.Id, _ownerId));

        // The critical part: the second attempt must not have applied the variance again.
        Assert.Equal(97m, await StockOfAsync(product.Id));
        Assert.Single(await MovementsOfAsync(product.Id));
    }

    [Fact]
    public async Task CompletedCount_CannotHaveQuantitiesEdited()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, item) = await CountedAsync(product, 97m);
        await _sut.FinalizeAsync(count.Id, _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetCountedQuantityAsync(item.Id, 50m, null, _ownerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RemoveItemAsync(item.Id, _ownerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SetNotesAsync(count.Id, "changed", _ownerId));
    }

    [Fact]
    public async Task FinalizeAsync_HandlesAMixedCount()
    {
        var down = await SeedProductAsync(name: "Shortage", stock: 100m);
        var up = await SeedProductAsync(name: "Surplus", stock: 50m);
        var same = await SeedProductAsync(name: "Exact", stock: 30m);

        var count = await _sut.StartAsync(null, _ownerId);
        foreach (var (product, physical) in new[] { (down, 97m), (up, 52m), (same, 30m) })
        {
            var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);
            await _sut.SetCountedQuantityAsync(item.Id, physical, null, _ownerId);
        }

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(3, result.ProductsCounted);
        Assert.Equal(1, result.IncreasedCount);
        Assert.Equal(1, result.DecreasedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(2, result.AdjustmentCount);
        Assert.Equal(2m, result.TotalIncreaseQuantity);
        Assert.Equal(3m, result.TotalDecreaseQuantity);
        Assert.Equal(97m, await StockOfAsync(down.Id));
        Assert.Equal(52m, await StockOfAsync(up.Id));
        Assert.Equal(30m, await StockOfAsync(same.Id));
    }

    [Fact]
    public async Task FinalizeAsync_WritesAuditLog()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 97m);

        await _sut.FinalizeAsync(count.Id, _ownerId);

        var audit = await _fixture.Context.AuditLogs.FirstAsync(a => a.Action == "StockCountCompleted");
        Assert.Contains(count.CountNumber, audit.NewValue!);
        Assert.Contains("1 decreased", audit.NewValue!);
    }

    // ---- Concurrency: stock moving during an open count ----

    /// <summary>
    /// The scenario the whole rebase strategy exists for. Snapshot 100, a sale takes stock to 98,
    /// physical count is 97. Blindly applying the observed -3 would land on 95 — losing two units
    /// that were legitimately sold. The adjustment must rebase onto live stock so the result is the
    /// counted figure, 97.
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_RebasesOntoLiveStock_WhenInventoryMovedDuringTheCount()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 97m);

        // A sale happens while the count is open.
        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 98m;
        await _fixture.Context.SaveChangesAsync();

        await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(97m, await StockOfAsync(product.Id));
        Assert.NotEqual(95m, await StockOfAsync(product.Id));

        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(-1m, movement.QuantityChange);
        Assert.Equal(98m, movement.PreviousQuantity);
        Assert.Equal(97m, movement.NewQuantity);
    }

    /// <summary>
    /// The realistic version of the concurrency test: the sale happens on a DIFFERENT DbContext,
    /// exactly as a POS sale does while a count screen is open. The counting context is already
    /// tracking those Inventory rows, so EF's identity map hands back the cached quantity and a
    /// plain requery silently returns stale data.
    ///
    /// <para>The sibling test that mutates stock through the SAME context passes either way, which
    /// is precisely why this one exists — the original implementation passed every in-process test
    /// while being wrong against a real database.</para>
    /// </summary>
    [Fact]
    public async Task FinalizeAsync_SeesStockChangedByAnotherContext_AndRebasesAgainstIt()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, item) = await CountedAsync(product, 97m);

        // A sale from an independent context, sharing only the underlying database.
        using (var otherContext = new KiranaDbContext(
            new DbContextOptionsBuilder<KiranaDbContext>()
                .UseSqlite(_fixture.Context.Database.GetDbConnection()).Options))
        {
            var inventory = await otherContext.Inventories.FirstAsync(i => i.ProductId == product.Id);
            inventory.QuantityOnHand = 98m;
            await otherContext.SaveChangesAsync();
        }

        var preview = await _sut.GetVariancePreviewAsync(count.Id);
        Assert.True(preview.HasRebases);
        Assert.Equal(-1m, preview.Lines[0].AppliedAdjustment);

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(97m, await StockOfAsync(product.Id));
        Assert.Equal(1, result.RebasedCount);

        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(-1m, movement.QuantityChange);
        Assert.Equal(98m, movement.PreviousQuantity);
        Assert.Equal(97m, movement.NewQuantity);

        var reloaded = await _fixture.Context.StockCountItems.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(98m, reloaded.SystemQuantityAtFinalization);
    }

    [Fact]
    public async Task FinalizeAsync_RecordsThatALineWasRebased()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, item) = await CountedAsync(product, 97m);

        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 98m;
        await _fixture.Context.SaveChangesAsync();

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(1, result.RebasedCount);
        var reloaded = await _fixture.Context.StockCountItems.FirstAsync(i => i.Id == item.Id);
        Assert.Equal(98m, reloaded.SystemQuantityAtFinalization);
        // The original snapshot survives untouched, so the count still reports what was observed.
        Assert.Equal(100m, reloaded.SystemQuantity);
        Assert.Equal(-3m, reloaded.VarianceQuantity);
    }

    [Fact]
    public async Task FinalizeAsync_LeavesRebaseMarkerNull_WhenNothingMoved()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, item) = await CountedAsync(product, 97m);

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(0, result.RebasedCount);
        var reloaded = await _fixture.Context.StockCountItems.FirstAsync(i => i.Id == item.Id);
        Assert.Null(reloaded.SystemQuantityAtFinalization);
    }

    /// <summary>Rebasing must work in the other direction too: a restock during the count.</summary>
    [Fact]
    public async Task FinalizeAsync_RebasesCorrectly_WhenStockIncreasedDuringTheCount()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 130m);

        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 125m; // a delivery landed mid-count
        await _fixture.Context.SaveChangesAsync();

        await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(130m, await StockOfAsync(product.Id));
        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(5m, movement.QuantityChange);
    }

    /// <summary>When the mid-count movement happens to land exactly on the counted figure there is
    /// nothing left to apply, and no movement should be written.</summary>
    [Fact]
    public async Task FinalizeAsync_WritesNoMovement_WhenLiveStockAlreadyMatchesTheCount()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 97m);

        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 97m;
        await _fixture.Context.SaveChangesAsync();

        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Empty(await MovementsOfAsync(product.Id));
        Assert.Equal(97m, await StockOfAsync(product.Id));
        Assert.Equal(1, result.RebasedCount);
    }

    // ---- Variance preview ----

    [Fact]
    public async Task GetVariancePreviewAsync_SummarisesWithoutWritingAnything()
    {
        var down = await SeedProductAsync(name: "Shortage", stock: 100m);
        var up = await SeedProductAsync(name: "Surplus", stock: 50m);
        var count = await _sut.StartAsync(null, _ownerId);
        foreach (var (product, physical) in new[] { (down, 97m), (up, 52m) })
        {
            var item = await _sut.AddItemAsync(count.Id, product.Id, null, _ownerId);
            await _sut.SetCountedQuantityAsync(item.Id, physical, null, _ownerId);
        }

        var preview = await _sut.GetVariancePreviewAsync(count.Id);

        Assert.Equal(2, preview.CountedItems);
        Assert.Equal(1, preview.IncreaseCount);
        Assert.Equal(1, preview.DecreaseCount);
        Assert.Equal(2m, preview.TotalIncreaseQuantity);
        Assert.Equal(3m, preview.TotalDecreaseQuantity);
        Assert.Equal(2, preview.AdjustmentCount);

        // Preview must be pure: nothing applied.
        Assert.Equal(100m, await StockOfAsync(down.Id));
        Assert.Equal(50m, await StockOfAsync(up.Id));
        Assert.Empty(await MovementsOfAsync(down.Id));
        var reloaded = await _fixture.Context.StockCounts.FirstAsync(c => c.Id == count.Id);
        Assert.Equal(StockCountStatus.InProgress, reloaded.Status);
    }

    [Fact]
    public async Task GetVariancePreviewAsync_FlagsLinesThatWillBeRebased()
    {
        var product = await SeedProductAsync(stock: 100m);
        var (count, _) = await CountedAsync(product, 97m);

        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 98m;
        await _fixture.Context.SaveChangesAsync();

        var preview = await _sut.GetVariancePreviewAsync(count.Id);

        Assert.True(preview.HasRebases);
        var line = Assert.Single(preview.RebasedLines);
        Assert.Equal(-3m, line.ObservedVariance);   // what the counter saw
        Assert.Equal(-1m, line.AppliedAdjustment);  // what will actually be applied
        Assert.Equal(97m, line.ResultingQuantity);
    }

    [Fact]
    public async Task GetVariancePreviewAsync_CountsUncountedItemsSeparately()
    {
        var counted = await SeedProductAsync(name: "Counted", stock: 10m);
        var pending = await SeedProductAsync(name: "Pending", stock: 10m);
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, counted.Id, null, _ownerId);
        await _sut.AddItemAsync(count.Id, pending.Id, null, _ownerId);
        await _sut.SetCountedQuantityAsync(item.Id, 9m, null, _ownerId);

        var preview = await _sut.GetVariancePreviewAsync(count.Id);

        Assert.Equal(2, preview.TotalItems);
        Assert.Equal(1, preview.CountedItems);
        Assert.Equal(1, preview.UncountedItems);
    }

    // ---- Summaries ----

    [Fact]
    public async Task GetSummariesAsync_ReportsProgressAndVarianceCounts()
    {
        var a = await SeedProductAsync(name: "A", stock: 10m);
        var b = await SeedProductAsync(name: "B", stock: 10m);
        var count = await _sut.StartAsync(null, _ownerId);
        var itemA = await _sut.AddItemAsync(count.Id, a.Id, null, _ownerId);
        await _sut.AddItemAsync(count.Id, b.Id, null, _ownerId);
        await _sut.SetCountedQuantityAsync(itemA.Id, 8m, null, _ownerId);

        var summary = Assert.Single(await _sut.GetSummariesAsync());

        Assert.Equal(count.CountNumber, summary.CountNumber);
        Assert.Equal(2, summary.ItemCount);
        Assert.Equal(1, summary.CountedItemCount);
        Assert.Equal(1, summary.VarianceItemCount);
        Assert.Equal(StockCountStatus.InProgress, summary.Status);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsNull_WhenNoCountIsOpen()
    {
        Assert.Null(await _sut.GetActiveAsync());
    }

    public void Dispose() => _fixture.Dispose();
}
