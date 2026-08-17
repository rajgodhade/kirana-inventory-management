using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Products;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Billing;

/// <summary>
/// Phase 15B-5: a completed sale records the price level it was sold at.
///
/// <para><see cref="Sale.PriceLevel"/> is historical metadata — written once from the same request
/// field the prices were resolved from, and never recomputed. The tests that matter most here are
/// the immutability ones: they move today's prices and the customer's preference underneath a
/// finished sale and require it not to budge.</para>
/// </summary>
public class SalePriceLevelPersistenceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SaleService _sales;
    private readonly ProductPricingService _pricing;
    private readonly CustomerService _customers;
    private readonly SalesReportService _reports;
    private readonly int _ownerId;

    public SalePriceLevelPersistenceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);

        _pricing = new ProductPricingService(_fixture.Context, audit, permissions);
        _customers = new CustomerService(_fixture.Context, sequence, audit);
        _sales = new SaleService(_fixture.Context, sequence, audit, permissions);
        _reports = new SalesReportService(_fixture.Context, permissions);
    }

    private async Task<Product> SeedProductAsync(decimal retail = 100m, decimal? wholesale = 90m, decimal stock = 200m)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Levelled Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 40m,
            Mrp = 300m,
            SellingPrice = retail,
            WholesalePrice = wholesale,
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

    private Task<Sale> SellAsync(Product product, PriceLevel level, decimal pay, int? customerId = null, decimal quantity = 1m) =>
        _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            PriceLevel = level,
            CustomerId = customerId,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = pay, AmountTendered = pay }],
            CashierUserId = _ownerId,
        });

    private async Task<Sale> ReloadAsync(int saleId) =>
        await _fixture.Context.Sales.AsNoTracking().FirstAsync(s => s.Id == saleId);

    // ---- The submitted level becomes the recorded level ----

    [Theory]
    [InlineData("Retail", 100)]
    [InlineData("Wholesale", 90)]
    public async Task TheSubmittedLevel_IsWhatTheSaleRecords(string level, int pay)
    {
        var expected = Enum.Parse<PriceLevel>(level);
        var product = await SeedProductAsync();

        var sale = await SellAsync(product, expected, pay);

        Assert.Equal(expected, (await ReloadAsync(sale.Id)).PriceLevel);
        // ...and the amount charged agrees with the level recorded.
        Assert.Equal(pay, (await _fixture.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.Id)).UnitPriceSnapshot);
    }

    /// <summary>A request that names no level is Retail, so pre-15B-3 callers keep behaving.</summary>
    [Fact]
    public async Task ARequestWithNoLevel_RecordsRetail()
    {
        var product = await SeedProductAsync();

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m }],
            CashierUserId = _ownerId,
        });

        Assert.Equal(PriceLevel.Retail, (await ReloadAsync(sale.Id)).PriceLevel);
    }

    /// <summary>§26: one level per sale. There is no per-line level to disagree with it.</summary>
    [Fact]
    public async Task ASaleHasExactlyOneLevel_AcrossAllItsLines()
    {
        var a = await SeedProductAsync(retail: 100m, wholesale: 90m);
        var b = await SeedProductAsync(retail: 200m, wholesale: 180m);

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            PriceLevel = PriceLevel.Wholesale,
            Lines =
            [
                new SaleLineInput { ProductId = a.Id, Quantity = 1m },
                new SaleLineInput { ProductId = b.Id, Quantity = 1m },
            ],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 270m, AmountTendered = 270m }],
            CashierUserId = _ownerId,
        });

        var reloaded = await ReloadAsync(sale.Id);
        Assert.Equal(PriceLevel.Wholesale, reloaded.PriceLevel);

        // Every line was priced at that one level.
        var snapshots = await _fixture.Context.SaleItems.AsNoTracking()
            .Where(i => i.SaleId == sale.Id).OrderBy(i => i.Id)
            .Select(i => i.UnitPriceSnapshot).ToListAsync();
        Assert.Equal([90m, 180m], snapshots);
    }

    // ---- The customer is not the authority ----

    [Fact]
    public async Task AWholesaleCustomerOnARetailBill_RecordsRetail()
    {
        var customer = await _customers.CreateAsync(new CreateCustomerRequest
        {
            Name = "ABC Wholesale", Phone = "9800000001",
            DefaultPriceLevel = PriceLevel.Wholesale, PerformedByUserId = _ownerId,
        });
        var product = await SeedProductAsync();

        var sale = await SellAsync(product, PriceLevel.Retail, pay: 100m, customerId: customer.Id);

        Assert.Equal(PriceLevel.Retail, (await ReloadAsync(sale.Id)).PriceLevel);
    }

    /// <summary>§23: the customer's preference can change afterwards; the sale cannot.</summary>
    [Fact]
    public async Task ChangingTheCustomersDefault_DoesNotRelabelTheirPastSales()
    {
        var customer = await _customers.CreateAsync(new CreateCustomerRequest
        {
            Name = "ABC Wholesale", Phone = "9800000002",
            DefaultPriceLevel = PriceLevel.Wholesale, PerformedByUserId = _ownerId,
        });
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, PriceLevel.Wholesale, pay: 90m, customerId: customer.Id);

        await _customers.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name, Phone = customer.Phone,
            DefaultPriceLevel = PriceLevel.Retail, PerformedByUserId = _ownerId,
        });

        Assert.Equal(PriceLevel.Wholesale, (await ReloadAsync(sale.Id)).PriceLevel);
        Assert.Equal(90m, (await _fixture.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.Id)).UnitPriceSnapshot);
    }

    // ---- Immutability against moving prices ----

    /// <summary>§25: two wholesale sales either side of a price change keep their own figures AND
    /// their own level.</summary>
    [Fact]
    public async Task TwoWholesaleSales_AcrossAPriceChange_KeepTheirOwnSnapshotsAndLevels()
    {
        var product = await SeedProductAsync(retail: 100m, wholesale: 20m);

        var first = await SellAsync(product, PriceLevel.Wholesale, pay: 20m);
        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 25m, _ownerId);
        var second = await SellAsync(product, PriceLevel.Wholesale, pay: 25m);

        Assert.Equal(PriceLevel.Wholesale, (await ReloadAsync(first.Id)).PriceLevel);
        Assert.Equal(PriceLevel.Wholesale, (await ReloadAsync(second.Id)).PriceLevel);
        Assert.Equal(20m, (await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == first.Id)).UnitPriceSnapshot);
        Assert.Equal(25m, (await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == second.Id)).UnitPriceSnapshot);
    }

    /// <summary>
    /// The level must not be recomputed by comparing the snapshot against current prices. Set up
    /// the trap: after this change, the wholesale sale's ₹90 equals today's RETAIL price, so any
    /// implementation that infers the level from amounts would relabel it Retail.
    /// </summary>
    [Fact]
    public async Task AWholesaleSale_StaysWholesale_EvenWhenItsPriceLaterMatchesRetail()
    {
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);
        var sale = await SellAsync(product, PriceLevel.Wholesale, pay: 90m);

        await _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 90m, _ownerId);

        Assert.Equal(PriceLevel.Wholesale, (await ReloadAsync(sale.Id)).PriceLevel);
    }

    // ---- Reporting ----

    [Fact]
    public async Task TheSummarySplitsGrossSalesByRecordedLevel()
    {
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);
        await SellAsync(product, PriceLevel.Retail, pay: 100m);
        await SellAsync(product, PriceLevel.Retail, pay: 100m);
        await SellAsync(product, PriceLevel.Wholesale, pay: 90m);

        var summary = await _reports.GetSummaryAsync(TodayRange(), null, _ownerId);

        Assert.Equal(200m, summary.RetailSales);
        Assert.Equal(90m, summary.WholesaleSales);
        Assert.Equal(2, summary.RetailBillCount);
        Assert.Equal(1, summary.WholesaleBillCount);
        // The split reconciles with the unsplit figure.
        Assert.Equal(summary.GrossSales, summary.RetailSales + summary.WholesaleSales);
        Assert.Equal(summary.BillCount, summary.RetailBillCount + summary.WholesaleBillCount);
    }

    /// <summary>
    /// The report must read the RECORDED level, not infer one from today's prices.
    ///
    /// <para>Set up so the two answers genuinely differ: a wholesale bill is sold at ₹90, then the
    /// WHOLESALE price moves to ₹95. The sale's ₹90 snapshot now matches neither current level, so
    /// any implementation that classifies by comparing amounts against today's prices loses the
    /// bill (or calls it Retail). Only reading <see cref="Sale.PriceLevel"/> still says Wholesale.</para>
    ///
    /// <para>Added because a fault injection that derived the level from current ProductPrice passed
    /// the plain split test — its data happened to make both implementations agree. The first
    /// version of this test moved the RETAIL price instead, which left the wholesale comparison
    /// untouched and so still failed to separate them.</para>
    /// </summary>
    [Fact]
    public async Task TheSummaryUsesTheRecordedLevel_EvenWhenCurrentPricesWouldSayOtherwise()
    {
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);
        await SellAsync(product, PriceLevel.Wholesale, pay: 90m);

        // The sold-at amount no longer equals any current price, so nothing about today's catalogue
        // can identify this bill's level.
        await _pricing.SetPriceAsync(product.Id, PriceLevel.Wholesale, 95m, _ownerId);

        var summary = await _reports.GetSummaryAsync(TodayRange(), null, _ownerId);

        Assert.Equal(90m, summary.WholesaleSales);
        Assert.Equal(1, summary.WholesaleBillCount);
        Assert.Equal(0m, summary.RetailSales);
        Assert.Equal(0, summary.RetailBillCount);

        // ...and the same holds through the filter.
        var wholesaleOnly = await _reports.GetSummaryAsync(
            TodayRange(), new ReportFilter { PriceLevel = PriceLevel.Wholesale }, _ownerId);
        Assert.Equal(90m, wholesaleOnly.GrossSales);
        Assert.Equal(1, wholesaleOnly.BillCount);
    }

    [Theory]
    [InlineData("Retail", 200, 2)]
    [InlineData("Wholesale", 90, 1)]
    public async Task FilteringByLevel_NarrowsToThatLevelsBills(string level, int expectedGross, int expectedBills)
    {
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);
        await SellAsync(product, PriceLevel.Retail, pay: 100m);
        await SellAsync(product, PriceLevel.Retail, pay: 100m);
        await SellAsync(product, PriceLevel.Wholesale, pay: 90m);

        var summary = await _reports.GetSummaryAsync(
            TodayRange(), new ReportFilter { PriceLevel = Enum.Parse<PriceLevel>(level) }, _ownerId);

        Assert.Equal(expectedGross, summary.GrossSales);
        Assert.Equal(expectedBills, summary.BillCount);
    }

    /// <summary>
    /// Reporting reads; it must not write.
    ///
    /// <para>Counts alone are not enough: a report that rewrote every sale's level in place would
    /// leave the row COUNT untouched and pass. Proven by fault injection — an injected report that
    /// set every sale to Retail and saved slipped past the count-only version of this test. The
    /// sale rows' own contents are therefore fingerprinted and compared.</para>
    /// </summary>
    [Fact]
    public async Task RunningTheReport_MutatesNothing()
    {
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);
        await SellAsync(product, PriceLevel.Wholesale, pay: 90m);
        await SellAsync(product, PriceLevel.Retail, pay: 100m);

        var auditsBefore = await _fixture.Context.AuditLogs.CountAsync();
        var pricesBefore = await _fixture.Context.ProductPrices.AsNoTracking()
            .OrderBy(p => p.Id).Select(p => p.Price).ToListAsync();
        var stockBefore = (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand;
        var salesBefore = await SaleRowsAsync();
        var itemsBefore = await _fixture.Context.SaleItems.AsNoTracking()
            .OrderBy(i => i.Id).Select(i => i.UnitPriceSnapshot).ToListAsync();

        for (var i = 0; i < 3; i++)
        {
            await _reports.GetSummaryAsync(TodayRange(), null, _ownerId);
            await _reports.GetSummaryAsync(TodayRange(), new ReportFilter { PriceLevel = PriceLevel.Wholesale }, _ownerId);
        }

        Assert.Equal(auditsBefore, await _fixture.Context.AuditLogs.CountAsync());
        Assert.Equal(pricesBefore, await _fixture.Context.ProductPrices.AsNoTracking()
            .OrderBy(p => p.Id).Select(p => p.Price).ToListAsync());
        Assert.Equal(stockBefore, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
        Assert.Equal(itemsBefore, await _fixture.Context.SaleItems.AsNoTracking()
            .OrderBy(i => i.Id).Select(i => i.UnitPriceSnapshot).ToListAsync());
        // The sales themselves - level and money - not merely how many there are.
        Assert.Equal(salesBefore, await SaleRowsAsync());
        Assert.False(_fixture.Context.ChangeTracker.HasChanges());
    }

    private async Task<List<string>> SaleRowsAsync() =>
        await _fixture.Context.Sales.AsNoTracking().OrderBy(s => s.Id)
            .Select(s => s.Id + "|" + s.PriceLevel + "|" + s.GrandTotal + "|" + s.Status)
            .ToListAsync();

    /// <summary>Reporting stays behind the existing ReportsView permission — no new one, and no
    /// separate gate for the level split.</summary>
    [Fact]
    public async Task ACashierWithoutReportsView_CannotReadTheLevelSplit()
    {
        var cashierId = (await _fixture.SeedCashierAsync()).Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _reports.GetSummaryAsync(TodayRange(), new ReportFilter { PriceLevel = PriceLevel.Wholesale }, cashierId));
    }

    /// <summary>Same helper shape the existing report tests use, so "today" resolves by local
    /// wall-clock day exactly as the application does.</summary>
    private static ReportDateRange TodayRange() => ReportDateRange.Resolve(ReportDatePreset.Today);

    public void Dispose() => _fixture.Dispose();
}
