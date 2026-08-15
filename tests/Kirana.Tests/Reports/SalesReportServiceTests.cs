using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>Sales report accuracy and GST calculations (PRD §51). GST figures are checked against
/// the GstRatePercentSnapshot/TaxableAmount/GstAmount actually stored on each SaleItem, and are
/// shown NOT to move when the product's live GST rate or price is changed afterwards.</summary>
public class SalesReportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SalesReportService _sut;
    private readonly SaleService _saleService;
    private readonly int _ownerId;

    public SalesReportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _sut = new SalesReportService(_fixture.Context, enforcer);
        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);

        // SeedOwnerAsync already created the single Store row via first-time setup; enable GST on
        // it rather than inserting a second row (Stores has no unique constraint, and SaleService
        // reads it with a bare FirstOrDefaultAsync, so a duplicate row would make results depend on
        // row order).
        var store = _fixture.Context.Stores.Single();
        store.IsGstEnabled = true;
        _fixture.Context.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private async Task<Product> SeedProductAsync(
        decimal sellingPrice = 100, decimal? gstRate = 12, bool isTaxInclusive = false, int? categoryId = null)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "GST Test Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = sellingPrice * 0.6m,
            Mrp = sellingPrice + 10,
            SellingPrice = sellingPrice,
            GstRatePercent = gstRate,
            IsTaxInclusive = isTaxInclusive,
            CategoryId = categoryId,
            IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 1000 });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<Sale> SellAsync(Product product, decimal quantity, PaymentMethod method = PaymentMethod.Cash, decimal? discountPercent = null)
    {
        // A GST-exclusive line's grand total is qty*price, less any discount, plus tax on top —
        // tender enough to cover exactly that so SaleService's payment-matches-total check passes.
        var afterDiscount = quantity * product.SellingPrice * (1m - (discountPercent ?? 0) / 100m);
        var amount = afterDiscount * (product.IsTaxInclusive ? 1m : 1m + (product.GstRatePercent ?? 0) / 100m);
        return _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity, DiscountPercent = discountPercent ?? 0 }],
            Payments = [new SalePaymentInput { Method = method, Amount = Math.Round(amount, 0), AmountTendered = method == PaymentMethod.Cash ? Math.Round(amount, 0) : null }],
            CashierUserId = _ownerId,
        });
    }

    private static ReportDateRange TodayRange() => ReportDateRange.Resolve(ReportDatePreset.Today);

    [Fact]
    public async Task GrossSales_ReconcilesWithSaleGrandTotals()
    {
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 0);
        await SellAsync(product, 3);
        await SellAsync(product, 2);

        var summary = await _sut.GetSummaryAsync(TodayRange(), filter: null, _ownerId);

        var expected = await _fixture.Context.Sales.AsNoTracking().SumAsync(s => s.GrandTotal);
        Assert.Equal(expected, summary.GrossSales);
        Assert.Equal(2, summary.BillCount);
        Assert.Equal(5m, summary.ItemsSold);
        Assert.Equal(Math.Round(expected / 2, 2), summary.AverageBillValue);
    }

    [Fact]
    public async Task ItemDiscounts_AreSummedAcrossLines()
    {
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 0);
        await SellAsync(product, 10, discountPercent: 10); // 100 discount

        var summary = await _sut.GetSummaryAsync(TodayRange(), filter: null, _ownerId);

        Assert.Equal(100m, summary.ItemDiscounts);
    }

    [Fact]
    public async Task PaymentMethodBreakdown_SumsEachMethodIndependently()
    {
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 0);
        await SellAsync(product, 2, PaymentMethod.Cash);
        await SellAsync(product, 1, PaymentMethod.Upi);

        var summary = await _sut.GetSummaryAsync(TodayRange(), filter: null, _ownerId);

        Assert.Equal(200m, summary.PaymentMethodBreakdown.Single(p => p.Method == "Cash").Amount);
        Assert.Equal(100m, summary.PaymentMethodBreakdown.Single(p => p.Method == "UPI").Amount);
    }

    [Fact]
    public async Task Filter_ByProduct_NarrowsTheSummary()
    {
        var target = await SeedProductAsync(sellingPrice: 100, gstRate: 0);
        var other = await SeedProductAsync(sellingPrice: 50, gstRate: 0);
        await SellAsync(target, 2);
        await SellAsync(other, 3);

        var summary = await _sut.GetSummaryAsync(TodayRange(), new ReportFilter { ProductId = target.Id }, _ownerId);

        Assert.Equal(200m, summary.GrossSales);
        Assert.Equal(2m, summary.ItemsSold);
    }

    [Fact]
    public async Task Filter_ByCategory_NarrowsTheSummary()
    {
        var category = new Category { Name = "Snacks", IsActive = true };
        _fixture.Context.Categories.Add(category);
        await _fixture.Context.SaveChangesAsync();

        var inCategory = await SeedProductAsync(sellingPrice: 100, gstRate: 0, categoryId: category.Id);
        var outOfCategory = await SeedProductAsync(sellingPrice: 50, gstRate: 0);
        await SellAsync(inCategory, 1);
        await SellAsync(outOfCategory, 1);

        var summary = await _sut.GetSummaryAsync(TodayRange(), new ReportFilter { CategoryId = category.Id }, _ownerId);

        Assert.Equal(100m, summary.GrossSales);
    }

    [Fact]
    public async Task DateRange_ExcludesSalesOutsideIt()
    {
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 0);
        await SellAsync(product, 1);

        var yesterday = ReportDateRange.Resolve(ReportDatePreset.Yesterday);
        var summary = await _sut.GetSummaryAsync(yesterday, filter: null, _ownerId);

        Assert.Equal(0m, summary.GrossSales);
        Assert.Equal(0, summary.BillCount);
    }

    // ---------------------------------------------------------------- GST

    [Fact]
    public async Task Gst_TaxExclusive_ComputesTaxOnTopOfPrice()
    {
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 12, isTaxInclusive: false);
        await SellAsync(product, 1);

        var gst = await _sut.GetGstReportAsync(TodayRange(), _ownerId);

        var bucket = Assert.Single(gst.SalesByRate);
        Assert.Equal(12m, bucket.RatePercent);
        Assert.Equal(100m, bucket.TaxableAmount);
        Assert.Equal(12m, bucket.TaxAmount);
        Assert.Equal(1, bucket.InvoiceCount);
    }

    [Fact]
    public async Task Gst_SplitsEvenlyIntoCgstAndSgst_WithZeroIgst()
    {
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 18);
        await SellAsync(product, 1);

        var gst = await _sut.GetGstReportAsync(TodayRange(), _ownerId);

        var bucket = gst.SalesByRate.Single();
        Assert.Equal(bucket.Cgst + bucket.Sgst, bucket.TaxAmount);
        Assert.Equal(bucket.Cgst, bucket.Sgst);
        Assert.Equal(0m, bucket.Igst);
    }

    [Fact]
    public async Task Gst_GroupsByRate_WhenMultipleRatesAreSold()
    {
        var five = await SeedProductAsync(sellingPrice: 100, gstRate: 5);
        var eighteen = await SeedProductAsync(sellingPrice: 100, gstRate: 18);
        await SellAsync(five, 1);
        await SellAsync(eighteen, 1);

        var gst = await _sut.GetGstReportAsync(TodayRange(), _ownerId);

        Assert.Equal(2, gst.SalesByRate.Count);
        Assert.Contains(gst.SalesByRate, b => b.RatePercent == 5m);
        Assert.Contains(gst.SalesByRate, b => b.RatePercent == 18m);
        Assert.Equal(gst.SalesByRate.Sum(b => b.TaxAmount), gst.SalesGstCollected);
    }

    [Fact]
    public async Task Gst_UnaffectedByLaterChangesToTheProductsLiveGstRate()
    {
        // The historical snapshot, not the live product, is what a GST report must reflect (PRD
        // "must use historical invoice data, never current product values").
        var product = await SeedProductAsync(sellingPrice: 100, gstRate: 12);
        await SellAsync(product, 1);

        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.GstRatePercent = 28;
        tracked.SellingPrice = 999;
        await _fixture.Context.SaveChangesAsync();

        var gst = await _sut.GetGstReportAsync(TodayRange(), _ownerId);

        var bucket = Assert.Single(gst.SalesByRate);
        Assert.Equal(12m, bucket.RatePercent);
        Assert.Equal(100m, bucket.TaxableAmount);
    }

    [Fact]
    public async Task RequiresReportsViewPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetSummaryAsync(TodayRange(), null, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetGstReportAsync(TodayRange(), cashier.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
