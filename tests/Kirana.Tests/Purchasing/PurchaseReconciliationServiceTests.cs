using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Purchasing;

public sealed class PurchaseReconciliationServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly int _ownerId;
    private readonly Supplier _supplier;
    private readonly Product _product;
    private readonly PurchaseReconciliationService _service;

    public PurchaseReconciliationServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _supplier = new Supplier { SupplierCode = "SUP-REC", Name = "Original Supplier", IsActive = true };
        _product = new Product
        {
            ProductCode = "PRD-REC", Name = "Original Product", Sku = "ORIGINAL-SKU",
            Unit = UnitOfMeasure.Piece, PurchasePrice = 42m, Mrp = 50m, SellingPrice = 48m,
            GstRatePercent = 0m, PricingType = PricingType.Exclusive, IsActive = true,
        };
        _fixture.Context.AddRange(_supplier, _product);
        _fixture.Context.Inventories.Add(new Inventory { Product = _product, QuantityOnHand = 10m });
        _fixture.Context.SaveChanges();
        _service = new PurchaseReconciliationService(
            _fixture.Context, new PermissionEnforcer(_fixture.Context));
    }

    [Fact]
    public async Task EqualOrderedReceivedAndPurchased_IsFullyReconciled()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 100m, "GRN-FULL");
        await AddPurchaseAsync(order, 100m, 42m, "PUR-FULL");

        var result = await DetailAsync(order.Id);

        Assert.True(result.Has(PurchaseReconciliationFlags.FullyReconciled));
        Assert.Equal(100m, result.OrderedQuantity);
        Assert.Equal(100m, result.ReceivedQuantity);
        Assert.Equal(100m, result.PurchasedQuantity);
        Assert.Equal(0m, result.PendingReceiptQuantity);
        Assert.Equal(0m, result.PendingInvoiceQuantity);
    }

    [Fact]
    public async Task PartialReceiptAndMatchingPurchase_ShowsPendingReceiptOnly()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 60m, "GRN-PARTIAL");
        await AddPurchaseAsync(order, 60m, 42m, "PUR-PARTIAL");

        var result = await DetailAsync(order.Id);

        Assert.True(result.Has(PurchaseReconciliationFlags.PartiallyReceived));
        Assert.Equal(40m, result.PendingReceiptQuantity);
        Assert.Equal(0m, result.PendingInvoiceQuantity);
    }

    [Fact]
    public async Task FullyReceivedButPartlyPurchased_ShowsPendingInvoice()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 100m, "GRN-100");
        await AddPurchaseAsync(order, 60m, 42m, "PUR-60");

        var result = await DetailAsync(order.Id);

        Assert.Equal(40m, result.PendingInvoiceQuantity);
        Assert.True(result.Has(PurchaseReconciliationFlags.PendingPurchase));
        Assert.False(result.Has(PurchaseReconciliationFlags.FullyReconciled));
    }

    [Fact]
    public async Task PartialReceiptAndPartlyPurchased_SeparatesBothPendingQuantities()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 60m, "GRN-60");
        await AddPurchaseAsync(order, 40m, 42m, "PUR-40");

        var result = await DetailAsync(order.Id);

        Assert.Equal(40m, result.PendingReceiptQuantity);
        Assert.Equal(20m, result.PendingInvoiceQuantity);
    }

    [Fact]
    public async Task MultipleReceiptsAndPurchases_AggregateAcrossAllDocuments()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 60m, "GRN-1");
        await AddReceiptAsync(order, item, 30m, "GRN-2");
        await AddPurchaseAsync(order, 60m, 42m, "PUR-1");
        await AddPurchaseAsync(order, 20m, 42m, "PUR-2");

        var result = await DetailAsync(order.Id);

        Assert.Equal(90m, result.ReceivedQuantity);
        Assert.Equal(80m, result.PurchasedQuantity);
        Assert.Equal(10m, result.PendingReceiptQuantity);
        Assert.Equal(10m, result.PendingInvoiceQuantity);
        Assert.Equal(2, result.GoodsReceipts.Count);
        Assert.Equal(2, result.Purchases.Count);
    }

    [Fact]
    public async Task OverInvoiceAndOverReceipt_AreVisibleExceptionsWithoutCorrection()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 105m, "GRN-IMPORT-105");
        await AddPurchaseAsync(order, 110m, 42m, "PUR-IMPORT-110");

        var result = await DetailAsync(order.Id);
        var line = Assert.Single(result.Lines);

        Assert.Equal(5m, line.OverReceivedQuantity);
        Assert.Equal(5m, line.OverInvoicedQuantity);
        Assert.True(result.Has(PurchaseReconciliationFlags.OverReceived));
        Assert.True(result.Has(PurchaseReconciliationFlags.OverInvoiced));
        Assert.True(result.Has(PurchaseReconciliationFlags.Exception));
    }

    [Fact]
    public async Task PriceVariance_UsesWeightedActualCostAndDoesNotRewriteExpectedCost()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 100m, "GRN-PRICE");
        await AddPurchaseAsync(order, 40m, 42m, "PUR-PRICE-1");
        await AddPurchaseAsync(order, 60m, 44m, "PUR-PRICE-2");

        var line = Assert.Single((await DetailAsync(order.Id)).Lines);

        Assert.Equal(43.2m, line.ActualUnitCost);
        Assert.Equal(1.2m, line.UnitCostVariance);
        Assert.Equal(2.8571428571428571428571428600m, line.UnitCostVariancePercent);
        Assert.Equal(120m, line.TotalVariance);
        Assert.Equal(42m, (await _fixture.Context.PurchaseOrderItems.FindAsync(item.Id))!.UnitCost);
    }

    [Fact]
    public async Task PriceVariance_ExactScenarioShowsUnitPercentAndTotalVariance()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 100m, "GRN-EXACT-PRICE");
        await AddPurchaseAsync(order, 100m, 43m, "PUR-EXACT-PRICE");

        var line = Assert.Single((await DetailAsync(order.Id)).Lines);

        Assert.Equal(42m, line.ExpectedUnitCost);
        Assert.Equal(43m, line.ActualUnitCost);
        Assert.Equal(1m, line.UnitCostVariance);
        Assert.Equal(2.3809523809523809523809523800m, line.UnitCostVariancePercent);
        Assert.Equal(100m, line.TotalVariance);
    }

    [Fact]
    public async Task DecimalQuantitiesRemainInTheirAuthoritativeUnit()
    {
        _product.Unit = UnitOfMeasure.Kilogram;
        await _fixture.Context.SaveChangesAsync();
        var (order, item) = await CreateOrderAsync(10.5m, 42m);
        await AddReceiptAsync(order, item, 6.25m, "GRN-DECIMAL");
        await AddPurchaseAsync(order, 5.75m, 42m, "PUR-DECIMAL");

        var line = Assert.Single((await DetailAsync(order.Id)).Lines);

        Assert.Equal("Kilogram", line.Unit);
        Assert.Equal(4.25m, line.PendingReceiptQuantity);
        Assert.Equal(0.5m, line.PendingInvoiceQuantity);
    }

    [Fact]
    public async Task CancelledReceiptDoesNotContributeToDerivedQuantities()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 40m, "GRN-CANCELLED");
        (await _fixture.Context.GoodsReceipts.SingleAsync(x => x.GoodsReceiptNumber == "GRN-CANCELLED")).Status = GoodsReceiptStatus.Cancelled;
        await _fixture.Context.SaveChangesAsync();

        var result = await DetailAsync(order.Id);

        Assert.Equal(0m, result.ReceivedQuantity);
        Assert.Equal(0m, result.PurchasedQuantity);
        Assert.Equal(100m, result.PendingReceiptQuantity);
        Assert.True(result.Has(PurchaseReconciliationFlags.AwaitingReceipt));
    }

    [Fact]
    public async Task TaxVariance_UsesStoredPoAndPurchaseTaxValues()
    {
        _product.GstRatePercent = 5m;
        await _fixture.Context.SaveChangesAsync();
        var (order, item) = await CreateOrderAsync(100m, 42m, 5m);
        await AddReceiptAsync(order, item, 100m, "GRN-TAX");
        await AddPurchaseAsync(order, 100m, 43m, "PUR-TAX", 5m);

        var result = await DetailAsync(order.Id);
        var line = Assert.Single(result.Lines);

        Assert.Equal(210m, line.ExpectedTax);
        Assert.Equal(215m, line.ActualTax);
        Assert.Equal(5m, line.TaxVariance);
        Assert.True(result.Has(PurchaseReconciliationFlags.TaxMismatch));
    }

    [Fact]
    public async Task DirectPurchase_IsNotAReconciliationRecordOrException()
    {
        await AddDirectPurchaseAsync(20m, 42m, "PUR-DIRECT");

        var result = await _service.SearchAsync(new PurchaseReconciliationQuery(), _ownerId);

        Assert.Empty(result.Records);
        Assert.Equal(0, result.Metrics.Exceptions);
    }

    [Fact]
    public async Task HistoricalSnapshotsRemainReadableAfterMasterDataChanges()
    {
        var (order, item) = await CreateOrderAsync(10m, 42m);
        await AddReceiptAsync(order, item, 10m, "GRN-HISTORY");
        await AddPurchaseAsync(order, 10m, 42m, "PUR-HISTORY");
        _product.Name = "Renamed Product";
        _product.Sku = "CHANGED-SKU";
        _product.IsActive = false;
        _supplier.Name = "Renamed Supplier";
        _fixture.Context.ProductBarcodes.Add(new ProductBarcode
        {
            Product = _product, Value = "RETIRED", NormalizedValue = "RETIRED", IsActive = false,
        });
        await _fixture.Context.SaveChangesAsync();

        var result = await DetailAsync(order.Id);

        Assert.Equal("Original Supplier", result.SupplierName);
        Assert.Equal("Original Product", result.Lines.Single().ProductName);
        Assert.Equal("ORIGINAL-SKU", result.Lines.Single().Sku);
    }

    [Fact]
    public async Task SearchMatchesPoGrnPurchaseAndSupplierWithoutNPlusOneMutation()
    {
        var (order, item) = await CreateOrderAsync(10m, 42m, number: "PO-SEARCH-ME");
        await AddReceiptAsync(order, item, 10m, "GRN-SEARCH-ME");
        await AddPurchaseAsync(order, 10m, 42m, "PUR-SEARCH-ME");

        foreach (var search in new[] { "PO-SEARCH", "GRN-SEARCH", "PUR-SEARCH", "Original Supplier" })
        {
            var result = await _service.SearchAsync(
                new PurchaseReconciliationQuery { SearchText = search }, _ownerId);
            Assert.Single(result.Records);
        }
    }

    [Fact]
    public async Task OpeningAndRefreshingReconciliation_IsStrictlyReadOnly()
    {
        var (order, item) = await CreateOrderAsync(100m, 42m);
        await AddReceiptAsync(order, item, 60m, "GRN-READONLY");
        await AddPurchaseAsync(order, 60m, 42m, "PUR-READONLY");
        var before = await FingerprintAsync(order.Id);

        await DetailAsync(order.Id);
        await DetailAsync(order.Id);

        var after = await FingerprintAsync(order.Id);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ServiceEnforcesPurchasesPermission()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.SearchAsync(new PurchaseReconciliationQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetByPurchaseOrderIdAsync(1, null));
    }

    private async Task<(PurchaseOrder Order, PurchaseOrderItem Item)> CreateOrderAsync(
        decimal quantity,
        decimal cost,
        decimal gstRate = 0m,
        string? number = null)
    {
        var priced = PurchaseGstCalculationService.Shared.Calculate(
        [
            new PurchaseLine
            {
                ProductId = _product.Id, Quantity = quantity, UnitPrice = cost,
                DiscountPercent = 0m, PricingType = PricingType.Exclusive, GstRatePercent = gstRate,
            },
        ]);
        var line = priced.Lines.Single();
        var order = new PurchaseOrder
        {
            PurchaseOrderNumber = number ?? $"PO-REC-{Guid.NewGuid():N}",
            Supplier = _supplier, SupplierNameSnapshot = "Original Supplier",
            SupplierCodeSnapshot = _supplier.SupplierCode, Status = PurchaseOrderStatus.Submitted,
            CreatedByUserId = _ownerId, SubTotal = priced.SubTotal, DiscountTotal = priced.DiscountTotal,
            TaxableTotal = priced.TaxableTotal, TaxTotal = priced.TaxTotal,
            RoundOffAmount = priced.RoundOffAmount, GrandTotal = priced.GrandTotal,
        };
        var item = new PurchaseOrderItem
        {
            PurchaseOrder = order, Product = _product, ProductNameSnapshot = "Original Product",
            ProductCodeSnapshot = _product.ProductCode, SkuSnapshot = "ORIGINAL-SKU",
            UnitSnapshot = _product.Unit.ToString(), PricingTypeSnapshot = PricingType.Exclusive,
            GstRatePercentSnapshot = gstRate, OrderedQuantity = quantity, UnitCost = cost,
            DiscountAmount = line.DiscountAmount, TaxableAmount = line.TaxableAmount,
            GstAmount = line.GstAmount, LineTotal = line.LineTotal,
        };
        _fixture.Context.Add(item);
        await _fixture.Context.SaveChangesAsync();
        return (order, item);
    }

    private async Task AddReceiptAsync(
        PurchaseOrder order,
        PurchaseOrderItem item,
        decimal quantity,
        string number)
    {
        var receipt = new GoodsReceipt
        {
            GoodsReceiptNumber = number, PurchaseOrderId = order.Id, SupplierId = _supplier.Id,
            SupplierNameSnapshot = "Original Supplier", SupplierCodeSnapshot = _supplier.SupplierCode,
            Status = GoodsReceiptStatus.Completed, CreatedByUserId = _ownerId,
            CompletedByUserId = _ownerId, CompletedAtUtc = DateTime.UtcNow,
        };
        receipt.Items.Add(new GoodsReceiptItem
        {
            PurchaseOrderItemId = item.Id, ProductId = _product.Id,
            ProductNameSnapshot = "Original Product", ProductCodeSnapshot = _product.ProductCode,
            SkuSnapshot = "ORIGINAL-SKU", UnitSnapshot = _product.Unit,
            OrderedQuantitySnapshot = item.OrderedQuantity, ReceivedQuantity = quantity,
        });
        _fixture.Context.GoodsReceipts.Add(receipt);
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task AddPurchaseAsync(
        PurchaseOrder order,
        decimal quantity,
        decimal cost,
        string number,
        decimal gstRate = 0m)
    {
        var priced = PurchaseGstCalculationService.Shared.Calculate(
        [
            new PurchaseLine
            {
                ProductId = _product.Id, Quantity = quantity, UnitPrice = cost,
                DiscountPercent = 0m, PricingType = PricingType.Exclusive, GstRatePercent = gstRate,
            },
        ]);
        var line = priced.Lines.Single();
        var purchase = new Purchase
        {
            PurchaseNumber = number, PurchaseOrderId = order.Id, SupplierId = _supplier.Id,
            Status = PurchaseStatus.Completed, SubTotal = priced.SubTotal,
            DiscountTotal = priced.DiscountTotal, TaxableTotal = priced.TaxableTotal,
            TaxTotal = priced.TaxTotal, RoundOffAmount = priced.RoundOffAmount,
            GrandTotal = priced.GrandTotal, OutstandingAmount = priced.GrandTotal,
        };
        purchase.Items.Add(new PurchaseItem
        {
            ProductId = _product.Id, ProductNameSnapshot = "Original Product",
            ProductCodeSnapshot = _product.ProductCode, SkuSnapshot = "ORIGINAL-SKU",
            UnitSnapshot = _product.Unit.ToString(), Quantity = quantity,
            PurchasePriceSnapshot = cost, GstRatePercentSnapshot = gstRate,
            DiscountAmount = line.DiscountAmount, TaxableAmount = line.TaxableAmount,
            GstAmount = line.GstAmount, LineTotal = line.LineTotal,
        });
        _fixture.Context.Purchases.Add(purchase);
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task AddDirectPurchaseAsync(decimal quantity, decimal cost, string number)
    {
        var purchase = new Purchase
        {
            PurchaseNumber = number, SupplierId = _supplier.Id, Status = PurchaseStatus.Completed,
            SubTotal = quantity * cost, TaxableTotal = quantity * cost, GrandTotal = quantity * cost,
            OutstandingAmount = quantity * cost,
        };
        purchase.Items.Add(new PurchaseItem
        {
            ProductId = _product.Id, ProductNameSnapshot = _product.Name,
            ProductCodeSnapshot = _product.ProductCode, UnitSnapshot = _product.Unit.ToString(),
            Quantity = quantity, PurchasePriceSnapshot = cost, TaxableAmount = quantity * cost,
            LineTotal = quantity * cost,
        });
        _fixture.Context.Purchases.Add(purchase);
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task<PurchaseReconciliationRecord> DetailAsync(int orderId) =>
        await _service.GetByPurchaseOrderIdAsync(orderId, _ownerId)
        ?? throw new InvalidOperationException("Expected reconciliation record.");

    private async Task<string> FingerprintAsync(int orderId)
    {
        var inventory = await _fixture.Context.Inventories.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.QuantityOnHand }).ToListAsync();
        return string.Join('|',
            await _fixture.Context.PurchaseOrders.AsNoTracking().CountAsync(),
            await _fixture.Context.GoodsReceipts.AsNoTracking().CountAsync(),
            await _fixture.Context.Purchases.AsNoTracking().CountAsync(),
            await _fixture.Context.PurchaseItems.AsNoTracking().CountAsync(),
            await _fixture.Context.StockMovements.AsNoTracking().CountAsync(),
            await _fixture.Context.AuditLogs.AsNoTracking().CountAsync(),
            await _fixture.Context.Suppliers.AsNoTracking().Where(x => x.Id == _supplier.Id)
                .Select(x => x.OutstandingBalance).SingleAsync(),
            await _fixture.Context.PurchaseOrders.AsNoTracking().Where(x => x.Id == orderId)
                .Select(x => x.Status).SingleAsync(),
            string.Join(',', inventory.Select(x => $"{x.Id}:{x.QuantityOnHand}")));
    }

    public void Dispose() => _fixture.Dispose();
}
