using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Inventories;
using Kirana.Application.Products;
using Kirana.Application.Reports;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>
/// Phase 17A-Fix: the Dashboard's Gross Profit Trend chart was found (during the Phase 17B review)
/// to hold a second, independent copy of the exact defect Phase 17A fixed in
/// <c>ProfitReportService</c> — its sold- and returned-cost queries both read
/// <c>Product.PurchasePrice</c>, current master data, instead of the cost snapshotted on each line
/// at sale time. Repricing a product silently redrew past months on this chart even though the
/// Profit report itself was already correct for the same period.
///
/// <para>These tests pin the chart to the same historical basis, and reprice TWICE (never once) so
/// an implementation that merely lags by one change still fails — the same discipline
/// <c>HistoricalCostBasisTests</c> uses for the Profit report.</para>
/// </summary>
public sealed class DashboardGrossProfitTrendCostBasisTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly DashboardService _dashboard;
    private readonly SaleService _sales;
    private readonly SalesReturnService _returns;
    private readonly int _ownerId;

    public DashboardGrossProfitTrendCostBasisTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        var inventory = new InventoryService(_fixture.Context, audit, permissions);
        var profit = new ProfitReportService(_fixture.Context, permissions);

        _dashboard = new DashboardService(_fixture.Context, inventory, profit, permissions);
        _sales = new SaleService(_fixture.Context, seq, audit, permissions);
        _returns = new SalesReturnService(_fixture.Context, seq, audit, permissions);
    }

    // ---------------- Test 1: historical cost survives repricing ----------------

    [Fact]
    public async Task HistoricalGrossProfit_DoesNotMove_WhenTheProductIsRepricedLater()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);   // revenue 1,500; cost 1,000 at today's snapshot

        var before = await CurrentMonthPointAsync();
        Assert.Equal(500m, before);   // 1,500 - 1,000

        await SetPurchasePriceAsync(product.Id, 130m);
        var after = await CurrentMonthPointAsync();
        Assert.Equal(500m, after);   // NOT 1,500 - 1,300 = 200
    }

    // ---------------- Test 6: reprice TWICE, so a one-step-lag bug still fails ----------------

    [Fact]
    public async Task HistoricalGrossProfit_StaysPinned_AcrossTwoSeparateReprices()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);

        Assert.Equal(500m, await CurrentMonthPointAsync());

        await SetPurchasePriceAsync(product.Id, 130m);
        Assert.Equal(500m, await CurrentMonthPointAsync());

        await SetPurchasePriceAsync(product.Id, 175m);
        Assert.Equal(500m, await CurrentMonthPointAsync());   // NOT 1,500 - 1,750 = -250
    }

    // ---------------- Test 2: returned units use the original cost ----------------

    [Fact]
    public async Task ReturnedGrossProfit_UsesTheOriginatingCost_NotTodays()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);

        // Net effect on revenue and cost before any return: +1,500 revenue, -1,000 cost = 500.
        await SetPurchasePriceAsync(product.Id, 130m);

        await _returns.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            RefundMethod = RefundMethod.Cash,
            ProcessedByUserId = _ownerId,
            Lines = [new SalesReturnLineInput
            {
                SaleItemId = sale.Items.First().Id, Quantity = 3m, Disposition = ReturnDisposition.ReturnToStock,
            }],
        });

        // Revenue: 1,500 - (3 * 150) = 1,050. Cost: 1,000 - (3 * 100) = 700. Profit: 350.
        // At today's 130 the return would have credited 390, giving cost 610 and profit 440.
        Assert.Equal(350m, await CurrentMonthPointAsync());
    }

    // ---------------- Test 3: multiple partial returns ----------------

    [Fact]
    public async Task MultiplePartialReturns_NetCorrectly_WithoutDoubleCountingOrLosingEither()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        var saleItemId = sale.Items.First().Id;

        await _returns.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id, RefundMethod = RefundMethod.Cash, ProcessedByUserId = _ownerId,
            Lines = [new SalesReturnLineInput { SaleItemId = saleItemId, Quantity = 3m, Disposition = ReturnDisposition.ReturnToStock }],
        });
        await _returns.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id, RefundMethod = RefundMethod.Cash, ProcessedByUserId = _ownerId,
            Lines = [new SalesReturnLineInput { SaleItemId = saleItemId, Quantity = 2m, Disposition = ReturnDisposition.ReturnToStock }],
        });

        // Revenue: 1,500 - (5 * 150) = 750. Cost: 1,000 - (5 * 100) = 500. Profit: 250.
        Assert.Equal(250m, await CurrentMonthPointAsync());
    }

    // ---------------- Test 4: unknown historical cost ----------------

    [Fact]
    public async Task ALineWithNoRecordedCost_IsExcluded_NeverTreatedAsZeroOrCurrentCost()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(sale.Id);

        // Cost contributes nothing, so the point is pure revenue (an upper bound on profit) rather
        // than a fabricated figure at ₹0 cost (over-stated) or at current cost (invented history).
        Assert.Equal(1_500m, await CurrentMonthPointAsync());
    }

    [Fact]
    public async Task AReturnAgainstAnUnknownCostSale_AddsNoPhantomCreditToTheChart()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(sale.Id);

        await _returns.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id, RefundMethod = RefundMethod.Cash, ProcessedByUserId = _ownerId,
            Lines = [new SalesReturnLineInput { SaleItemId = sale.Items.First().Id, Quantity = 3m, Disposition = ReturnDisposition.ReturnToStock }],
        });

        // Revenue: 1,500 - 450 = 1,050. Cost: 0 (both sides unknown). Profit: 1,050 — never negative
        // from crediting a return whose cost side was never known either.
        Assert.Equal(1_050m, await CurrentMonthPointAsync());
    }

    // ---------------- Test 7: consistency with ProfitReportService ----------------

    [Fact]
    public async Task TheChartAgreesWithTheProfitReport_ForTheSamePeriod()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);
        await SetPurchasePriceAsync(product.Id, 130m);

        var chartValue = await CurrentMonthPointAsync();

        var profitService = new ProfitReportService(_fixture.Context, new PermissionEnforcer(_fixture.Context));
        var summary = await profitService.GetSummaryAsync(ReportDateRange.Resolve(ReportDatePreset.ThisMonth), _ownerId);

        Assert.Equal(summary.GrossProfit, chartValue);
    }

    // ---------------- read-only ----------------

    [Fact]
    public async Task BuildingTheChart_WritesNothing()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        _fixture.Context.ChangeTracker.Clear();

        var before = await FingerprintAsync(sale.Id);
        await CurrentMonthPointAsync();
        await CurrentMonthPointAsync();

        Assert.Equal(before, await FingerprintAsync(sale.Id));
    }

    // ---------------- helpers ----------------

    private async Task<decimal> CurrentMonthPointAsync()
    {
        var charts = await _dashboard.GetChartsAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId);
        return charts.GrossProfitTrend.Points.Last().Value;   // the trend's last bucket is always the current month
    }

    private async Task<Product> SeedProductAsync(decimal cost, decimal price, decimal stock = 100m)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Trend Cost Basis Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = cost,
            Mrp = price + 10,
            SellingPrice = price,
            IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<Sale> SellAsync(Product product, decimal quantity) =>
        _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity }],
            Payments =
            [
                new SalePaymentInput
                {
                    Method = PaymentMethod.Cash,
                    Amount = product.SellingPrice * quantity,
                    AmountTendered = product.SellingPrice * quantity,
                },
            ],
            CashierUserId = _ownerId,
        });

    private async Task SetPurchasePriceAsync(int productId, decimal cost)
    {
        var product = await _fixture.Context.Products.SingleAsync(p => p.Id == productId);
        product.PurchasePrice = cost;
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
    }

    /// <summary>Blanks the snapshot so the line looks exactly like one recorded before Phase 17A.
    /// Only a test may do this — no production path clears a captured cost.</summary>
    private async Task ClearCostSnapshotAsync(int saleId)
    {
        foreach (var item in await _fixture.Context.SaleItems.Where(i => i.SaleId == saleId).ToListAsync())
        {
            item.UnitCostSnapshot = null;
        }

        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
    }

    private async Task<string> FingerprintAsync(int saleId)
    {
        var items = await _fixture.Context.SaleItems.AsNoTracking()
            .Where(i => i.SaleId == saleId).OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.UnitCostSnapshot, i.UnitPriceSnapshot, i.LineTotal })
            .ToListAsync();
        return string.Join("|", items.Select(i => $"{i.Id}:{i.UnitCostSnapshot}:{i.UnitPriceSnapshot}:{i.LineTotal}"));
    }

    public void Dispose() => _fixture.Dispose();
}
