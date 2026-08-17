using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Products;
using Kirana.Application.Reports;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>
/// Phase 17A: what a sale cost the shop is a historical fact, not a figure recomputed from today's
/// master data.
///
/// <para>The central invariant: sell today, change the product's purchase price tomorrow, and the
/// profit reported for today must not move. Before this phase COGS was
/// <c>quantity × Product.PurchasePrice</c> read at report time, so repricing silently rewrote past
/// profit. <see cref="HistoricalCogs_DoesNotMove_WhenTheProductIsRepricedLater"/> is the test that
/// pins it; an implementation that reads the product still passes almost everything else here.</para>
///
/// <para>The second rule is that an unknown cost is not a zero cost. Sales predating this phase
/// carry <c>null</c>, and counting those at zero would report them at 100% margin — a wrong number
/// that looks like a right one.</para>
/// </summary>
public sealed class HistoricalCostBasisTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProfitReportService _profit;
    private readonly SaleService _sales;
    private readonly SalesReturnService _returns;
    private readonly int _ownerId;

    public HistoricalCostBasisTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        _profit = new ProfitReportService(_fixture.Context, permissions);
        _sales = new SaleService(_fixture.Context, seq, audit, permissions);
        _returns = new SalesReturnService(_fixture.Context, seq, audit, permissions);
    }

    // ---------------- capture ----------------

    [Fact]
    public async Task ASale_RecordsWhatEachUnitCost()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);

        var sale = await SellAsync(product, 10);

        var item = Assert.Single(sale.Items);
        Assert.Equal(100m, item.UnitCostSnapshot);
        Assert.Equal(150m, item.UnitPriceSnapshot);   // cost and price are separate facts
    }

    [Fact]
    public async Task TheRecordedCost_IsTheCostAtTheMomentOfSale_NotAnEarlierOrLaterOne()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SetPurchasePriceAsync(product.Id, 120m);   // cost rises BEFORE the sale

        var sale = await SellAsync(product, 1);

        Assert.Equal(120m, Assert.Single(sale.Items).UnitCostSnapshot);
    }

    [Fact]
    public async Task TheRecordedCost_IsWrittenWithTheSale_NotAfterwards()
    {
        // Reached through a fresh context, so it is proven to be committed with the sale rather
        // than lingering in the change tracker of the context that wrote it.
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 4);
        _fixture.Context.ChangeTracker.Clear();

        var persisted = await _fixture.Context.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == sale.Id);
        Assert.Equal(100m, persisted.UnitCostSnapshot);
    }

    // ---------------- the central invariant ----------------

    [Fact]
    public async Task HistoricalCogs_DoesNotMove_WhenTheProductIsRepricedLater()
    {
        // The spec's worked example: 10 units bought at 100, sold at 150.
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);

        var before = await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(1_000m, before.CostOfGoodsSold);
        Assert.Equal(500m, before.GrossProfit);

        await SetPurchasePriceAsync(product.Id, 130m);

        var after = await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(1_000m, after.CostOfGoodsSold);   // NOT 1,300
        Assert.Equal(500m, after.GrossProfit);

        // And again, to prove it is stable rather than merely lagging by one change.
        await SetPurchasePriceAsync(product.Id, 60m);

        var third = await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(1_000m, third.CostOfGoodsSold);   // NOT 600 either
        Assert.Equal(500m, third.GrossProfit);
    }

    [Fact]
    public async Task RepricingAProduct_LeavesTheStoredSnapshotUntouched()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 3);

        await SetPurchasePriceAsync(product.Id, 999m);

        _fixture.Context.ChangeTracker.Clear();
        var item = await _fixture.Context.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == sale.Id);
        Assert.Equal(100m, item.UnitCostSnapshot);
        Assert.Equal(999m, (await _fixture.Context.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id)).PurchasePrice);
    }

    [Fact]
    public async Task RepricingThroughTheRealProductService_DoesNotTouchSoldLines()
    {
        // The sibling test above rewrites the cost straight through the DbContext, which proves the
        // report reads the snapshot but cannot prove the repricing PATH leaves it alone. This goes
        // through ProductService.UpdateAsync — the code an operator actually triggers — so a future
        // change there that reached into SaleItems would be caught here rather than in production.
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);

        var sequences = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        var products = new ProductService(
            _fixture.Context, sequences, audit,
            new BarcodeService(_fixture.Context, sequences, audit, permissions),
            permissions,
            new ProductPricingService(_fixture.Context, audit, permissions));

        await products.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = product.Name,
            Unit = product.Unit,
            PurchasePrice = 130m,
            Mrp = product.Mrp,
            SellingPrice = product.SellingPrice,
            GstRatePercent = product.GstRatePercent,
            PricingType = product.PricingType,
            PerformedByUserId = _ownerId,
        });

        _fixture.Context.ChangeTracker.Clear();
        var item = await _fixture.Context.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == sale.Id);
        Assert.Equal(100m, item.UnitCostSnapshot);

        var summary = await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(1_000m, summary.CostOfGoodsSold);
    }

    // ---------------- unknown cost is not zero cost ----------------

    [Fact]
    public async Task ALineWithNoRecordedCost_IsExcludedFromCost_NotCountedAsFree()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(sale.Id);   // as every pre-17A sale looks

        var summary = await _profit.GetSummaryAsync(TodayRange(), _ownerId);

        // Zero cost would have produced gross profit of 1,500 — the full revenue, at 100% margin.
        Assert.Equal(0m, summary.CostOfGoodsSold);
        Assert.Equal(0, summary.KnownCostLineCount);
        Assert.Equal(1, summary.UnknownCostLineCount);
        Assert.False(summary.HasCompleteCostBasis);
    }

    [Fact]
    public async Task AMixedPeriod_CostsWhatItCan_AndSaysWhatItCouldNot()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var historical = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(historical.Id);
        await SellAsync(product, 4);   // this one carries its cost

        var summary = await _profit.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(400m, summary.CostOfGoodsSold);   // only the costed line
        Assert.Equal(1, summary.KnownCostLineCount);
        Assert.Equal(1, summary.UnknownCostLineCount);
        Assert.False(summary.HasCompleteCostBasis);
    }

    [Fact]
    public async Task APeriodWhereEveryLineIsCosted_ReportsACompleteBasis()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        await SellAsync(product, 10);

        var summary = await _profit.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.True(summary.HasCompleteCostBasis);
        Assert.Equal(1, summary.KnownCostLineCount);
        Assert.Equal(0, summary.UnknownCostLineCount);
    }

    // ---------------- returns ----------------

    [Fact]
    public async Task AReturn_ReversesCostAtTheOriginalPrice_NotTodays()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        await SetPurchasePriceAsync(product.Id, 130m);

        await _returns.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            RefundMethod = RefundMethod.Cash,
            ProcessedByUserId = _ownerId,
            Lines = [new SalesReturnLineInput
            {
                SaleItemId = sale.Items.First().Id, Quantity = 4m, Disposition = ReturnDisposition.ReturnToStock,
            }],
        });

        // Sold 10 @100 = 1,000; returned 4 @100 = 400. At today's 130 the return would credit 520.
        var summary = await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(600m, summary.CostOfGoodsSold);
    }

    [Fact]
    public async Task AReturnAgainstAnUnknownCostSale_AddsNoPhantomCredit()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        await ClearCostSnapshotAsync(sale.Id);

        await _returns.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            RefundMethod = RefundMethod.Cash,
            ProcessedByUserId = _ownerId,
            Lines = [new SalesReturnLineInput
            {
                SaleItemId = sale.Items.First().Id, Quantity = 4m, Disposition = ReturnDisposition.ReturnToStock,
            }],
        });

        // Neither side is known, so COGS is 0 — never a negative cost from crediting a return
        // against a sale that was never costed.
        var summary = await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(0m, summary.CostOfGoodsSold);
    }

    // ---------------- nothing else moved ----------------

    [Fact]
    public async Task EveryOtherSnapshotOnTheLine_IsUnchangedByThisPhase()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        product.GstRatePercent = 5m;
        product.HsnCode = "1006";
        product.PricingType = PricingType.Inclusive;
        await _fixture.Context.SaveChangesAsync();

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 2, DiscountPercent = 10 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 270m, AmountTendered = 270m }],
            CashierUserId = _ownerId,
        });

        var item = Assert.Single(sale.Items);
        Assert.Equal(150m, item.UnitPriceSnapshot);
        Assert.Equal(160m, item.MrpSnapshot);
        Assert.Equal(5m, item.GstRatePercentSnapshot);
        Assert.Equal("1006", item.HsnCodeSnapshot);
        Assert.True(item.IsTaxInclusiveSnapshot);
        Assert.Equal(10m, item.DiscountPercent);
        Assert.Equal(30m, item.DiscountAmount);
        Assert.Equal(270m, item.LineTotal);
        Assert.Equal(100m, item.UnitCostSnapshot);   // the only addition
    }

    [Fact]
    public async Task RunningTheProfitReport_WritesNothing()
    {
        var product = await SeedProductAsync(cost: 100m, price: 150m);
        var sale = await SellAsync(product, 10);
        _fixture.Context.ChangeTracker.Clear();

        var before = await FingerprintAsync(sale.Id);
        await _profit.GetSummaryAsync(TodayRange(), _ownerId);
        await _profit.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(before, await FingerprintAsync(sale.Id));
    }

    // ---------------- helpers ----------------

    private static ReportDateRange TodayRange() => ReportDateRange.Resolve(ReportDatePreset.Today);

    private async Task<Product> SeedProductAsync(decimal cost, decimal price, decimal stock = 100m)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Cost Basis Product",
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
            .Select(i => new { i.Id, i.UnitCostSnapshot, i.UnitPriceSnapshot, i.LineTotal, i.GstAmount })
            .ToListAsync();
        return string.Join("|", items.Select(i => $"{i.Id}:{i.UnitCostSnapshot}:{i.UnitPriceSnapshot}:{i.LineTotal}:{i.GstAmount}"));
    }

    public void Dispose() => _fixture.Dispose();
}
