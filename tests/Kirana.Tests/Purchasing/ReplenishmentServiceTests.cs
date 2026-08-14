using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Purchasing;

public sealed class ReplenishmentServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ReplenishmentService _sut;
    private readonly int _ownerId;

    public ReplenishmentServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _sut = new ReplenishmentService(_fixture.Context, new PermissionEnforcer(_fixture.Context));
    }

    private async Task<(Product Product, Supplier Supplier)> SeedAsync(
        decimal stock, decimal reorder = 20, decimal target = 50,
        bool enabled = true, UnitOfMeasure unit = UnitOfMeasure.Piece)
    {
        var supplier = new Supplier { SupplierCode = $"SUP-{Guid.NewGuid():N}"[..12], Name = $"Supplier {Guid.NewGuid():N}", IsActive = true };
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12], Name = $"Product {Guid.NewGuid():N}",
            Unit = unit, PurchasePrice = 10, Mrp = 12, SellingPrice = 11, IsActive = true,
            MinimumStock = reorder, ReorderQuantity = target, ReplenishmentEnabled = enabled,
            PreferredSupplier = supplier,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return (product, supplier);
    }

    private Task<ReplenishmentSummary> NeedsAsync() => _sut.GetRecommendationsAsync(
        new ReplenishmentQuery { NeedsReorderOnly = true }, _ownerId);

    [Theory]
    [InlineData(8, 42, ReplenishmentStatus.BelowReorderLevel)]
    [InlineData(20, 30, ReplenishmentStatus.AtReorderLevel)]
    [InlineData(0, 50, ReplenishmentStatus.OutOfStock)]
    public async Task CandidateThresholds_UseTargetMinusCurrent(
        decimal stock, decimal expected, ReplenishmentStatus status)
    {
        await SeedAsync(stock);
        var item = Assert.Single((await NeedsAsync()).Items);
        Assert.Equal(expected, item.SuggestedQuantity);
        Assert.Equal(status, item.Status);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(35)]
    public async Task AboveReorderLevel_IsNotRecommended(decimal stock)
    {
        await SeedAsync(stock);
        Assert.Empty((await NeedsAsync()).Items);
    }

    [Fact]
    public async Task DisabledProduct_IsExcludedAndShownAsNotConfigured()
    {
        await SeedAsync(8, enabled: false);
        Assert.Empty((await NeedsAsync()).Items);
        var all = await _sut.GetRecommendationsAsync(
            new ReplenishmentQuery { NeedsReorderOnly = false }, _ownerId);
        Assert.Equal(ReplenishmentStatus.NotConfigured, Assert.Single(all.Items).Status);
        Assert.Equal(1, all.UnconfiguredLowStockProducts);
    }

    [Fact]
    public async Task DecimalUnit_PreservesFractionalRecommendation()
    {
        await SeedAsync(12.5m, 20m, 50m, unit: UnitOfMeasure.Kilogram);
        Assert.Equal(37.5m, Assert.Single((await NeedsAsync()).Items).SuggestedQuantity);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Submitted, 42, 0)]
    [InlineData(PurchaseOrderStatus.PartiallyReceived, 20, 22)]
    [InlineData(PurchaseOrderStatus.Draft, 42, 42)]
    [InlineData(PurchaseOrderStatus.Cancelled, 42, 42)]
    [InlineData(PurchaseOrderStatus.Completed, 42, 42)]
    public async Task OnlyEligibleOpenPurchaseOrdersReduceRecommendation(
        PurchaseOrderStatus status, decimal ordered, decimal expected)
    {
        var (product, supplier) = await SeedAsync(8);
        await AddOrderAsync(product, supplier, status, ordered);
        Assert.Equal(expected, Assert.Single((await _sut.GetRecommendationsAsync(
            new ReplenishmentQuery { NeedsReorderOnly = false, Enabled = true }, _ownerId)).Items).SuggestedQuantity);
    }

    [Fact]
    public async Task CompletedReceipt_ReducesOnlyOutstandingOpenCommitment()
    {
        var (product, supplier) = await SeedAsync(8);
        var (order, line) = await AddOrderAsync(product, supplier, PurchaseOrderStatus.PartiallyReceived, 42);
        var receipt = new GoodsReceipt
        {
            GoodsReceiptNumber = "GRN-TEST", PurchaseOrder = order, Supplier = supplier,
            SupplierNameSnapshot = supplier.Name, SupplierCodeSnapshot = supplier.SupplierCode,
            Status = GoodsReceiptStatus.Completed,
        };
        _fixture.Context.GoodsReceiptItems.Add(new GoodsReceiptItem
        {
            GoodsReceipt = receipt, PurchaseOrderItem = line, Product = product,
            ProductNameSnapshot = product.Name, ProductCodeSnapshot = product.ProductCode,
            UnitSnapshot = product.Unit, OrderedQuantitySnapshot = 42, ReceivedQuantity = 20,
        });
        await _fixture.Context.SaveChangesAsync();

        var item = Assert.Single((await _sut.GetRecommendationsAsync(
            new ReplenishmentQuery { NeedsReorderOnly = false, Enabled = true }, _ownerId)).Items);
        Assert.Equal(22, item.OpenPurchaseOrderQuantity);
        Assert.Equal(20, item.SuggestedQuantity);
    }

    [Fact]
    public async Task LatestCompletedPurchaseCost_DrivesEstimateWithoutChangingProductCost()
    {
        var (product, supplier) = await SeedAsync(8);
        await AddPurchaseCostAsync(product, supplier, 20, DateTime.UtcNow.AddDays(-2));
        await AddPurchaseCostAsync(product, supplier, 25, DateTime.UtcNow.AddDays(-1));

        var item = Assert.Single((await NeedsAsync()).Items);
        Assert.Equal(25, item.EstimatedUnitCost);
        Assert.Equal(1050, item.EstimatedOrderValue);
        Assert.Equal(10, (await _fixture.Context.Products.FindAsync(product.Id))!.PurchasePrice);
    }

    [Fact]
    public async Task NoPurchaseHistory_ReportsUnavailableCost()
    {
        await SeedAsync(8);
        var item = Assert.Single((await NeedsAsync()).Items);
        Assert.Null(item.EstimatedUnitCost);
        Assert.Null(item.EstimatedOrderValue);
    }

    [Fact]
    public async Task RefreshUsesCurrentInventoryAndAnalysisDoesNotWriteAnything()
    {
        var (product, supplier) = await SeedAsync(21);
        var countsBefore = await CountsAsync();
        Assert.Empty((await NeedsAsync()).Items);
        (await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id)).QuantityOnHand = 16;
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(34, Assert.Single((await NeedsAsync()).Items).SuggestedQuantity);
        Assert.Equal(countsBefore, await CountsAsync());
        Assert.Equal(0, supplier.OutstandingBalance);
    }

    [Fact]
    public async Task UnauthorizedCashierCannotReadRecommendations()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetRecommendationsAsync(
            new ReplenishmentQuery(), cashier.Id));
    }

    private async Task<(PurchaseOrder, PurchaseOrderItem)> AddOrderAsync(
        Product product, Supplier supplier, PurchaseOrderStatus status, decimal quantity)
    {
        var order = new PurchaseOrder
        {
            PurchaseOrderNumber = $"PO-{Guid.NewGuid():N}"[..12], Supplier = supplier,
            SupplierNameSnapshot = supplier.Name, SupplierCodeSnapshot = supplier.SupplierCode, Status = status,
        };
        var item = new PurchaseOrderItem
        {
            PurchaseOrder = order, Product = product, ProductNameSnapshot = product.Name,
            ProductCodeSnapshot = product.ProductCode, UnitSnapshot = product.Unit.ToString(),
            OrderedQuantity = quantity, UnitCost = 10, PricingTypeSnapshot = PricingType.Exclusive,
        };
        _fixture.Context.PurchaseOrderItems.Add(item);
        await _fixture.Context.SaveChangesAsync();
        return (order, item);
    }

    private async Task AddPurchaseCostAsync(Product product, Supplier supplier, decimal cost, DateTime date)
    {
        var purchase = new Purchase
        {
            PurchaseNumber = $"PUR-{Guid.NewGuid():N}"[..12], Supplier = supplier,
            PurchaseDateUtc = date, Status = PurchaseStatus.Completed,
        };
        _fixture.Context.PurchaseItems.Add(new PurchaseItem
        {
            Purchase = purchase, Product = product, ProductNameSnapshot = product.Name,
            ProductCodeSnapshot = product.ProductCode, UnitSnapshot = product.Unit.ToString(),
            Quantity = 1, PurchasePriceSnapshot = cost,
        });
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task<(int Orders, int Purchases, int Receipts, int Movements, int Audits)> CountsAsync() =>
        (await _fixture.Context.PurchaseOrders.CountAsync(), await _fixture.Context.Purchases.CountAsync(),
         await _fixture.Context.GoodsReceipts.CountAsync(), await _fixture.Context.StockMovements.CountAsync(),
         await _fixture.Context.AuditLogs.CountAsync());

    public void Dispose() => _fixture.Dispose();
}
