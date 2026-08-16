using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Products;

/// <summary>
/// Phase 15A: product create/update now route selling prices through
/// <see cref="IProductPricingService"/>, so the authoritative <see cref="ProductPrice"/> rows and
/// the legacy projection columns are written by one implementation.
///
/// <para>These tests assert BOTH stores after every operation — a passing test here means the two
/// cannot have drifted, which is the whole point of having a single write path.</para>
/// </summary>
public class ProductPricingIntegrationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductService _sut;
    private readonly ProductPricingService _pricing;
    private readonly int _ownerId;

    public ProductPricingIntegrationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        var barcodes = new BarcodeService(_fixture.Context, sequence, audit, permissions);

        _pricing = new ProductPricingService(_fixture.Context, audit, permissions);
        _sut = new ProductService(_fixture.Context, sequence, audit, barcodes, permissions, _pricing);
    }

    private CreateProductRequest Request(
        decimal retail = 100m, decimal? wholesale = null, string name = "Pricing Product") => new()
        {
            Name = name,
            Sku = $"SKU-{Guid.NewGuid():N}"[..10],
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 60m,
            Mrp = 120m,
            SellingPrice = retail,
            WholesalePrice = wholesale,
            PerformedByUserId = _ownerId,
        };

    private Task<List<ProductPrice>> PricesOfAsync(int productId) =>
        _fixture.Context.ProductPrices.Where(p => p.ProductId == productId && p.IsActive).ToListAsync();

    private async Task<decimal?> LevelAsync(int productId, PriceLevel level) =>
        (await PricesOfAsync(productId)).FirstOrDefault(p => p.Level == level)?.Price;

    // ---- §15 Creation ----

    [Fact]
    public async Task Create_WithRetailOnly_StoresRetailRowAndProjection()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));

        var price = Assert.Single(await PricesOfAsync(product.Id));
        Assert.Equal(PriceLevel.Retail, price.Level);
        Assert.Equal(100m, price.Price);
        Assert.Equal(100m, product.SellingPrice);   // projection
        Assert.Null(product.WholesalePrice);
    }

    [Fact]
    public async Task Create_WithRetailAndWholesale_StoresBothRowsAndProjections()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        var prices = await PricesOfAsync(product.Id);
        Assert.Equal(2, prices.Count);
        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(95m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        Assert.Equal(100m, product.SellingPrice);
        Assert.Equal(95m, product.WholesalePrice);
    }

    /// <summary>Null wholesale means the level does not apply — which is not the same as zero, so
    /// no row may be invented for it.</summary>
    [Fact]
    public async Task Create_WithNullWholesale_CreatesNoWholesaleRow()
    {
        var product = await _sut.CreateAsync(Request(wholesale: null));

        Assert.DoesNotContain(await PricesOfAsync(product.Id), p => p.Level == PriceLevel.Wholesale);
        Assert.Null(product.WholesalePrice);
    }

    [Fact]
    public async Task Create_WithZeroWholesale_StoresZeroRatherThanTreatingItAsUnset()
    {
        var product = await _sut.CreateAsync(Request(wholesale: 0m));

        Assert.Equal(0m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        Assert.Equal(0m, product.WholesalePrice);
    }

    [Fact]
    public async Task Create_WithZeroRetail_IsAllowed()
    {
        // The existing catalogue already permits zero-priced products; 15A does not tighten that.
        var product = await _sut.CreateAsync(Request(retail: 0m));

        Assert.Equal(0m, await LevelAsync(product.Id, PriceLevel.Retail));
    }

    [Fact]
    public async Task Create_WithNegativeRetail_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(retail: -5m)));
    }

    /// <summary>The pre-existing bug 15A fixes: a negative wholesale price used to be accepted and
    /// silently persisted.</summary>
    [Fact]
    public async Task Create_WithNegativeWholesale_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(Request(retail: 100m, wholesale: -5m)));
    }

    [Fact]
    public async Task Create_WithNegativeWholesale_LeavesNothingBehind()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(Request(retail: 100m, wholesale: -5m)));

        Assert.Empty(await _fixture.Context.Products.ToListAsync());
        Assert.Empty(await _fixture.Context.ProductPrices.ToListAsync());
        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceChanged"));
    }

    [Fact]
    public async Task Create_PreservesDecimalPrecision()
    {
        var product = await _sut.CreateAsync(Request(retail: 57.50m, wholesale: 49.99m));

        Assert.Equal(57.50m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(49.99m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    // ---- §16 Update ----

    private UpdateProductRequest UpdateRequest(Product product, decimal retail, decimal? wholesale) => new()
    {
        Name = product.Name,
        Sku = product.Sku,
        Unit = product.Unit,
        PurchasePrice = product.PurchasePrice,
        Mrp = product.Mrp,
        SellingPrice = retail,
        WholesalePrice = wholesale,
        PerformedByUserId = _ownerId,
    };

    [Fact]
    public async Task Update_RetailOnly_LeavesWholesaleUntouched()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 105m, wholesale: 95m));

        Assert.Equal(105m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(95m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(105m, reloaded.SellingPrice);
        Assert.Equal(95m, reloaded.WholesalePrice);
    }

    [Fact]
    public async Task Update_WholesaleOnly_LeavesRetailUntouched()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 100m, wholesale: 90m));

        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(90m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    [Fact]
    public async Task Update_BothLevels_PersistsBoth()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 105m, wholesale: 90m));

        Assert.Equal(105m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(90m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(105m, reloaded.SellingPrice);
        Assert.Equal(90m, reloaded.WholesalePrice);
    }

    [Fact]
    public async Task Update_WithNoPriceChange_WritesNoPriceAudit()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 100m, wholesale: 95m));

        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceChanged"));
        Assert.Equal(2, (await PricesOfAsync(product.Id)).Count);   // no duplicate rows
    }

    /// <summary>Clearing wholesale withdraws the level rather than storing zero.</summary>
    [Fact]
    public async Task Update_ClearingWholesale_WithdrawsTheLevel()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        // Asserted BEFORE the clear on purpose. "Null afterwards" is also what you would see if the
        // projection had never been written at all, so without this the test would pass for the
        // wrong reason — a projection bug that skips the wholesale column entirely would slip
        // through. Proving it held 95 first makes the null below mean "cleared", not "never set".
        Assert.Equal(95m, (await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id)).WholesalePrice);

        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 100m, wholesale: null));

        Assert.Null(await LevelAsync(product.Id, PriceLevel.Wholesale));
        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Null(reloaded.WholesalePrice);
        // Retail is untouched, and the product still has exactly one active level.
        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
    }

    [Fact]
    public async Task Update_AddingWholesaleLater_CreatesTheLevel()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: null));

        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 100m, wholesale: 88m));

        Assert.Equal(88m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    [Fact]
    public async Task Update_WithNegativeWholesale_IsRejectedAndChangesNothing()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 100m, wholesale: -1m)));

        Assert.Equal(95m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
    }

    // ---- §9 One active price per level ----

    [Fact]
    public async Task RepeatedSetPrice_UpdatesInPlaceRatherThanAddingASecondActiveRow()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 105m, _ownerId);
        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 110m, _ownerId);

        var retail = Assert.Single(await PricesOfAsync(product.Id), p => p.Level == PriceLevel.Retail);
        Assert.Equal(110m, retail.Price);
    }

    [Fact]
    public async Task RetailAndWholesale_CoexistAsSeparateLevels()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 95m, _ownerId);

        var prices = await PricesOfAsync(product.Id);
        Assert.Equal(2, prices.Count);
        Assert.Single(prices, p => p.Level == PriceLevel.Retail);
        Assert.Single(prices, p => p.Level == PriceLevel.Wholesale);
    }

    // ---- §19/§13 Permissions ----

    [Fact]
    public async Task SetPrice_RequiresProductsEdit()
    {
        var product = await _sut.CreateAsync(Request());
        var cashierId = (await _fixture.SeedCashierAsync()).Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 200m, cashierId));

        // ...and nothing moved.
        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(100m, reloaded.SellingPrice);
    }

    /// <summary>Being called from ProductService must not become an authorization bypass: the
    /// product-level check still gates the whole operation.</summary>
    [Fact]
    public async Task Update_ThroughProductService_StillRequiresProductsEdit()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));
        var cashierId = (await _fixture.SeedCashierAsync()).Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = product.Name,
            Unit = product.Unit,
            PurchasePrice = product.PurchasePrice,
            Mrp = product.Mrp,
            SellingPrice = 999m,
            PerformedByUserId = cashierId,
        }));

        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
    }

    /// <summary>
    /// The pricing service must protect itself, not lean on ProductService happening to check first.
    /// Typed as <see cref="IProductPricingService"/> and called with no ProductService involved at
    /// all, because that is the shape a future caller would take — and if authorization lived only
    /// in ProductService, such a caller would be a silent bypass.
    /// </summary>
    [Theory]
    [InlineData(PriceLevel.Retail)]
    [InlineData(PriceLevel.Wholesale)]
    public async Task PricingServiceCalledDirectly_RefusesPriceChange_ForUnauthorizedUser(PriceLevel level)
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));
        var cashierId = (await _fixture.SeedCashierAsync()).Id;
        IProductPricingService pricing = _pricing;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => pricing.SetPriceAsync(product.Id, level, 1m, cashierId));

        // Both stores untouched, and a refused attempt is not a price change to record.
        Assert.Equal(100m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(95m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(100m, reloaded.SellingPrice);
        Assert.Equal(95m, reloaded.WholesalePrice);
        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceChanged"));
    }

    /// <summary>Removal is the other mutation entry point, so it needs the same gate — withdrawing a
    /// wholesale tier is as much a pricing change as setting one.</summary>
    [Fact]
    public async Task PricingServiceCalledDirectly_RefusesPriceRemoval_ForUnauthorizedUser()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));
        var cashierId = (await _fixture.SeedCashierAsync()).Id;
        IProductPricingService pricing = _pricing;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => pricing.RemovePriceAsync(product.Id, PriceLevel.Wholesale, cashierId));

        Assert.Equal(95m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceRemoved"));
    }

    [Fact]
    public async Task ReadingAPrice_WritesNoAudit()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));
        var auditsBefore = await _fixture.Context.AuditLogs.CountAsync();

        await _pricing.GetPriceAsync(product.Id, PriceLevel.Retail);
        await _pricing.GetPricesAsync(product.Id);

        Assert.Equal(auditsBefore, await _fixture.Context.AuditLogs.CountAsync());
    }

    // ---- §17 Audit ----

    [Fact]
    public async Task SetPrice_AuditsExactlyOneChangePerLevel()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 105m, _ownerId);
        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 90m, _ownerId);

        var audits = await _fixture.Context.AuditLogs
            .Where(a => a.Action == "PriceChanged").OrderBy(a => a.Id).ToListAsync();

        Assert.Equal(2, audits.Count);
        Assert.Equal("100.00", audits[0].PreviousValue);
        Assert.Equal("105.00", audits[0].NewValue);
        Assert.Contains("Retail", audits[0].Reason!);
        Assert.Equal("95.00", audits[1].PreviousValue);
        Assert.Equal("90.00", audits[1].NewValue);
        Assert.Contains("Wholesale", audits[1].Reason!);
        Assert.All(audits, a => Assert.Equal(_ownerId, a.UserId));
    }

    [Fact]
    public async Task SetPrice_ToTheSameValue_WritesNoAudit()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 100m, _ownerId);

        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceChanged"));
    }

    // ---- §20 Historical protection ----

    /// <summary>
    /// The rule that matters most: a sale keeps what it charged. Changing today's retail price must
    /// never reach back into a completed transaction.
    /// </summary>
    [Fact]
    public async Task ChangingRetailPrice_DoesNotAlterHistoricalSaleItems()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));
        _fixture.Context.Inventories.Add(new Inventory { ProductId = product.Id, QuantityOnHand = 50m });
        await _fixture.Context.SaveChangesAsync();

        var sales = new SaleService(
            _fixture.Context, new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));

        var sale = await sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 2m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 200m, AmountTendered = 200m }],
            CashierUserId = _ownerId,
        });

        var soldAt = (await _fixture.Context.SaleItems.FirstAsync(i => i.SaleId == sale.Id)).UnitPriceSnapshot;
        Assert.Equal(100m, soldAt);

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 110m, _ownerId);

        var afterChange = await _fixture.Context.SaleItems.FirstAsync(i => i.SaleId == sale.Id);
        Assert.Equal(100m, afterChange.UnitPriceSnapshot);   // unchanged
        Assert.Equal(110m, await LevelAsync(product.Id, PriceLevel.Retail));
    }

    /// <summary>§15: selling price and purchase cost are separate concepts.</summary>
    [Fact]
    public async Task ChangingSellingPrice_DoesNotAlterPurchaseCost()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));
        var costBefore = product.PurchasePrice;

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 150m, _ownerId);

        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(costBefore, reloaded.PurchasePrice);
    }

    // ---- §6/§18 Atomicity ----

    /// <summary>
    /// The invariant §6 calls critical: ProductPrice and the projection column can never disagree.
    /// They are written in one SaveChanges, so a failed audit rolls the price back with them rather
    /// than leaving stock priced at one figure in one store and another elsewhere.
    /// </summary>
    [Fact]
    public async Task SetPrice_RollsBackThePriceWhenTheAuditWriteFails()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m));

        var failing = new ProductPricingService(
            _fixture.Context, new ThrowingAuditLogger(), new PermissionEnforcer(_fixture.Context));

        await Assert.ThrowsAnyAsync<Exception>(
            () => failing.SetPriceAsync(product.Id, PriceLevel.Retail, 105m, _ownerId));

        // Read through a fresh context: the failed attempt leaves stale tracked entities behind,
        // and the question is what actually reached the database.
        using var verify = new KiranaDbContext(
            new DbContextOptionsBuilder<KiranaDbContext>()
                .UseSqlite(_fixture.Context.Database.GetDbConnection()).Options);

        var storedPrice = await verify.ProductPrices
            .Where(p => p.ProductId == product.Id && p.Level == PriceLevel.Retail && p.IsActive)
            .Select(p => p.Price).FirstAsync();
        var projection = await verify.Products
            .Where(p => p.Id == product.Id).Select(p => p.SellingPrice).FirstAsync();

        Assert.Equal(100m, storedPrice);
        Assert.Equal(100m, projection);
        Assert.Equal(storedPrice, projection);   // the two stores agree
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == "PriceChanged"));
    }

    /// <summary>Whatever happens, the two stores must never disagree — asserted directly across a
    /// normal sequence of price operations.</summary>
    [Fact]
    public async Task ProductPriceAndProjection_StayInStepAcrossManyOperations()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 95m));

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 105m, _ownerId);
        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 90m, _ownerId);
        await _sut.UpdateAsync(product.Id, UpdateRequest(product, retail: 112.25m, wholesale: 99.99m));
        await _pricing.RemovePriceAsync(product.Id, PriceLevel.Wholesale, _ownerId);

        var reloaded = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        Assert.Equal(await LevelAsync(product.Id, PriceLevel.Retail), reloaded.SellingPrice);
        Assert.Equal(await LevelAsync(product.Id, PriceLevel.Wholesale), reloaded.WholesalePrice);
        Assert.Equal(112.25m, reloaded.SellingPrice);
        Assert.Null(reloaded.WholesalePrice);
    }

    private sealed class ThrowingAuditLogger : Kirana.Application.Abstractions.IAuditLogger
    {
        public Task RecordAsync(
            int? userId, string action, string entityName, string? entityId = null,
            string? previousValue = null, string? newValue = null, string? reason = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure: audit write refused.");
    }

    // ---- §31 POS regression ----

    /// <summary>
    /// 15A stores a wholesale price but must NOT let it reach the till. POS still charges retail —
    /// price-level resolution is Phase 15B.
    /// </summary>
    [Fact]
    public async Task Pos_ChargesRetail_EvenWhenWholesaleIsConfiguredAndLower()
    {
        var product = await _sut.CreateAsync(Request(retail: 100m, wholesale: 80m));
        _fixture.Context.Inventories.Add(new Inventory { ProductId = product.Id, QuantityOnHand = 50m });
        await _fixture.Context.SaveChangesAsync();

        var sales = new SaleService(
            _fixture.Context, new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));

        var sale = await sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m }],
            CashierUserId = _ownerId,
        });

        var item = await _fixture.Context.SaleItems.FirstAsync(i => i.SaleId == sale.Id);
        Assert.Equal(100m, item.UnitPriceSnapshot);
        Assert.NotEqual(80m, item.UnitPriceSnapshot);
    }

    public void Dispose() => _fixture.Dispose();
}
