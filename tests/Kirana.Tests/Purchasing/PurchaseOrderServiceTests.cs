using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Purchasing;

public sealed class PurchaseOrderServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly PurchaseOrderService _sut;
    private readonly SupplierService _supplierService;
    private readonly int _ownerId;

    public PurchaseOrderServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        _sut = new PurchaseOrderService(_fixture.Context, sequence, audit, permissions, PurchaseGstCalculationService.Shared);
        _supplierService = new SupplierService(_fixture.Context, sequence, audit, permissions);
    }

    private Task<Supplier> SeedSupplierAsync() => _supplierService.CreateAsync(new CreateSupplierRequest { Name = "ABC Distributors", Phone = "9876543210", PerformedByUserId = _ownerId });
    private async Task<Product> SeedProductAsync(UnitOfMeasure unit = UnitOfMeasure.Piece, decimal stock = 7)
    {
        var product = new Product { ProductCode = "PRD-PO-001", Name = "Amul Butter", Sku = "AMUL-1", Unit = unit,
            PurchasePrice = 42, Mrp = 50, SellingPrice = 48, GstRatePercent = 5, PricingType = PricingType.Exclusive, IsActive = true };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private SavePurchaseOrderDraftRequest Request(int supplierId, int productId, decimal quantity = 10, decimal cost = 42) => new()
    {
        SupplierId = supplierId, PerformedByUserId = _ownerId, Notes = "Deliver Monday",
        Lines = [new PurchaseOrderLineInput { ProductId = productId, OrderedQuantity = quantity, UnitCost = cost, DiscountPercent = 10 }],
    };

    [Fact]
    public async Task CreateDraft_GeneratesSequentialHumanReadableNumbers()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var first = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        var second = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        Assert.Equal("PO-000001", first.PurchaseOrderNumber);
        Assert.Equal("PO-000002", second.PurchaseOrderNumber);
    }

    [Fact]
    public async Task CreateDraft_UsesSharedPurchasePricingAndSnapshotsMasterData()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id, 100, 42));
        Assert.Equal(4200, order.SubTotal); Assert.Equal(420, order.DiscountTotal);
        Assert.Equal(189, order.TaxTotal); Assert.Equal(3969, order.GrandTotal);
        Assert.Equal("ABC Distributors", order.SupplierNameSnapshot);
        Assert.Equal("Amul Butter", Assert.Single(order.Items).ProductNameSnapshot);
    }

    [Fact]
    public async Task CreateAndSubmit_DoNotPostInventoryPurchaseMovementOrSupplierBalance()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync(stock: 7);
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        await _sut.SubmitAsync(order.Id, _ownerId);
        Assert.Equal(7, (await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id)).QuantityOnHand);
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Empty(await _fixture.Context.Purchases.ToListAsync());
        Assert.Empty(await _fixture.Context.SupplierPayments.ToListAsync());
        Assert.Equal(0, (await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id)).OutstandingBalance);
    }

    [Fact]
    public async Task Submit_RecordsTimestampAndUserAndMakesOrderImmutable()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        var submitted = await _sut.SubmitAsync(order.Id, _ownerId);
        Assert.Equal(PurchaseOrderStatus.Submitted, submitted.Status); Assert.NotNull(submitted.SubmittedAtUtc); Assert.Equal(_ownerId, submitted.SubmittedByUserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateDraftAsync(order.Id, Request(supplier.Id, product.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitAsync(order.Id, _ownerId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_PreservesOrderAndDoesNotPostFinancialOrStockEffects(bool submitFirst)
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        if (submitFirst) await _sut.SubmitAsync(order.Id, _ownerId);
        var cancelled = await _sut.CancelAsync(new CancelPurchaseOrderRequest { PurchaseOrderId = order.Id, Reason = "Supplier unavailable", PerformedByUserId = _ownerId });
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status); Assert.Equal("Supplier unavailable", cancelled.CancellationReason); Assert.NotNull(cancelled.CancelledAtUtc);
        Assert.Empty(_fixture.Context.Purchases); Assert.Empty(_fixture.Context.StockMovements);
        Assert.Equal(0, (await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id)).OutstandingBalance);
    }

    [Fact]
    public async Task CreateDraft_RejectsFractionalQuantityForWholeUnit()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateDraftAsync(Request(supplier.Id, product.Id, 1.5m)));
    }

    [Fact]
    public async Task CreateDraft_AllowsFractionalQuantityForDecimalUnit()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync(UnitOfMeasure.Kilogram);
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id, 12.5m));
        Assert.Equal(12.5m, Assert.Single(order.Items).OrderedQuantity);
    }

    [Fact]
    public async Task Submit_RejectsInactiveSupplierAndProduct()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        supplier.IsActive = false; await _fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitAsync(order.Id, _ownerId));
    }

    [Fact]
    public async Task UnauthorizedUserCannotCreateSubmitOrSearch()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync(); var cashier = await _fixture.SeedCashierAsync();
        var request = new SavePurchaseOrderDraftRequest { SupplierId = supplier.Id, PerformedByUserId = cashier.Id,
            Lines = [new PurchaseOrderLineInput { ProductId = product.Id, OrderedQuantity = 1, UnitCost = 10 }] };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateDraftAsync(request));
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.SubmitAsync(order.Id, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.SearchAsync(new PurchaseOrderSearchQuery(), cashier.Id));
    }

    [Fact]
    public async Task Search_FindsNumberSupplierAndFiltersStatus()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id)); await _sut.SubmitAsync(order.Id, _ownerId);
        Assert.Single(await _sut.SearchAsync(new PurchaseOrderSearchQuery { SearchText = "ABC" }, _ownerId));
        Assert.Single(await _sut.SearchAsync(new PurchaseOrderSearchQuery { SearchText = "PO-000001", Status = PurchaseOrderStatus.Submitted }, _ownerId));
        Assert.Empty(await _sut.SearchAsync(new PurchaseOrderSearchQuery { Status = PurchaseOrderStatus.Draft }, _ownerId));
    }

    [Fact]
    public async Task LifecycleEventsAreAudited()
    {
        var supplier = await SeedSupplierAsync(); var product = await SeedProductAsync();
        var order = await _sut.CreateDraftAsync(Request(supplier.Id, product.Id)); await _sut.SubmitAsync(order.Id, _ownerId);
        await _sut.CancelAsync(new CancelPurchaseOrderRequest { PurchaseOrderId = order.Id, Reason = "Test", PerformedByUserId = _ownerId });
        var actions = await _fixture.Context.AuditLogs.Where(a => a.Entity == nameof(PurchaseOrder)).Select(a => a.Action).ToListAsync();
        Assert.Contains("PurchaseOrderCreated", actions); Assert.Contains("PurchaseOrderSubmitted", actions); Assert.Contains("PurchaseOrderCancelled", actions);
    }

    public void Dispose() => _fixture.Dispose();
}
