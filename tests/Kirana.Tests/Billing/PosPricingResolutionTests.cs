using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Products;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Billing;

/// <summary>
/// Phase 15B-2: the till prices through <see cref="IProductPriceResolver"/> instead of reading
/// <c>Product.SellingPrice</c>.
///
/// <para>Behaviour is supposed to be identical to before, which makes this migration hard to test —
/// a sale priced from the projection column looks exactly like one priced from ProductPrice. The
/// tests that actually distinguish them are the divergence ones: they desynchronise the projection
/// on purpose, so only a till reading the authoritative store gets the right answer.</para>
/// </summary>
public class PosPricingResolutionTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SaleService _sut;
    private readonly ProductPricingService _pricing;
    private readonly int _ownerId;

    public PosPricingResolutionTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);

        _pricing = new ProductPricingService(_fixture.Context, audit, permissions);
        _sut = new SaleService(_fixture.Context, sequence, audit, permissions);
    }

    private async Task<Product> SeedAsync(
        decimal retail = 100m, decimal? wholesale = null, decimal stock = 50m,
        string name = "Priced Product", decimal? packSize = null)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 40m,
            Mrp = 200m,
            SellingPrice = retail,
            WholesalePrice = wholesale,
            PurchasePackUnit = packSize is null ? null : UnitOfMeasure.Box,
            PurchasePackSize = packSize,
            IsActive = true,
        }.WithRetailPrice();

        if (wholesale is { } w)
        {
            product.WithWholesalePrice(w);
        }

        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<Sale> SellAsync(Product product, decimal quantity = 1m, decimal? override_ = null, decimal? pay = null)
    {
        var amount = pay ?? quantity * (override_ ?? product.SellingPrice);
        return _sut.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity, UnitPriceOverride = override_ }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = amount, AmountTendered = amount }],
            CashierUserId = _ownerId,
        });
    }

    private async Task<decimal> SoldAtAsync(int saleId) =>
        (await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == saleId)).UnitPriceSnapshot;

    /// <summary>Desynchronises the projection column behind the pricing service's back — the one
    /// state the service exists to prevent, and the only way to see which store the till reads.</summary>
    private async Task DesyncProjectionAsync(int productId, decimal selling)
    {
        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == productId);
        tracked.SellingPrice = selling;
        await _fixture.Context.SaveChangesAsync();
    }

    // ---- §14 The test that proves the migration happened ----

    [Fact]
    public async Task SellsAtTheResolvedRetailPrice_NotTheSellingPriceProjection()
    {
        var product = await SeedAsync(retail: 100m);
        await DesyncProjectionAsync(product.Id, selling: 95m);

        var sale = await SellAsync(product, quantity: 1m, pay: 100m);

        Assert.Equal(100m, await SoldAtAsync(sale.Id));
        Assert.Equal(100m, sale.SubTotal);
        // ...and the divergence really was present, so the assertion above means something.
        Assert.Equal(95m, (await _fixture.Context.Products.AsNoTracking()
            .FirstAsync(p => p.Id == product.Id)).SellingPrice);
    }

    // ---- §15 Wholesale must stay out of normal billing ----

    [Fact]
    public async Task ChargesRetail_EvenWhenWholesaleIsConfiguredAndCheaper()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        var sale = await SellAsync(product, quantity: 1m, pay: 100m);

        Assert.Equal(100m, await SoldAtAsync(sale.Id));
        Assert.NotEqual(90m, await SoldAtAsync(sale.Id));
    }

    // ---- §16 Missing / inactive retail blocks the sale, with nothing left behind ----

    [Fact]
    public async Task RefusesToSell_WhenTheRetailPriceHasBeenWithdrawn()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        // Withdraw retail directly — RemovePriceAsync refuses to, precisely because a product
        // without a shelf price should not exist. This models the corrupt/edge state anyway.
        var retailRow = await _fixture.Context.ProductPrices
            .FirstAsync(p => p.ProductId == product.Id && p.Level == PriceLevel.Retail);
        retailRow.IsActive = false;
        await _fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SellAsync(product, pay: 100m));

        Assert.Contains("retail price", error.Message, StringComparison.OrdinalIgnoreCase);
        // No silent substitution of the projection column, wholesale, MRP or zero.
        Assert.DoesNotContain("90", error.Message);
    }

    /// <summary>
    /// The failure must be a PRICING failure, not merely "something went wrong". Asserting only that
    /// an InvalidOperationException escaped is not enough: a till that priced the line at 0 and then
    /// tripped the payment-vs-total check would throw the same exception type and leave the same
    /// empty database, so this test passed under an injected "carry on with an unresolved price"
    /// bug until the message assertion below was added.
    /// </summary>
    [Fact]
    public async Task AFailedPriceResolution_LeavesNoSaleNoStockMovementAndNoAudit()
    {
        var product = await SeedAsync(retail: 100m, stock: 50m);
        var auditsBefore = await _fixture.Context.AuditLogs.CountAsync();

        var retailRow = await _fixture.Context.ProductPrices
            .FirstAsync(p => p.ProductId == product.Id && p.Level == PriceLevel.Retail);
        retailRow.IsActive = false;
        await _fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SellAsync(product, pay: 100m));
        Assert.Contains("retail price", error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
        Assert.Equal(0, await _fixture.Context.SaleItems.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
        Assert.Equal(auditsBefore, await _fixture.Context.AuditLogs.CountAsync());
        // Stock untouched.
        Assert.Equal(50m, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
    }

    // ---- §11/§22.6 The resolved price is what gets snapshotted ----

    /// <summary>
    /// History must survive BOTH a price change and the next sale. The second half matters more
    /// than it looks: a bug that rewrote old snapshots would most plausibly do it while completing
    /// a later sale, and a test that only changes the price and re-reads would never execute that
    /// code path. Proven by fault injection — an injected "rewrite history on checkout" bug passed
    /// this test until the subsequent sale below was added.
    /// </summary>
    [Fact]
    public async Task TheResolvedPriceReachesTheSaleItemSnapshot_AndSurvivesLaterPriceChanges()
    {
        var product = await SeedAsync(retail: 100m, stock: 50m);

        var sale = await SellAsync(product, quantity: 2m, pay: 200m);
        Assert.Equal(100m, await SoldAtAsync(sale.Id));

        // Change today's price through the authoritative path.
        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 130m, _ownerId);

        Assert.Equal(100m, await SoldAtAsync(sale.Id));                       // history is immutable
        Assert.Equal(130m, (await _fixture.Context.Products.AsNoTracking()
            .FirstAsync(p => p.Id == product.Id)).SellingPrice);              // ...but today moved

        // ...and completing another sale at the new price still must not touch the old one.
        await SellAsync(product, quantity: 1m, pay: 130m);

        Assert.Equal(100m, await SoldAtAsync(sale.Id));
    }

    /// <summary>A later sale picks up the new price — proving the resolver is consulted per sale
    /// rather than a value being cached from startup.</summary>
    [Fact]
    public async Task ASubsequentSale_UsesTheUpdatedRetailPrice()
    {
        var product = await SeedAsync(retail: 100m, stock: 50m);
        var first = await SellAsync(product, pay: 100m);

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 130m, _ownerId);
        var second = await SellAsync(product, pay: 130m);

        Assert.Equal(100m, await SoldAtAsync(first.Id));
        Assert.Equal(130m, (await _fixture.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == second.Id)).UnitPriceSnapshot);
    }

    // ---- §7 Quantity does not change the unit price ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task QuantityDoesNotAlterTheUnitPrice(int quantity)
    {
        var product = await SeedAsync(retail: 22m, stock: 100m);

        var sale = await SellAsync(product, quantity: quantity, pay: 22m * quantity);

        Assert.Equal(22m, await SoldAtAsync(sale.Id));
        Assert.Equal(22m * quantity, sale.SubTotal);
    }

    // ---- §10 Price override still works, and is still authorized against the RESOLVED price ----

    /// <summary>
    /// The override check compares the submitted price against the real one. Now that the real one
    /// comes from the resolver, an override equal to the RESOLVED retail price must still count as
    /// "no override" — even when the stale projection column says something else. Otherwise a
    /// diverged projection would start demanding manager PINs for ordinary sales.
    /// </summary>
    [Fact]
    public async Task AnOverrideEqualToResolvedRetail_NeedsNoAuthorization_EvenIfTheProjectionDiverges()
    {
        var product = await SeedAsync(retail: 100m);
        await DesyncProjectionAsync(product.Id, selling: 95m);

        var sale = await SellAsync(product, quantity: 1m, override_: 100m, pay: 100m);

        Assert.Equal(100m, await SoldAtAsync(sale.Id));
    }

    /// <summary>Unchanged contract: an unauthorized override throws InvalidOperationException,
    /// exactly as it did before the till started resolving prices.</summary>
    [Fact]
    public async Task AnUnauthorizedOverride_IsStillRejected()
    {
        var product = await SeedAsync(retail: 100m);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellAsync(product, quantity: 1m, override_: 10m, pay: 10m));

        Assert.Contains("authorization", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
    }

    // ---- §19 Pack configuration must not reach POS pricing ----

    [Fact]
    public async Task APurchasePackConfiguration_DoesNotAffectTheSellingPrice()
    {
        var product = await SeedAsync(retail: 22m, packSize: 24m);

        var sale = await SellAsync(product, quantity: 1m, pay: 22m);

        Assert.Equal(22m, await SoldAtAsync(sale.Id));   // per base unit, not per pack
    }

    // ---- §18 Every active barcode prices identically ----

    [Fact]
    public async Task EveryActiveBarcodeResolvesToTheSameRetailPrice()
    {
        var product = await SeedAsync(retail: 22m, stock: 100m);
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "PRIMARY-1", NormalizedValue = "PRIMARY-1",
            Symbology = BarcodeSymbology.Code128, IsPrimary = true, IsActive = true,
        });
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "ALTERNATE-1", NormalizedValue = "ALTERNATE-1",
            Symbology = BarcodeSymbology.Code128, IsPrimary = false, IsActive = true,
        });
        await _fixture.Context.SaveChangesAsync();

        var lookup = new Kirana.Application.Barcodes.BarcodeLookupService(_fixture.Context);

        foreach (var code in new[] { "PRIMARY-1", "ALTERNATE-1" })
        {
            var found = await lookup.LookupAsync(code);
            Assert.NotNull(found);
            Assert.Equal(product.Id, found!.Id);

            var sale = await SellAsync(found, quantity: 1m, pay: 22m);
            Assert.Equal(22m, (await _fixture.Context.SaleItems.AsNoTracking()
                .FirstAsync(i => i.SaleId == sale.Id)).UnitPriceSnapshot);
        }
    }

    // ---- §20 Stock behaviour unchanged ----

    [Fact]
    public async Task ASuccessfulSale_StillDeductsStockExactlyOnce()
    {
        var product = await SeedAsync(retail: 100m, stock: 50m);

        await SellAsync(product, quantity: 3m, pay: 300m);

        Assert.Equal(47m, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
        Assert.Equal(1, await _fixture.Context.StockMovements.CountAsync(m => m.ProductId == product.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
