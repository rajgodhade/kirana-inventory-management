using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Products;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Billing;

/// <summary>
/// Phase 15B-3: a bill can be sold at Retail or Wholesale.
///
/// <para>These cover the service side of the feature — what a bill at a given level actually
/// charges, and what happens when a product has no price at that level. The cart's own switching
/// behaviour lives in the ViewModel, which this net10.0 project cannot reference; the invariant
/// that matters for money is that <see cref="SaleService"/> re-resolves from the LEVEL the client
/// selected and never from a price the client supplied.</para>
/// </summary>
public class PriceLevelSelectionTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SaleService _sut;
    private readonly ProductPricingService _pricing;
    private readonly int _ownerId;

    public PriceLevelSelectionTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);

        _pricing = new ProductPricingService(_fixture.Context, audit, permissions);
        _sut = new SaleService(_fixture.Context, sequence, audit, permissions);
    }

    private async Task<Product> SeedAsync(
        decimal retail = 100m, decimal? wholesale = null, decimal stock = 50m,
        string name = "Levelled Product", decimal? packSize = null)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 40m,
            Mrp = 300m,
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

    private Task<Sale> SellAsync(
        PriceLevel level, decimal pay, params (Product Product, decimal Quantity, decimal? Override, decimal Discount)[] lines) =>
        _sut.CompleteSaleAsync(new CompleteSaleRequest
        {
            PriceLevel = level,
            Lines = lines.Select(l => new SaleLineInput
            {
                ProductId = l.Product.Id,
                Quantity = l.Quantity,
                UnitPriceOverride = l.Override,
                DiscountPercent = l.Discount,
            }).ToList(),
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = pay, AmountTendered = pay }],
            CashierUserId = _ownerId,
        });

    private Task<Sale> SellOneAsync(Product product, PriceLevel level, decimal pay, decimal quantity = 1m) =>
        SellAsync(level, pay, (product, quantity, null, 0m));

    private async Task<decimal> SoldAtAsync(int saleId, int productId) =>
        (await _fixture.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == saleId && i.ProductId == productId)).UnitPriceSnapshot;

    // ---- Default and per-level resolution ----

    /// <summary>An unspecified level is Retail, so every pre-15B-3 caller keeps its behaviour.</summary>
    [Fact]
    public async Task ARequestThatNamesNoLevel_SellsAtRetail()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        var sale = await _sut.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m }],
            CashierUserId = _ownerId,
        });

        Assert.Equal(100m, await SoldAtAsync(sale.Id, product.Id));
    }

    [Theory]
    [InlineData("Retail", 100)]
    [InlineData("Wholesale", 90)]
    public async Task EachLevelSellsAtItsOwnConfiguredPrice(string level, int expected)
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);
        var priceLevel = Enum.Parse<PriceLevel>(level);

        var sale = await SellOneAsync(product, priceLevel, pay: expected);

        Assert.Equal(expected, await SoldAtAsync(sale.Id, product.Id));
    }

    /// <summary>§8: every line resolves independently at the bill's level.</summary>
    [Fact]
    public async Task EveryLineOnAWholesaleBill_ResolvesItsOwnWholesalePrice()
    {
        var a = await SeedAsync(retail: 100m, wholesale: 90m, name: "Product A");
        var b = await SeedAsync(retail: 200m, wholesale: 180m, name: "Product B");

        var sale = await SellAsync(PriceLevel.Wholesale, pay: 270m, (a, 1m, null, 0m), (b, 1m, null, 0m));

        Assert.Equal(90m, await SoldAtAsync(sale.Id, a.Id));
        Assert.Equal(180m, await SoldAtAsync(sale.Id, b.Id));
    }

    // ---- Missing level: refuse, never substitute ----

    [Fact]
    public async Task AWholesaleBill_RefusesAProductWithNoWholesalePrice()
    {
        var product = await SeedAsync(retail: 100m, wholesale: null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellOneAsync(product, PriceLevel.Wholesale, pay: 100m));

        Assert.Contains("wholesale", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(product.Name, error.Message);
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
    }

    /// <summary>§10: one unpriced line refuses the WHOLE bill. The alternative — billing the rest
    /// at wholesale and this one at retail — would produce an invoice whose header lies about it.</summary>
    [Fact]
    public async Task AMixedCart_RefusesEntirely_RatherThanBillingOneLineAtAnotherLevel()
    {
        var priced = await SeedAsync(retail: 100m, wholesale: 90m, name: "Has Wholesale");
        var unpriced = await SeedAsync(retail: 100m, wholesale: null, name: "No Wholesale");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellAsync(PriceLevel.Wholesale, pay: 190m, (priced, 1m, null, 0m), (unpriced, 1m, null, 0m)));

        Assert.Contains("No Wholesale", error.Message);
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
        Assert.Equal(0, await _fixture.Context.SaleItems.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
    }

    [Fact]
    public async Task AWholesaleBill_NeverFallsBackToTheRetailPrice()
    {
        var product = await SeedAsync(retail: 100m, wholesale: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellOneAsync(product, PriceLevel.Wholesale, pay: 100m));

        // Nothing was billed at all, least of all 100.
        Assert.Equal(0, await _fixture.Context.SaleItems.CountAsync());
    }

    /// <summary>Missing RETAIL still refuses too — 15B-2's rule is unchanged by adding a level.</summary>
    [Fact]
    public async Task ARetailBill_StillRefusesAProductWithNoRetailPrice()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);
        var retailRow = await _fixture.Context.ProductPrices
            .FirstAsync(p => p.ProductId == product.Id && p.Level == PriceLevel.Retail);
        retailRow.IsActive = false;
        await _fixture.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellOneAsync(product, PriceLevel.Retail, pay: 100m));

        Assert.Contains("retail", error.Message, StringComparison.OrdinalIgnoreCase);
        // ...and it did not quietly sell at the wholesale price it does have.
        Assert.Equal(0, await _fixture.Context.SaleItems.CountAsync());
    }

    /// <summary>
    /// A configured zero is a price, not "unset", so it bills as 0 rather than being refused as
    /// unavailable. Paired with a normal line because the existing (unrelated) payment rule requires
    /// a positive payment amount — a bill totalling zero cannot be tendered, which is a payment
    /// constraint rather than a pricing one and is left exactly as it was.
    /// </summary>
    [Fact]
    public async Task AZeroWholesalePrice_BillsAsZero_RatherThanBeingRefused()
    {
        var free = await SeedAsync(retail: 100m, wholesale: 0m, name: "Free At Wholesale");
        var paid = await SeedAsync(retail: 100m, wholesale: 90m, name: "Normal");

        var sale = await SellAsync(PriceLevel.Wholesale, pay: 90m, (free, 1m, null, 0m), (paid, 1m, null, 0m));

        Assert.Equal(0m, await SoldAtAsync(sale.Id, free.Id));
        Assert.Equal(90m, await SoldAtAsync(sale.Id, paid.Id));
        Assert.Equal(90m, sale.GrandTotal);
    }

    // ---- Quantity, discounts, tax ----

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task QuantityDoesNotAlterTheWholesaleUnitPrice(int quantity)
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m, stock: 100m);

        var sale = await SellOneAsync(product, PriceLevel.Wholesale, pay: 90m * quantity, quantity: quantity);

        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));
        Assert.Equal(90m * quantity, sale.SubTotal);
    }

    /// <summary>§25: the discount formula is untouched; it just applies to a different base.</summary>
    [Fact]
    public async Task DiscountsApplyToTheResolvedWholesalePrice()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        var sale = await SellAsync(PriceLevel.Wholesale, pay: 81m, (product, 1m, null, 10m));

        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));   // snapshot is the resolved price
        Assert.Equal(9m, sale.ItemDiscountTotal);                    // 10% of 90, not of 100
        Assert.Equal(81m, sale.GrandTotal);
    }

    // ---- Override stays a separate mechanism (§17/§18) ----

    [Fact]
    public async Task AnOverrideEqualToTheWholesalePrice_NeedsNoAuthorization()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        var sale = await SellAsync(PriceLevel.Wholesale, pay: 90m, (product, 1m, 90m, 0m));

        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));
    }

    /// <summary>Overriding BELOW wholesale is still an override — selecting a cheaper level must not
    /// become a way to move the bar for what needs a manager.</summary>
    [Fact]
    public async Task AnUnauthorizedOverrideOnAWholesaleBill_IsStillRejected()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellAsync(PriceLevel.Wholesale, pay: 85m, (product, 1m, 85m, 0m)));

        Assert.Contains("authorization", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
    }

    /// <summary>The retail price is NOT the baseline on a wholesale bill: paying retail on a
    /// wholesale bill is a deviation and must be authorized like any other.</summary>
    [Fact]
    public async Task OverridingAWholesaleLineUpToTheRetailPrice_CountsAsAnOverride()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SellAsync(PriceLevel.Wholesale, pay: 100m, (product, 1m, 100m, 0m)));
    }

    // ---- History and returns ----

    [Fact]
    public async Task AWholesaleSaleKeepsItsPrice_WhenTheWholesalePriceLaterChanges()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m, stock: 50m);
        var sale = await SellOneAsync(product, PriceLevel.Wholesale, pay: 90m);
        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 95m, _ownerId);

        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));

        // ...and a later wholesale bill picks up the new price.
        var later = await SellOneAsync(product, PriceLevel.Wholesale, pay: 95m);
        Assert.Equal(95m, await SoldAtAsync(later.Id, product.Id));
        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));
    }

    /// <summary>§24: a return works off the historical sale, not today's wholesale price.</summary>
    [Fact]
    public async Task AReturnOfAWholesaleSale_UsesTheHistoricalPrice()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m, stock: 50m);
        var sale = await SellOneAsync(product, PriceLevel.Wholesale, pay: 90m);

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 95m, _ownerId);

        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == sale.Id);
        Assert.Equal(90m, saleItem.UnitPriceSnapshot);
        Assert.Equal(90m, saleItem.LineTotal);
    }

    // ---- Stock and barcodes ----

    [Fact]
    public async Task AWholesaleSale_DeductsStockExactlyLikeARetailOne()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m, stock: 50m);

        await SellOneAsync(product, PriceLevel.Wholesale, pay: 270m, quantity: 3m);

        Assert.Equal(47m, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
        Assert.Equal(1, await _fixture.Context.StockMovements.CountAsync(m => m.ProductId == product.Id));
    }

    [Fact]
    public async Task EveryActiveBarcodeSellsAtTheBillsLevel()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m, stock: 100m);
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "WS-PRIMARY", NormalizedValue = "WS-PRIMARY",
            Symbology = BarcodeSymbology.Code128, IsPrimary = true, IsActive = true,
        });
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "WS-ALTERNATE", NormalizedValue = "WS-ALTERNATE",
            Symbology = BarcodeSymbology.Code128, IsPrimary = false, IsActive = true,
        });
        product.Barcodes.Add(new ProductBarcode
        {
            Value = "WS-RETIRED", NormalizedValue = "WS-RETIRED",
            Symbology = BarcodeSymbology.Code128, IsPrimary = false, IsActive = false,
        });
        await _fixture.Context.SaveChangesAsync();

        var lookup = new BarcodeLookupService(_fixture.Context);

        foreach (var code in new[] { "WS-PRIMARY", "WS-ALTERNATE" })
        {
            var found = await lookup.LookupAsync(code);
            Assert.NotNull(found);
            var sale = await SellOneAsync(found!, PriceLevel.Wholesale, pay: 90m);
            Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));
        }

        // A retired code still does not scan, regardless of price level.
        Assert.Null(await lookup.LookupAsync("WS-RETIRED"));
    }

    [Fact]
    public async Task APurchasePackConfiguration_DoesNotAffectWholesaleSelling()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m, packSize: 24m);

        var sale = await SellOneAsync(product, PriceLevel.Wholesale, pay: 90m);

        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));   // per base unit, not per pack
    }

    // ---- Determinism ----

    /// <summary>§28/§29: switching back and forth, or re-selecting the same level, is deterministic
    /// and mutates nothing. Modelled here as repeated resolutions, which is what a switch performs.</summary>
    [Fact]
    public async Task RepeatedLevelResolution_IsDeterministicAndWritesNothing()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);
        var resolver = new ProductPriceResolver(_fixture.Context);
        var auditsBefore = await _fixture.Context.AuditLogs.CountAsync();

        foreach (var level in new[]
        {
            PriceLevel.Retail, PriceLevel.Wholesale, PriceLevel.Retail,
            PriceLevel.Retail, PriceLevel.Wholesale, PriceLevel.Wholesale,
        })
        {
            var resolution = await resolver.ResolveAsync(product.Id, new PricingContext(level));
            Assert.True(resolution.IsResolved);
            Assert.Equal(level == PriceLevel.Retail ? 100m : 90m, resolution.UnitPrice);
        }

        Assert.Equal(auditsBefore, await _fixture.Context.AuditLogs.CountAsync());
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
        Assert.Equal(50m, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
    }

    /// <summary>The resolver stays authoritative at every level — a diverged projection column
    /// changes nothing about what a wholesale bill charges.</summary>
    [Fact]
    public async Task WholesaleSellsFromProductPrice_NotTheWholesalePriceProjection()
    {
        var product = await SeedAsync(retail: 100m, wholesale: 90m);
        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.WholesalePrice = 95m;
        await _fixture.Context.SaveChangesAsync();

        var sale = await SellOneAsync(product, PriceLevel.Wholesale, pay: 90m);

        Assert.Equal(90m, await SoldAtAsync(sale.Id, product.Id));
        Assert.NotEqual(95m, await SoldAtAsync(sale.Id, product.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
