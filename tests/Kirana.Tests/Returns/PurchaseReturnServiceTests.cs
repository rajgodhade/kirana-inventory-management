using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Returns;

/// <summary>
/// Purchase returns (PRD §34). Purchases go through the real <see cref="PurchaseService"/>, so
/// these tests prove the Phase 7 receive path and the Phase 9 return path agree about quantities,
/// batches, stock and the supplier's balance.
/// </summary>
public class PurchaseReturnServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly PurchaseReturnService _sut;
    private readonly PurchaseService _purchaseService;
    private readonly SupplierService _supplierService;
    private readonly int _ownerId;

    public PurchaseReturnServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _supplierService = new SupplierService(_fixture.Context, seq, audit, enforcer);
        _purchaseService = new PurchaseService(_fixture.Context, seq, audit, enforcer);
        _sut = new PurchaseReturnService(_fixture.Context, seq, audit, enforcer);
    }

    // ---------------------------------------------------------------- helpers

    private Task<Supplier> SeedSupplierAsync() =>
        _supplierService.CreateAsync(new CreateSupplierRequest { Name = "Return Supplier", PerformedByUserId = _ownerId });

    private async Task<Product> SeedProductAsync(bool tracksBatches = false, UnitOfMeasure unit = UnitOfMeasure.Piece)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Purchase Return Product",
            Unit = unit,
            PurchasePrice = 50,
            Mrp = 90,
            SellingPrice = 80,
            TracksBatches = tracksBatches,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 0 });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<Purchase> ReceiveAsync(Supplier supplier, Product product, decimal quantity, decimal price = 50, string? batch = null) =>
        _purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines =
            [
                new PurchaseLineInput
                {
                    ProductId = product.Id, Quantity = quantity, UnitPrice = price, BatchNumber = batch,
                },
            ],
            CreatedByUserId = _ownerId,
        });

    private async Task<decimal> StockAsync(int productId) =>
        (await _fixture.Context.Inventories.AsNoTracking().FirstAsync(i => i.ProductId == productId)).QuantityOnHand;

    private async Task<decimal> SupplierBalanceAsync(int supplierId) =>
        (await _fixture.Context.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId)).OutstandingBalance;

    private Task<PurchaseReturn> ReturnAsync(Purchase purchase, int purchaseItemId, decimal quantity, string? batch = null) =>
        _sut.ProcessReturnAsync(new CreatePurchaseReturnRequest
        {
            PurchaseId = purchase.Id,
            Lines = [new PurchaseReturnLineInput { PurchaseItemId = purchaseItemId, Quantity = quantity, BatchNumber = batch }],
            Reason = "Damaged in transit",
            ProcessedByUserId = _ownerId,
        });

    private async Task<int> FirstItemIdAsync(int purchaseId) =>
        (await _fixture.Context.PurchaseItems.AsNoTracking().FirstAsync(i => i.PurchaseId == purchaseId)).Id;

    // ---------------------------------------------------------------- full & partial

    [Fact]
    public async Task FullReturn_RemovesAllStockAndCreditsFullValue()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);

        Assert.Equal(10m, await StockAsync(product.Id));
        var balanceAfterPurchase = await SupplierBalanceAsync(supplier.Id);

        var itemId = await FirstItemIdAsync(purchase.Id);
        var purchaseReturn = await ReturnAsync(purchase, itemId, 10);

        Assert.Equal(0m, await StockAsync(product.Id));
        Assert.Equal(500m, purchaseReturn.TotalReturnAmount);
        Assert.Equal(balanceAfterPurchase - 500m, await SupplierBalanceAsync(supplier.Id));
        Assert.StartsWith("PRN-", purchaseReturn.ReturnNumber);
    }

    [Fact]
    public async Task PartialReturn_RemovesOnlyReturnedQuantity()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);
        var itemId = await FirstItemIdAsync(purchase.Id);

        var purchaseReturn = await ReturnAsync(purchase, itemId, 4);

        Assert.Equal(6m, await StockAsync(product.Id));
        Assert.Equal(200m, purchaseReturn.TotalReturnAmount);
    }

    [Fact]
    public async Task MultiplePartialReturns_AccumulateUpToTheReceivedQuantity()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);
        var itemId = await FirstItemIdAsync(purchase.Id);

        await ReturnAsync(purchase, itemId, 4);
        await ReturnAsync(purchase, itemId, 6);

        Assert.Equal(0m, await StockAsync(product.Id));

        var returnable = await _sut.GetReturnablePurchaseAsync(purchase.Id, _ownerId);
        Assert.True(returnable!.Lines.Single().IsFullyReturned);
    }

    [Fact]
    public async Task Return_CreatesAStockMovement()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);
        var itemId = await FirstItemIdAsync(purchase.Id);

        var purchaseReturn = await ReturnAsync(purchase, itemId, 3);

        var movement = await _fixture.Context.StockMovements.AsNoTracking()
            .SingleAsync(m => m.ReferenceType == nameof(PurchaseReturn));

        Assert.Equal(StockMovementType.PurchaseReturn, movement.MovementType);
        Assert.Equal(-3m, movement.QuantityChange);
        Assert.Equal(10m, movement.PreviousQuantity);
        Assert.Equal(7m, movement.NewQuantity);
        Assert.Equal(purchaseReturn.ReturnNumber, movement.ReferenceId);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public async Task Return_Throws_WhenExceedingReceivedQuantity()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 5);
        var itemId = await FirstItemIdAsync(purchase.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(purchase, itemId, 6));
        Assert.Contains("returnable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Return_Throws_WhenCumulativeReturnsExceedReceivedQuantity()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 5);
        var itemId = await FirstItemIdAsync(purchase.Id);

        await ReturnAsync(purchase, itemId, 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(purchase, itemId, 2));
    }

    [Fact]
    public async Task Return_Throws_WhenStockHasAlreadyBeenSoldOn()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);
        var itemId = await FirstItemIdAsync(purchase.Id);

        // Sell most of it, so the goods are no longer physically there to send back.
        var inventory = await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 2m;
        await _fixture.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(purchase, itemId, 8));
        Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Return_Throws_OnNonPositiveQuantity()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 5);
        var itemId = await FirstItemIdAsync(purchase.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => ReturnAsync(purchase, itemId, 0));
    }

    [Fact]
    public async Task Return_Throws_WhenLineBelongsToADifferentPurchase()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var first = await ReceiveAsync(supplier, product, 5);
        var second = await ReceiveAsync(supplier, product, 5);

        var otherItemId = (await _fixture.Context.PurchaseItems.AsNoTracking()
            .FirstAsync(i => i.PurchaseId == second.Id)).Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(first, otherItemId, 1));
    }

    [Fact]
    public async Task FailedReturn_LeavesStockAndSupplierBalanceUntouched()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 5);
        var itemId = await FirstItemIdAsync(purchase.Id);

        var stockBefore = await StockAsync(product.Id);
        var balanceBefore = await SupplierBalanceAsync(supplier.Id);
        var movementsBefore = await _fixture.Context.StockMovements.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(purchase, itemId, 99));

        Assert.Equal(stockBefore, await StockAsync(product.Id));
        Assert.Equal(balanceBefore, await SupplierBalanceAsync(supplier.Id));
        Assert.Equal(movementsBefore, await _fixture.Context.StockMovements.CountAsync());
        Assert.Empty(await _fixture.Context.PurchaseReturns.ToListAsync());
    }

    // ---------------------------------------------------------------- batches

    [Fact]
    public async Task Return_ReducesTheBatchItWasReceivedInto()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(tracksBatches: true);
        var purchase = await ReceiveAsync(supplier, product, 10, batch: "B-100");
        var itemId = await FirstItemIdAsync(purchase.Id);

        await ReturnAsync(purchase, itemId, 4);

        var batch = await _fixture.Context.ProductBatches.AsNoTracking().FirstAsync(b => b.ProductId == product.Id);
        Assert.Equal(6m, batch.Quantity);
    }

    [Fact]
    public async Task Return_NeverDrivesABatchNegative()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(tracksBatches: true);
        var purchase = await ReceiveAsync(supplier, product, 10, batch: "B-100");
        var itemId = await FirstItemIdAsync(purchase.Id);

        // Batch was partly consumed from elsewhere, but overall stock still covers the return.
        var batch = await _fixture.Context.ProductBatches.FirstAsync(b => b.ProductId == product.Id);
        batch.Quantity = 3m;
        await _fixture.Context.SaveChangesAsync();

        await ReturnAsync(purchase, itemId, 8);

        var updated = await _fixture.Context.ProductBatches.AsNoTracking().FirstAsync(b => b.ProductId == product.Id);
        Assert.Equal(0m, updated.Quantity);
    }

    // ---------------------------------------------------------------- integrity

    [Fact]
    public async Task Return_NeverAltersTheOriginalPurchase()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);

        var before = await _fixture.Context.Purchases.AsNoTracking()
            .Include(p => p.Items).FirstAsync(p => p.Id == purchase.Id);

        await ReturnAsync(purchase, before.Items.First().Id, 4);

        var after = await _fixture.Context.Purchases.AsNoTracking()
            .Include(p => p.Items).FirstAsync(p => p.Id == purchase.Id);

        Assert.Equal(before.PurchaseNumber, after.PurchaseNumber);
        Assert.Equal(before.GrandTotal, after.GrandTotal);
        Assert.Equal(before.Items.Single().Quantity, after.Items.Single().Quantity);
    }

    [Fact]
    public async Task ReturnLines_SnapshotTheOriginalPurchaseNotTheLiveProduct()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10, price: 50);
        var itemId = await FirstItemIdAsync(purchase.Id);

        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.Name = "Renamed";
        tracked.PurchasePrice = 999m;
        await _fixture.Context.SaveChangesAsync();

        var purchaseReturn = await ReturnAsync(purchase, itemId, 2);

        var line = purchaseReturn.Items.Single();
        Assert.Equal("Purchase Return Product", line.ProductNameSnapshot);
        Assert.Equal(50m, line.PurchasePriceSnapshot);
        Assert.Equal(100m, line.LineReturnAmount);
    }

    [Fact]
    public async Task SupplierBalance_NeverGoesNegative()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);
        var itemId = await FirstItemIdAsync(purchase.Id);

        // Settle the purchase in full first, then return goods against it.
        var tracked = await _fixture.Context.Suppliers.FirstAsync(s => s.Id == supplier.Id);
        tracked.OutstandingBalance = 0m;
        await _fixture.Context.SaveChangesAsync();

        await ReturnAsync(purchase, itemId, 10);

        Assert.Equal(0m, await SupplierBalanceAsync(supplier.Id));
    }

    // ---------------------------------------------------------------- lookup & audit

    [Fact]
    public async Task FindReturnablePurchases_ByPurchaseNumberAndSupplier()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 5);

        var byNumber = await _sut.FindReturnablePurchasesAsync(purchase.PurchaseNumber, _ownerId);
        var bySupplier = await _sut.FindReturnablePurchasesAsync("Return Supplier", _ownerId);

        Assert.Equal(purchase.Id, Assert.Single(byNumber).PurchaseId);
        Assert.Contains(bySupplier, p => p.PurchaseId == purchase.Id);
    }

    [Fact]
    public async Task ReturnablePurchase_ReportsRemainingQuantityAndStockOnHand()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 10);
        var itemId = await FirstItemIdAsync(purchase.Id);
        await ReturnAsync(purchase, itemId, 3);

        var line = (await _sut.GetReturnablePurchaseAsync(purchase.Id, _ownerId))!.Lines.Single();

        Assert.Equal(10m, line.ReceivedQuantity);
        Assert.Equal(3m, line.AlreadyReturnedQuantity);
        Assert.Equal(7m, line.ReturnableQuantity);
        Assert.Equal(7m, line.StockOnHand);
    }

    [Fact]
    public async Task Return_WritesAuditEntry()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await ReceiveAsync(supplier, product, 5);
        var itemId = await FirstItemIdAsync(purchase.Id);

        var purchaseReturn = await ReturnAsync(purchase, itemId, 2);

        var audit = await _fixture.Context.AuditLogs.SingleOrDefaultAsync(
            a => a.Action == "PurchaseReturnProcessed" && a.EntityId == purchaseReturn.Id.ToString());
        Assert.NotNull(audit);
        Assert.Contains(purchaseReturn.ReturnNumber, audit!.NewValue);
    }

    public void Dispose() => _fixture.Dispose();
}
