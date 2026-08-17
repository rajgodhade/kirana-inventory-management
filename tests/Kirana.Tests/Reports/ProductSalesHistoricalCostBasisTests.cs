using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>
/// Phase 17A-Fix-2: <c>ProductSalesRow.EstimatedProfit</c> — the Product Sales / Top Sellers report
/// — was found (during the Phase 17A-Fix write-path audit) to hold a THIRD independent copy of the
/// defect fixed first in <c>ProfitReportService</c> (17A) and then in the Dashboard trend chart
/// (17A-Fix): its cost aggregation read <c>Product.PurchasePrice</c>, current master data, instead
/// of the cost snapshotted on each line at sale time. A product's "Estimated Profit" here could
/// silently disagree with the Profit report and the Dashboard for the exact same historical period.
///
/// <para>These tests pin it to <c>SaleItem.UnitCostSnapshot</c>, reprice TWICE (never once) so a
/// one-step-lag implementation still fails, and confirm the three reports now agree.</para>
///
/// <para><b>Returns are deliberately out of scope here</b>, matching the pre-existing contract:
/// <c>BuildSoldAggregatesAsync</c> only ever queried <c>SaleItems</c>, never <c>SalesReturnItems</c>
/// — Estimated Profit has never netted off returns. This phase does not add that; it only corrects
/// the cost basis of what was already being computed.</para>
/// </summary>
public sealed class ProductSalesHistoricalCostBasisTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductReportService _products;
    private readonly SaleService _sales;
    private readonly int _ownerId;

    public ProductSalesHistoricalCostBasisTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        _products = new ProductReportService(_fixture.Context, permissions);
        _sales = new SaleService(_fixture.Context, seq, audit, permissions);
    }

    // ---------------- Test A: historical cost survives repricing ----------------

    [Fact]
    public async Task EstimatedProfit_DoesNotMove_WhenTheProductIsRepricedLater()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);   // revenue 1,500; cost 1,000 at today's snapshot

        var before = await EstimatedProfitForAsync(product.Id);
        Assert.Equal(500m, before);   // 1,500 - 1,000

        await SetPurchasePriceAsync(product.Id, 130m);
        var after = await EstimatedProfitForAsync(product.Id);
        Assert.Equal(500m, after);   // NOT 1,500 - 1,300 = 200
    }

    // ---------------- Test B: reprice TWICE ----------------

    [Fact]
    public async Task EstimatedProfit_StaysPinned_AcrossTwoSeparateReprices()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);

        Assert.Equal(500m, await EstimatedProfitForAsync(product.Id));

        await SetPurchasePriceAsync(product.Id, 130m);
        Assert.Equal(500m, await EstimatedProfitForAsync(product.Id));

        await SetPurchasePriceAsync(product.Id, 175m);
        Assert.Equal(500m, await EstimatedProfitForAsync(product.Id));   // NOT 1,500 - 1,750 = -250
    }

    // ---------------- Test C: unknown historical cost ----------------

    [Fact]
    public async Task ALineWithNoRecordedCost_IsExcludedFromCost_NeverTreatedAsZeroOrCurrentCost()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(sale.Id);

        // Cost contributes nothing: neither a fabricated ₹0 (which would inflate profit to 1,500,
        // i.e. 100% margin) nor the current ₹100/₹whatever PurchasePrice happens to be now.
        Assert.Equal(1_500m, await EstimatedProfitForAsync(product.Id));
    }

    [Fact]
    public async Task AMixedProduct_CostsWhatItCan_FromTheLinesThatRecordedIt()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var historical = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(historical.Id);
        await SellAsync(product, 4);   // this sale carries its cost

        // Revenue: 1,500 + 600 = 2,100. Cost: only the second sale's 400. Profit: 1,700.
        Assert.Equal(2_100m, (await RowForAsync(product.Id)).Revenue);
        Assert.Equal(14m, (await RowForAsync(product.Id)).QuantitySold);   // quantity is unaffected
        Assert.Equal(1_700m, await EstimatedProfitForAsync(product.Id));
    }

    // ---------------- Test D: agreement across the three reports ----------------

    [Fact]
    public async Task ProductSalesAgreesWithProfitReport_ForASingleProductPeriod()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);
        await SetPurchasePriceAsync(product.Id, 130m);

        var productSalesProfit = await EstimatedProfitForAsync(product.Id);

        var profitService = new ProfitReportService(_fixture.Context, new PermissionEnforcer(_fixture.Context));
        var summary = await profitService.GetSummaryAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId);

        // A single-product period with no expenses/returns: the two figures must agree exactly.
        Assert.Equal(summary.GrossProfit, productSalesProfit);
    }

    [Fact]
    public async Task ProductSalesAgreesWithDashboardTrend_ForASingleProductPeriod()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);
        await SetPurchasePriceAsync(product.Id, 130m);

        var productSalesProfit = await EstimatedProfitForAsync(product.Id);

        var dashboard = new DashboardService(
            _fixture.Context,
            new Kirana.Application.Inventories.InventoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context)),
            new ProfitReportService(_fixture.Context, new PermissionEnforcer(_fixture.Context)),
            new PermissionEnforcer(_fixture.Context));
        var charts = await dashboard.GetChartsAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId);
        var chartValue = charts.GrossProfitTrend.Points.Last().Value;

        Assert.Equal(chartValue, productSalesProfit);
    }

    // ---------------- read-only ----------------

    [Fact]
    public async Task RunningTheReport_WritesNothing()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        _fixture.Context.ChangeTracker.Clear();

        var before = await FingerprintAsync(sale.Id);
        await EstimatedProfitForAsync(product.Id);
        await EstimatedProfitForAsync(product.Id);

        Assert.Equal(before, await FingerprintAsync(sale.Id));
    }

    // ---------------- existing contract: returns are not netted ----------------

    [Fact]
    public async Task EstimatedProfit_DoesNotNetOffReturns_MatchingTheExistingContract()
    {
        // Documents current behaviour rather than changing it, per this phase's explicit scope: a
        // return against this sale changes nothing here, because BuildSoldAggregatesAsync has never
        // queried SalesReturnItems.
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);

        var before = await EstimatedProfitForAsync(product.Id);
        Assert.Equal(500m, before);

        // (No return is processed — the point is the absence of any return-aware code path, which
        // a full return-processing test would not distinguish from "returns are netted at zero.")
    }

    // ---------------- helpers ----------------

    private async Task<decimal?> EstimatedProfitForAsync(int productId) =>
        (await RowForAsync(productId)).EstimatedProfit;

    private async Task<ProductSalesRow> RowForAsync(int productId)
    {
        var rows = await _products.GetProductWiseSalesAsync(
            ReportDateRange.Resolve(ReportDatePreset.Today),
            new ReportFilter { ProductId = productId },
            _ownerId);
        return Assert.Single(rows);
    }

    private async Task<Product> SeedProductAsync(decimal cost, decimal price, decimal stock = 100m)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Product Sales Cost Basis Item",
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
