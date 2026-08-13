using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Inventories;

public class InventoryAdjustmentServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly InventoryAdjustmentService _sut;
    private readonly int _ownerId;

    public InventoryAdjustmentServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        _sut = new InventoryAdjustmentService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context));
    }

    private async Task<Product> SeedProductAsync(
        string name = "Amul Butter 500g",
        decimal stock = 100m,
        UnitOfMeasure unit = UnitOfMeasure.Piece,
        bool withInventory = true)
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
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        if (withInventory)
        {
            _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        }

        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private CreateInventoryAdjustmentRequest Request(
        int productId,
        InventoryAdjustmentDirection direction = InventoryAdjustmentDirection.Decrease,
        decimal quantity = 5m,
        InventoryAdjustmentReason reason = InventoryAdjustmentReason.Damaged,
        string? notes = null,
        int? userId = null) => new()
        {
            ProductId = productId,
            Direction = direction,
            Quantity = quantity,
            Reason = reason,
            Notes = notes,
            PerformedByUserId = userId ?? _ownerId,
        };

    private Task<decimal> StockOfAsync(int productId) =>
        _fixture.Context.Inventories.Where(i => i.ProductId == productId)
            .Select(i => i.QuantityOnHand).FirstAsync();

    private Task<List<StockMovement>> MovementsOfAsync(int productId) =>
        _fixture.Context.StockMovements.Where(m => m.ProductId == productId).ToListAsync();

    // ---- Creation ----

    [Fact]
    public async Task Increase_AddsStockAndRecordsTheAdjustment()
    {
        var product = await SeedProductAsync(stock: 100m);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Increase, 5m, InventoryAdjustmentReason.Found));

        Assert.Equal(105m, await StockOfAsync(product.Id));
        Assert.Equal(100m, adjustment.PreviousQuantity);
        Assert.Equal(105m, adjustment.NewQuantity);
        Assert.Equal(5m, adjustment.AdjustmentQuantity);
        Assert.Equal(5m, adjustment.SignedQuantity);
        Assert.Equal(InventoryAdjustmentReason.Found, adjustment.Reason);
    }

    [Fact]
    public async Task Decrease_RemovesStockAndRecordsTheAdjustment()
    {
        var product = await SeedProductAsync(stock: 100m);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Decrease, 5m, InventoryAdjustmentReason.Damaged));

        Assert.Equal(95m, await StockOfAsync(product.Id));
        Assert.Equal(100m, adjustment.PreviousQuantity);
        Assert.Equal(95m, adjustment.NewQuantity);
        Assert.Equal(5m, adjustment.AdjustmentQuantity); // magnitude stays positive
        Assert.Equal(-5m, adjustment.SignedQuantity);
    }

    [Fact]
    public async Task AdjustmentNumbers_AreSequential()
    {
        var product = await SeedProductAsync(stock: 100m);

        var first = await _sut.CreateAsync(Request(product.Id));
        var second = await _sut.CreateAsync(Request(product.Id));

        Assert.Equal("ADJ-000001", first.AdjustmentNumber);
        Assert.Equal("ADJ-000002", second.AdjustmentNumber);
    }

    [Fact]
    public async Task SnapshotsProductIdentityAtTheTimeOfAdjustment()
    {
        var product = await SeedProductAsync(name: "Tata Salt 1kg", stock: 50m, unit: UnitOfMeasure.Kilogram);

        var adjustment = await _sut.CreateAsync(Request(product.Id, quantity: 2m));

        Assert.Equal("Tata Salt 1kg", adjustment.ProductNameSnapshot);
        Assert.Equal(product.ProductCode, adjustment.ProductCodeSnapshot);
        Assert.Equal(product.Sku, adjustment.SkuSnapshot);
        Assert.Equal(UnitOfMeasure.Kilogram, adjustment.UnitSnapshot);
    }

    /// <summary>Renaming a product must not rewrite the history of what was corrected.</summary>
    [Fact]
    public async Task Snapshot_SurvivesAProductRename()
    {
        var product = await SeedProductAsync(name: "Original Name", stock: 50m);
        var adjustment = await _sut.CreateAsync(Request(product.Id));

        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.Name = "Renamed Later";
        await _fixture.Context.SaveChangesAsync();

        var reloaded = await _fixture.Context.InventoryAdjustments.FirstAsync(a => a.Id == adjustment.Id);
        Assert.Equal("Original Name", reloaded.ProductNameSnapshot);
    }

    [Fact]
    public async Task RecordsWhoAdjustedAndWhen()
    {
        var product = await SeedProductAsync();

        var adjustment = await _sut.CreateAsync(Request(product.Id));

        Assert.Equal(_ownerId, adjustment.AdjustedByUserId);
        Assert.True(adjustment.AdjustedAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public async Task CreatesInventoryRow_ForAProductThatNeverHadOne()
    {
        var product = await SeedProductAsync(withInventory: false);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Increase, 12m, InventoryAdjustmentReason.OpeningBalance));

        Assert.Equal(0m, adjustment.PreviousQuantity);
        Assert.Equal(12m, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Throws_WhenProductNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(Request(999999)));
    }

    // ---- Quantity validation ----

    [Fact]
    public async Task Throws_ForZeroQuantity()
    {
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(product.Id, quantity: 0m)));
    }

    /// <summary>Direction carries the sign, so a negative magnitude expresses direction twice.
    /// Rejected rather than guessing which one the caller meant.</summary>
    [Fact]
    public async Task Throws_ForNegativeQuantity()
    {
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(product.Id, quantity: -5m)));
    }

    [Fact]
    public async Task Throws_ForQuantityThatRoundsAwayToZero()
    {
        var product = await SeedProductAsync(unit: UnitOfMeasure.Kilogram);

        // Below the schema's 3-decimal precision: recording it would create a ledger row that
        // changes no stock.
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(product.Id, quantity: 0.0001m)));
    }

    [Fact]
    public async Task RejectedQuantity_MutatesNothing()
    {
        var product = await SeedProductAsync(stock: 100m);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(product.Id, quantity: 0m)));

        Assert.Equal(100m, await StockOfAsync(product.Id));
        Assert.Empty(await MovementsOfAsync(product.Id));
        Assert.Empty(await _fixture.Context.InventoryAdjustments.ToListAsync());
    }

    // ---- Reason and notes ----

    [Theory]
    [InlineData(InventoryAdjustmentReason.Damaged)]
    [InlineData(InventoryAdjustmentReason.Expired)]
    [InlineData(InventoryAdjustmentReason.Lost)]
    [InlineData(InventoryAdjustmentReason.TheftOrShrinkage)]
    [InlineData(InventoryAdjustmentReason.DataCorrection)]
    [InlineData(InventoryAdjustmentReason.OpeningBalance)]
    public async Task StandardReasons_DoNotRequireNotes(InventoryAdjustmentReason reason)
    {
        var product = await SeedProductAsync(stock: 100m);

        var adjustment = await _sut.CreateAsync(Request(product.Id, quantity: 1m, reason: reason));

        Assert.Equal(reason, adjustment.Reason);
        Assert.Null(adjustment.Notes);
    }

    /// <summary>"Other" says nothing on its own, so it cannot become an unexplained catch-all.</summary>
    [Fact]
    public async Task OtherReason_RequiresNotes()
    {
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(Request(product.Id, reason: InventoryAdjustmentReason.Other)));
    }

    [Fact]
    public async Task OtherReason_RejectsWhitespaceOnlyNotes()
    {
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(Request(product.Id, reason: InventoryAdjustmentReason.Other, notes: "   ")));
    }

    [Fact]
    public async Task OtherReason_SucceedsWithNotes()
    {
        var product = await SeedProductAsync(stock: 100m);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, reason: InventoryAdjustmentReason.Other, notes: "  Supplier recall  "));

        Assert.Equal("Supplier recall", adjustment.Notes); // trimmed
    }

    [Fact]
    public async Task NotesAreTrimmed_AndBlankBecomesNull()
    {
        var product = await SeedProductAsync(stock: 100m);

        var adjustment = await _sut.CreateAsync(Request(product.Id, notes: "   "));

        Assert.Null(adjustment.Notes);
    }

    // ---- Negative stock protection ----

    [Fact]
    public async Task Throws_WhenDecreaseWouldGoNegative()
    {
        var product = await SeedProductAsync(stock: 3m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 5m)));
    }

    /// <summary>The refusal must leave absolutely nothing behind — §12's explicit requirement.</summary>
    [Fact]
    public async Task NegativeStockRefusal_MutatesNothingAtAll()
    {
        var product = await SeedProductAsync(stock: 3m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 5m)));

        Assert.Equal(3m, await StockOfAsync(product.Id));
        Assert.Empty(await MovementsOfAsync(product.Id));
        Assert.Empty(await _fixture.Context.InventoryAdjustments.ToListAsync());
        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "InventoryAdjusted"));
    }

    [Fact]
    public async Task DecreasingToExactlyZero_IsAllowed()
    {
        var product = await SeedProductAsync(stock: 5m);

        var adjustment = await _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 5m));

        Assert.Equal(0m, await StockOfAsync(product.Id));
        Assert.Equal(0m, adjustment.NewQuantity);
    }

    // ---- Stock movements ----

    [Fact]
    public async Task Increase_WritesAnInventoryAdjustmentIncreaseMovement()
    {
        var product = await SeedProductAsync(stock: 100m);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Increase, 5m, InventoryAdjustmentReason.Found));

        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(StockMovementType.InventoryAdjustmentIncrease, movement.MovementType);
        Assert.Equal(5m, movement.QuantityChange);
        Assert.Equal(100m, movement.PreviousQuantity);
        Assert.Equal(105m, movement.NewQuantity);
        Assert.Equal(nameof(InventoryAdjustment), movement.ReferenceType);
        Assert.Equal(adjustment.AdjustmentNumber, movement.ReferenceId);
        Assert.Equal("Found", movement.Reason);
        Assert.Equal(_ownerId, movement.UserId);
    }

    [Fact]
    public async Task Decrease_WritesAnInventoryAdjustmentDecreaseMovement()
    {
        var product = await SeedProductAsync(stock: 100m);

        await _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 5m));

        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.Equal(StockMovementType.InventoryAdjustmentDecrease, movement.MovementType);
        Assert.Equal(-5m, movement.QuantityChange);
        Assert.Equal(95m, movement.NewQuantity);
    }

    /// <summary>Damage recorded here must NOT reuse StockMovementType.Damaged: that type is written
    /// by the sales-return flow and feeds the damaged-stock report, so sharing it would mix
    /// goods-returned-broken with shelf breakage.</summary>
    [Fact]
    public async Task DamagedReason_DoesNotWriteTheSalesReturnDamagedMovementType()
    {
        var product = await SeedProductAsync(stock: 100m);

        await _sut.CreateAsync(Request(product.Id, reason: InventoryAdjustmentReason.Damaged));

        var movement = Assert.Single(await MovementsOfAsync(product.Id));
        Assert.NotEqual(StockMovementType.Damaged, movement.MovementType);
        Assert.Equal(StockMovementType.InventoryAdjustmentDecrease, movement.MovementType);
    }

    /// <summary>All four adjustment-ish movement types must stay separable in the ledger — the whole
    /// reason 13C and 13D have their own types.</summary>
    [Fact]
    public void StockCountAndInventoryAdjustmentMovementTypes_AreDistinct()
    {
        var types = new[]
        {
            StockMovementType.StockCountIncrease,
            StockMovementType.StockCountDecrease,
            StockMovementType.InventoryAdjustmentIncrease,
            StockMovementType.InventoryAdjustmentDecrease,
        };

        Assert.Equal(4, types.Distinct().Count());
        Assert.True(StockMovementType.InventoryAdjustmentIncrease.IsIncrease());
        Assert.False(StockMovementType.InventoryAdjustmentDecrease.IsIncrease());
        Assert.True(StockMovementType.StockCountIncrease.IsIncrease());
        Assert.False(StockMovementType.StockCountDecrease.IsIncrease());
    }

    [Fact]
    public async Task SuccessiveAdjustments_ChainPreviousAndNewQuantitiesCorrectly()
    {
        var product = await SeedProductAsync(stock: 100m);

        await _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 5m));
        await _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Increase, 20m,
            InventoryAdjustmentReason.Found));

        var movements = (await MovementsOfAsync(product.Id)).OrderBy(m => m.Id).ToList();
        Assert.Equal(2, movements.Count);
        Assert.Equal(100m, movements[0].PreviousQuantity);
        Assert.Equal(95m, movements[0].NewQuantity);
        Assert.Equal(95m, movements[1].PreviousQuantity);
        Assert.Equal(115m, movements[1].NewQuantity);
        Assert.Equal(115m, await StockOfAsync(product.Id));
    }

    // ---- Units and decimals ----

    [Theory]
    [InlineData(UnitOfMeasure.Kilogram, 12.5)]
    [InlineData(UnitOfMeasure.Litre, 8.75)]
    [InlineData(UnitOfMeasure.Gram, 250.125)]
    public async Task AcceptsDecimalQuantities(UnitOfMeasure unit, double quantity)
    {
        var product = await SeedProductAsync(stock: 500m, unit: unit);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Decrease, (decimal)quantity));

        Assert.Equal((decimal)quantity, adjustment.AdjustmentQuantity);
        Assert.Equal(500m - (decimal)quantity, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task RoundsToTheSchemaQuantityScale()
    {
        var product = await SeedProductAsync(stock: 100m, unit: UnitOfMeasure.Litre);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Decrease, 8.7554m));

        Assert.Equal(8.755m, adjustment.AdjustmentQuantity);
    }

    [Fact]
    public async Task PieceQuantitiesRoundTripExactly()
    {
        var product = await SeedProductAsync(stock: 100m, unit: UnitOfMeasure.Piece);

        await _sut.CreateAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 3m));

        Assert.Equal(97m, await StockOfAsync(product.Id));
    }

    // ---- Preview ----

    [Fact]
    public async Task Preview_ComputesTheResultWithoutWritingAnything()
    {
        var product = await SeedProductAsync(stock: 120m);

        var preview = await _sut.PreviewAsync(Request(
            product.Id, InventoryAdjustmentDirection.Decrease, 5m));

        Assert.Equal(120m, preview.CurrentQuantity);
        Assert.Equal(-5m, preview.SignedQuantity);
        Assert.Equal(115m, preview.ResultingQuantity);
        Assert.Equal("120 → 115", preview.TransitionText);
        Assert.Equal("-5", preview.SignedQuantityText);
        Assert.False(preview.WouldGoNegative);

        // Nothing applied.
        Assert.Equal(120m, await StockOfAsync(product.Id));
        Assert.Empty(await MovementsOfAsync(product.Id));
        Assert.Empty(await _fixture.Context.InventoryAdjustments.ToListAsync());
    }

    [Fact]
    public async Task Preview_FlagsAnAdjustmentThatWouldGoNegative()
    {
        var product = await SeedProductAsync(stock: 3m);

        var preview = await _sut.PreviewAsync(Request(product.Id, InventoryAdjustmentDirection.Decrease, 5m));

        Assert.True(preview.WouldGoNegative);
        Assert.Equal(-2m, preview.ResultingQuantity);
    }

    [Fact]
    public async Task Preview_ShowsSignedTextForAnIncrease()
    {
        var product = await SeedProductAsync(stock: 100m);

        var preview = await _sut.PreviewAsync(Request(product.Id, InventoryAdjustmentDirection.Increase, 5m));

        Assert.Equal("+5", preview.SignedQuantityText);
        Assert.Equal(105m, preview.ResultingQuantity);
    }

    // ---- Audit ----

    [Fact]
    public async Task WritesAnAuditEntryWithEnoughDetailToReconstructTheChange()
    {
        var product = await SeedProductAsync(name: "Amul Butter 500g", stock: 120m);

        var adjustment = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Decrease, 5m,
            InventoryAdjustmentReason.Damaged, "5 packets damaged during handling"));

        var audit = await _fixture.Context.AuditLogs.FirstAsync(a => a.Action == "InventoryAdjusted");
        Assert.Equal(_ownerId, audit.UserId);
        Assert.Equal(nameof(InventoryAdjustment), audit.Entity);
        Assert.Equal(adjustment.Id.ToString(), audit.EntityId);
        Assert.Equal("120", audit.PreviousValue);

        // Asserted as components rather than one exact sentence, so wording tweaks don't break the
        // test while the reconstructable facts stay pinned.
        // Reads: "ADJ-000001: PRD-… Amul Butter 500g -5 Piece (120 -> 115), reason Damaged"
        var summary = audit.NewValue!;
        Assert.Contains(adjustment.AdjustmentNumber, summary);   // which adjustment
        Assert.Contains(product.ProductCode, summary);           // which product
        Assert.Contains("Amul Butter 500g", summary);
        Assert.Contains("-5", summary);                          // direction + magnitude
        Assert.Contains("Piece", summary);                       // unit
        Assert.Contains("(120 -> 115)", summary);                // from -> to
        Assert.Contains("Damaged", summary);                     // why
        Assert.Equal("5 packets damaged during handling", audit.Reason);
    }

    [Fact]
    public async Task AuditReasonFallsBackToTheReasonLabel_WhenThereAreNoNotes()
    {
        var product = await SeedProductAsync(stock: 100m);

        await _sut.CreateAsync(Request(product.Id, reason: InventoryAdjustmentReason.TheftOrShrinkage));

        var audit = await _fixture.Context.AuditLogs.FirstAsync(a => a.Action == "InventoryAdjusted");
        Assert.Equal("Theft / shrinkage", audit.Reason);
    }

    // ---- Immutability ----

    /// <summary>There is no edit/delete/re-finalize API at all — the correction path is a
    /// compensating adjustment, which keeps both the error and the fix in the ledger.</summary>
    [Fact]
    public void ServiceExposesNoMutationOfCompletedAdjustments()
    {
        var methods = typeof(IInventoryAdjustmentService).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain(methods, m => m.Contains("Update", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, m => m.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, m => m.Contains("Edit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, m => m.Contains("Cancel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, m => m.Contains("Finalize", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompensatingAdjustment_LeavesTheOriginalIntact()
    {
        var product = await SeedProductAsync(stock: 100m);
        var mistake = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Decrease, 5m, InventoryAdjustmentReason.Damaged));

        var fix = await _sut.CreateAsync(Request(
            product.Id, InventoryAdjustmentDirection.Increase, 5m,
            InventoryAdjustmentReason.DataCorrection, "Reverses ADJ-000001, entered in error"));

        Assert.Equal(100m, await StockOfAsync(product.Id));

        var original = await _fixture.Context.InventoryAdjustments.FirstAsync(a => a.Id == mistake.Id);
        Assert.Equal(5m, original.AdjustmentQuantity);
        Assert.Equal(InventoryAdjustmentDirection.Decrease, original.Direction);
        Assert.Equal(95m, original.NewQuantity);

        // Both survive in the ledger — the error is not erased.
        Assert.Equal(2, (await MovementsOfAsync(product.Id)).Count);
        Assert.Equal(2, await _fixture.Context.InventoryAdjustments.CountAsync());
        Assert.NotEqual(mistake.AdjustmentNumber, fix.AdjustmentNumber);
    }

    // ---- History / filters ----

    [Fact]
    public async Task SearchAsync_ReturnsNewestFirst()
    {
        var product = await SeedProductAsync(stock: 100m);
        await _sut.CreateAsync(Request(product.Id, quantity: 1m));
        await _sut.CreateAsync(Request(product.Id, quantity: 2m));

        var results = await _sut.SearchAsync(new InventoryAdjustmentQuery());

        Assert.Equal(2, results.Count);
        Assert.Equal("ADJ-000002", results[0].AdjustmentNumber);
    }

    [Fact]
    public async Task SearchAsync_FiltersByDirectionReasonAndProduct()
    {
        var a = await SeedProductAsync(name: "Product A", stock: 100m);
        var b = await SeedProductAsync(name: "Product B", stock: 100m);
        await _sut.CreateAsync(Request(a.Id, InventoryAdjustmentDirection.Decrease, 5m, InventoryAdjustmentReason.Damaged));
        await _sut.CreateAsync(Request(b.Id, InventoryAdjustmentDirection.Increase, 3m, InventoryAdjustmentReason.Found));

        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { Direction = InventoryAdjustmentDirection.Increase }));
        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { Reason = InventoryAdjustmentReason.Damaged }));
        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { ProductId = a.Id }));
        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { UserId = _ownerId, ProductId = b.Id }));
    }

    [Fact]
    public async Task SearchAsync_MatchesNumberProductAndNotes()
    {
        var product = await SeedProductAsync(name: "Searchable Product", stock: 100m);
        await _sut.CreateAsync(Request(product.Id, reason: InventoryAdjustmentReason.Other, notes: "warehouse cleanup"));

        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { SearchText = "ADJ-000001" }));
        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { SearchText = "Searchable" }));
        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery { SearchText = "cleanup" }));
        Assert.Empty(await _sut.SearchAsync(new InventoryAdjustmentQuery { SearchText = "nothing matches this" }));
    }

    [Fact]
    public async Task SearchAsync_FiltersByDateRange()
    {
        var product = await SeedProductAsync(stock: 100m);
        await _sut.CreateAsync(Request(product.Id));

        var today = await _sut.SearchAsync(new InventoryAdjustmentQuery
        {
            FromUtc = DateTime.UtcNow.Date,
            ToUtc = DateTime.UtcNow.Date.AddDays(1),
        });
        Assert.Single(today);

        var lastWeek = await _sut.SearchAsync(new InventoryAdjustmentQuery
        {
            FromUtc = DateTime.UtcNow.Date.AddDays(-7),
            ToUtc = DateTime.UtcNow.Date.AddDays(-6),
        });
        Assert.Empty(lastWeek);
    }

    [Fact]
    public async Task GetByIdAsync_LoadsProductAndUser()
    {
        var product = await SeedProductAsync(stock: 100m);
        var created = await _sut.CreateAsync(Request(product.Id));

        var loaded = await _sut.GetByIdAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal(product.Id, loaded!.Product.Id);
        Assert.NotNull(loaded.AdjustedByUser);
    }

    // ---- Barcode-selected products (Phase 13B reuse) ----

    /// <summary>
    /// Adjustments identify the product by id; the UI resolves a scan through the shared
    /// <c>IBarcodeLookupService</c> exactly as POS and stock counting do. These pin that ANY active
    /// barcode reaches the same product, so adjusting after a scan hits the right inventory row —
    /// and that no barcode-specific quantity conversion sneaks in (a scan means "this product",
    /// never "this many").
    /// </summary>
    [Fact]
    public async Task AdjustingAProductReachedByAnyOfItsBarcodes_HitsTheSameInventoryRow()
    {
        var product = await SeedProductAsync(stock: 100m);
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "8901030826501", NormalizedValue = "8901030826501",
            Symbology = Kirana.Domain.Barcodes.BarcodeSymbology.Ean13, IsPrimary = true, IsActive = true,
        });
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "ALT-CODE-1", NormalizedValue = "ALT-CODE-1",
            Symbology = Kirana.Domain.Barcodes.BarcodeSymbology.Code128, IsPrimary = false, IsActive = true,
        });
        await _fixture.Context.SaveChangesAsync();

        var lookup = new Kirana.Application.Barcodes.BarcodeLookupService(_fixture.Context);
        var viaPrimary = await lookup.LookupAsync("8901030826501");
        var viaAlternate = await lookup.LookupAsync("ALT-CODE-1");

        Assert.Equal(product.Id, viaPrimary!.Id);
        Assert.Equal(product.Id, viaAlternate!.Id);

        // One unit per adjustment regardless of which code identified the product.
        await _sut.CreateAsync(Request(viaPrimary.Id, InventoryAdjustmentDirection.Decrease, 1m));
        await _sut.CreateAsync(Request(viaAlternate.Id, InventoryAdjustmentDirection.Decrease, 1m));

        Assert.Equal(98m, await StockOfAsync(product.Id));
        Assert.Equal(2, await _fixture.Context.InventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task GetCurrentStockAsync_ReturnsZero_WhenTheProductHasNoInventoryRow()
    {
        var product = await SeedProductAsync(withInventory: false);

        Assert.Equal(0m, await _sut.GetCurrentStockAsync(product.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
