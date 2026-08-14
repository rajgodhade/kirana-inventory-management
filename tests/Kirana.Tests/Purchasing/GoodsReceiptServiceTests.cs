using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Purchasing;

public sealed class GoodsReceiptServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly int _ownerId;
    private readonly Supplier _supplier;
    private readonly Product _piece;
    private readonly Product _weight;
    private readonly PurchaseOrder _order;
    private readonly GoodsReceiptService _service;

    public GoodsReceiptServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _supplier = new Supplier { SupplierCode = "SUP-GRN", Name = "GRN Supplier", IsActive = true };
        _piece = new Product { ProductCode = "PRD-PIECE", Name = "Butter", Unit = UnitOfMeasure.Piece, PurchasePrice = 42m, Mrp = 50m, SellingPrice = 48m, GstRatePercent = 5m, PricingType = PricingType.Inclusive, IsActive = true };
        _weight = new Product { ProductCode = "PRD-KG", Name = "Rice", Unit = UnitOfMeasure.Kilogram, PurchasePrice = 70m, Mrp = 85m, SellingPrice = 80m, GstRatePercent = 5m, PricingType = PricingType.Inclusive, IsActive = true };
        _fixture.Context.AddRange(_supplier, _piece, _weight);
        _fixture.Context.Inventories.AddRange(new Inventory { Product = _piece, QuantityOnHand = 100m }, new Inventory { Product = _weight, QuantityOnHand = 25m });
        _order = new PurchaseOrder
        {
            PurchaseOrderNumber = "PO-TEST-000001", Supplier = _supplier,
            SupplierNameSnapshot = _supplier.Name, SupplierCodeSnapshot = _supplier.SupplierCode,
            Status = PurchaseOrderStatus.Submitted, CreatedByUserId = _ownerId,
        };
        _order.Items.Add(OrderItem(_piece, 100m, 42m));
        _order.Items.Add(OrderItem(_weight, 12.5m, 70m));
        _fixture.Context.PurchaseOrders.Add(_order);
        _fixture.Context.SaveChanges();
        _service = CreateService(_fixture.Context);
    }

    [Fact]
    public async Task CreateDraft_CapturesReferencesAndSnapshots_WithoutPostingAnything()
    {
        var beforeStock = await StockAsync(_piece.Id);
        var receipt = await _service.CreateDraftAsync(Request((_order.Items.First().Id, 60m)));
        Assert.StartsWith("GRN-", receipt.GoodsReceiptNumber);
        Assert.Equal(_order.Id, receipt.PurchaseOrderId);
        Assert.Equal(_supplier.Name, receipt.SupplierNameSnapshot);
        Assert.Equal("Butter", receipt.Items.Single().ProductNameSnapshot);
        Assert.Equal(60m, receipt.Items.Single().ReceivedQuantity);
        Assert.Equal(beforeStock, await StockAsync(_piece.Id));
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Empty(await _fixture.Context.Purchases.ToListAsync());
        Assert.Equal(0m, (await _fixture.Context.Suppliers.FindAsync(_supplier.Id))!.OutstandingBalance);
    }

    [Fact]
    public async Task PartialThenFinalReceipt_UpdatesRemainingAndPurchaseOrderStatus()
    {
        var pieceItem = _order.Items.First(i => i.ProductId == _piece.Id);
        await CompleteAsync((pieceItem.Id, 60m));
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await _fixture.Context.PurchaseOrders.FindAsync(_order.Id))!.Status);
        var preview = await _service.GetReceiptPreviewAsync(_order.Id, _ownerId);
        Assert.Equal(40m, preview.Lines.Single(l => l.ProductId == _piece.Id).RemainingQuantity);
        var weightItem = preview.Lines.Single(l => l.ProductId == _weight.Id);
        var remainingPiece = preview.Lines.Single(l => l.ProductId == _piece.Id);
        var second = await _service.CreateDraftAsync(Request((remainingPiece.PurchaseOrderItemId, 40m), (weightItem.PurchaseOrderItemId, 12.5m)));
        await _service.CompleteAsync(second.Id, _ownerId);
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(PurchaseOrderStatus.Completed, (await _fixture.Context.PurchaseOrders.FindAsync(_order.Id))!.Status);
        Assert.Equal(100m, await _fixture.Context.GoodsReceiptItems.Where(i => i.ProductId == _piece.Id && i.GoodsReceipt.Status == GoodsReceiptStatus.Completed).SumAsync(i => i.ReceivedQuantity));
    }

    [Fact]
    public async Task OverReceiving_IsRejectedAndLeavesDraftAndOrderUnchanged()
    {
        var item = _order.Items.First(i => i.ProductId == _piece.Id);
        await CompleteAsync((item.Id, 60m));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateDraftAsync(Request((item.Id, 45m))));
        Assert.Contains("only 40", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await _fixture.Context.GoodsReceipts.CountAsync());
        Assert.Equal(100m, await StockAsync(_piece.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ZeroOrNegativeOnlyReceipt_IsRejected(decimal quantity)
    {
        var item = _order.Items.First().Id;
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _service.CreateDraftAsync(Request((item, quantity))));
        Assert.Empty(await _fixture.Context.GoodsReceipts.ToListAsync());
    }

    [Fact]
    public async Task PieceRejectsDecimal_ButWeightAcceptsDecimal()
    {
        var piece = _order.Items.First(i => i.ProductId == _piece.Id).Id;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDraftAsync(Request((piece, 1.5m))));
        var weight = _order.Items.First(i => i.ProductId == _weight.Id).Id;
        var receipt = await _service.CreateDraftAsync(Request((weight, 8.75m)));
        Assert.Equal(8.75m, receipt.Items.Single().ReceivedQuantity);
    }

    [Fact]
    public async Task DraftMayBeCancelled_CompletedIsImmutable()
    {
        var receipt = await _service.CreateDraftAsync(Request((_order.Items.First().Id, 10m)));
        var cancelled = await _service.CancelAsync(new CancelGoodsReceiptRequest { GoodsReceiptId = receipt.Id, Reason = "Delivery rejected", PerformedByUserId = _ownerId });
        Assert.Equal(GoodsReceiptStatus.Cancelled, cancelled.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CompleteAsync(receipt.Id, _ownerId));

        var completed = await CompleteAsync((_order.Items.First().Id, 10m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CancelAsync(new CancelGoodsReceiptRequest { GoodsReceiptId = completed.Id, Reason = "No", PerformedByUserId = _ownerId }));
    }

    [Fact]
    public async Task NonEligiblePurchaseOrdersCannotReceive()
    {
        _order.Status = PurchaseOrderStatus.Draft; await _fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetReceiptPreviewAsync(_order.Id, _ownerId));
    }

    [Fact]
    public async Task ActiveAlternateBarcodeIsCaptured_RetiredBarcodeIsRejected()
    {
        var active = new ProductBarcode { Product = _piece, Value = "ALT-PIECE", NormalizedValue = BarcodeNormalizer.Normalize("ALT-PIECE"), IsActive = true, IsPrimary = true };
        var retired = new ProductBarcode { Product = _piece, Value = "OLD-PIECE", NormalizedValue = BarcodeNormalizer.Normalize("OLD-PIECE"), IsActive = false };
        _fixture.Context.ProductBarcodes.AddRange(active, retired); await _fixture.Context.SaveChangesAsync();
        var item = _order.Items.First(i => i.ProductId == _piece.Id).Id;
        var valid = await _service.CreateDraftAsync(new CreateGoodsReceiptDraftRequest { PurchaseOrderId = _order.Id, PerformedByUserId = _ownerId, Lines = [new GoodsReceiptLineInput { PurchaseOrderItemId = item, ReceivedQuantity = 1m, Barcode = "alt-piece" }] });
        Assert.Equal("ALT-PIECE", valid.Items.Single().BarcodeSnapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateDraftAsync(new CreateGoodsReceiptDraftRequest { PurchaseOrderId = _order.Id, PerformedByUserId = _ownerId, Lines = [new GoodsReceiptLineInput { PurchaseOrderItemId = item, ReceivedQuantity = 1m, Barcode = "OLD-PIECE" }] }));
    }

    [Fact]
    public async Task UnauthorizedOperationsFailAtServiceLayer()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetReceiptPreviewAsync(_order.Id, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateDraftAsync(new CreateGoodsReceiptDraftRequest
        {
            PurchaseOrderId = _order.Id,
            Lines =
            [
                new GoodsReceiptLineInput
                {
                    PurchaseOrderItemId = _order.Items.First().Id,
                    ReceivedQuantity = 1m,
                },
            ],
        }));
    }

    [Fact]
    public async Task LifecycleWritesAuditEntries()
    {
        var receipt = await CompleteAsync((_order.Items.First().Id, 5m));
        var actions = await _fixture.Context.AuditLogs.Where(a => a.Entity == nameof(GoodsReceipt) && a.EntityId == receipt.Id.ToString()).Select(a => a.Action).ToListAsync();
        Assert.Contains("GoodsReceiptCreated", actions);
        Assert.Contains("GoodsReceiptCompleted", actions);
    }

    [Fact]
    public async Task SearchFindsByGrnPoAndSupplier()
    {
        var receipt = await CompleteAsync((_order.Items.First().Id, 5m));
        Assert.Single(await _service.SearchAsync(new GoodsReceiptSearchQuery { SearchText = receipt.GoodsReceiptNumber }, _ownerId));
        Assert.Single(await _service.SearchAsync(new GoodsReceiptSearchQuery { SearchText = _order.PurchaseOrderNumber }, _ownerId));
        Assert.Single(await _service.SearchAsync(new GoodsReceiptSearchQuery { SupplierId = _supplier.Id, Status = GoodsReceiptStatus.Completed }, _ownerId));
    }

    [Fact]
    public async Task PurchaseFromGrn_UsesActualPrice_PostsStockAndPayableExactlyOnce()
    {
        var item = _order.Items.First(i => i.ProductId == _piece.Id);
        var receipt = await CompleteAsync((item.Id, 60m));
        var prefill = await _service.GetPurchasePrefillAsync(receipt.Id, _ownerId);
        Assert.Equal(60m, prefill.Lines.Single().Quantity);
        Assert.Equal(42m, prefill.Lines.Single().UnitPrice);

        var purchaseService = new PurchaseService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context), new PurchaseGstCalculationService());
        var purchase = await purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = _supplier.Id, GoodsReceiptId = receipt.Id, PurchaseOrderId = _order.Id,
            SupplierInvoiceNumber = "INV-GRN-1", CreatedByUserId = _ownerId,
            Lines = [new PurchaseLineInput { ProductId = _piece.Id, Quantity = 60m, UnitPrice = 43m, PricingType = PricingType.Inclusive }],
        });
        Assert.Equal(43m, purchase.Items.Single().PurchasePriceSnapshot);
        Assert.Equal(160m, await StockAsync(_piece.Id));
        Assert.Single(await _fixture.Context.StockMovements.Where(m => m.ReferenceId == purchase.PurchaseNumber).ToListAsync());
        Assert.Equal(60m, await _fixture.Context.StockMovements.Where(m => m.ReferenceId == purchase.PurchaseNumber).SumAsync(m => m.QuantityChange));
        Assert.Equal(purchase.GrandTotal, (await _fixture.Context.Suppliers.FindAsync(_supplier.Id))!.OutstandingBalance);
        Assert.Equal(receipt.Id, purchase.GoodsReceiptId);
        Assert.Equal(_order.Id, purchase.PurchaseOrderId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = _supplier.Id, GoodsReceiptId = receipt.Id, PurchaseOrderId = _order.Id, CreatedByUserId = _ownerId,
            Lines = [new PurchaseLineInput { ProductId = _piece.Id, Quantity = 60m, UnitPrice = 43m }],
        }));
        Assert.Equal(160m, await StockAsync(_piece.Id));
    }

    [Fact]
    public async Task PurchaseFromGrn_RejectsChangedReceivedQuantity()
    {
        var item = _order.Items.First(i => i.ProductId == _piece.Id);
        var receipt = await CompleteAsync((item.Id, 60m));
        var purchaseService = new PurchaseService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = _supplier.Id, GoodsReceiptId = receipt.Id, PurchaseOrderId = _order.Id, CreatedByUserId = _ownerId,
            Lines = [new PurchaseLineInput { ProductId = _piece.Id, Quantity = 59m, UnitPrice = 43m }],
        }));
        Assert.Equal(100m, await StockAsync(_piece.Id));
    }

    [Fact]
    public async Task CompletionRereadsFreshQuantitiesFromSeparateContext()
    {
        var item = _order.Items.First(i => i.ProductId == _piece.Id);
        var staleDraft = await _service.CreateDraftAsync(Request((item.Id, 60m)));
        using var other = SeparateContext();
        var otherService = CreateService(other);
        var otherDraft = await otherService.CreateDraftAsync(new CreateGoodsReceiptDraftRequest { PurchaseOrderId = _order.Id, PerformedByUserId = _ownerId, Lines = [new GoodsReceiptLineInput { PurchaseOrderItemId = item.Id, ReceivedQuantity = 60m }] });
        await otherService.CompleteAsync(otherDraft.Id, _ownerId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CompleteAsync(staleDraft.Id, _ownerId));
        Assert.Contains("Only 40", ex.Message, StringComparison.OrdinalIgnoreCase);
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(GoodsReceiptStatus.Draft, (await _fixture.Context.GoodsReceipts.FindAsync(staleDraft.Id))!.Status);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await _fixture.Context.PurchaseOrders.FindAsync(_order.Id))!.Status);
    }

    [Fact]
    public async Task AuditFailureRollsBackReceiptAndPurchaseOrderStatusAtomically()
    {
        var draft = await _service.CreateDraftAsync(Request((_order.Items.First().Id, 10m)));
        var failing = new GoodsReceiptService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), new ThrowingAuditLogger(), new PermissionEnforcer(_fixture.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CompleteAsync(draft.Id, _ownerId));
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(GoodsReceiptStatus.Draft, (await _fixture.Context.GoodsReceipts.FindAsync(draft.Id))!.Status);
        Assert.Equal(PurchaseOrderStatus.Submitted, (await _fixture.Context.PurchaseOrders.FindAsync(_order.Id))!.Status);
    }

    private PurchaseOrderItem OrderItem(Product product, decimal quantity, decimal cost) => new()
    {
        Product = product, ProductNameSnapshot = product.Name, ProductCodeSnapshot = product.ProductCode,
        UnitSnapshot = product.Unit.ToString(), PricingTypeSnapshot = product.PricingType,
        GstRatePercentSnapshot = product.GstRatePercent ?? 0m, OrderedQuantity = quantity, UnitCost = cost,
    };

    private CreateGoodsReceiptDraftRequest Request(params (int itemId, decimal quantity)[] lines) => new()
    {
        PurchaseOrderId = _order.Id, PerformedByUserId = _ownerId,
        Lines = lines.Select(x => new GoodsReceiptLineInput { PurchaseOrderItemId = x.itemId, ReceivedQuantity = x.quantity }).ToList(),
    };

    private async Task<GoodsReceipt> CompleteAsync(params (int itemId, decimal quantity)[] lines)
    {
        var draft = await _service.CreateDraftAsync(Request(lines));
        return await _service.CompleteAsync(draft.Id, _ownerId);
    }

    private GoodsReceiptService CreateService(KiranaDbContext context) => new(context, new EfSequenceGenerator(context), new EfAuditLogger(context), new PermissionEnforcer(context));
    private KiranaDbContext SeparateContext() => new(new DbContextOptionsBuilder<KiranaDbContext>().UseSqlite(_fixture.Context.Database.GetDbConnection()).Options);
    private Task<decimal> StockAsync(int productId) => _fixture.Context.Inventories.AsNoTracking().Where(i => i.ProductId == productId).Select(i => i.QuantityOnHand).SingleAsync();
    public void Dispose() => _fixture.Dispose();

    private sealed class ThrowingAuditLogger : IAuditLogger
    {
        public Task RecordAsync(int? userId, string action, string entity, string? entityId = null, string? previousValue = null, string? newValue = null, string? reason = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected audit failure.");
    }
}
