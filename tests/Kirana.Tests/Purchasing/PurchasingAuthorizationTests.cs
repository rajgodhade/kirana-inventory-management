using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Purchasing;

/// <summary>
/// Consolidated proof that a user without <see cref="PermissionKeys.PurchasesManage"/> cannot reach
/// supplier/purchase financial data through the Application layer at all — reads included, since
/// suppliers carry outstanding balances and purchases carry negotiated purchase prices (PRD §6, §9).
/// UI-level gating (hidden nav buttons, disabled actions) is verified separately and live; these
/// tests exist because UI hiding alone must never be the only barrier.
/// </summary>
public class PurchasingAuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SupplierService _supplierService;
    private readonly PurchaseService _purchaseService;
    private readonly int _ownerId;
    private int _cashierId;
    private int _supplierId;
    private int _purchaseId;

    public PurchasingAuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);
        _supplierService = new SupplierService(_fixture.Context, seq, audit, enforcer);
        _purchaseService = new PurchaseService(_fixture.Context, seq, audit, enforcer);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        _cashierId = (await _fixture.SeedCashierAsync()).Id;

        var supplier = await _supplierService.CreateAsync(
            new CreateSupplierRequest { Name = "Gated Supplier", PerformedByUserId = _ownerId });
        _supplierId = supplier.Id;

        var product = new Product
        {
            ProductCode = "PRD-GATE01", Name = "Gated Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 50, Mrp = 70, SellingPrice = 65, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 0 });
        await _fixture.Context.SaveChangesAsync();

        var purchase = await _purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = _supplierId,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 2, UnitPrice = 50 }],
            CreatedByUserId = _ownerId,
        });
        _purchaseId = purchase.Id;
    }

    // ---------- reads ----------

    [Fact]
    public async Task Cashier_CannotSearchSuppliers() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _supplierService.SearchAsync(new SupplierSearchQuery(), _cashierId));

    [Fact]
    public async Task Cashier_CannotReadSupplierById() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _supplierService.GetByIdAsync(_supplierId, _cashierId));

    [Fact]
    public async Task Cashier_CannotReadSupplierLedger() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _supplierService.GetLedgerAsync(_supplierId, _cashierId));

    [Fact]
    public async Task Cashier_CannotSearchPurchases() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseService.SearchAsync(new PurchaseSearchQuery(), _cashierId));

    [Fact]
    public async Task Cashier_CannotReadPurchaseById() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseService.GetByIdAsync(_purchaseId, _cashierId));

    // ---------- writes ----------

    [Fact]
    public async Task Cashier_CannotCreateSupplier() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _supplierService.CreateAsync(new CreateSupplierRequest { Name = "X", PerformedByUserId = _cashierId }));

    [Fact]
    public async Task Cashier_CannotUpdateSupplier() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _supplierService.UpdateAsync(_supplierId, new UpdateSupplierRequest { Name = "X", PerformedByUserId = _cashierId }));

    [Fact]
    public async Task Cashier_CannotDeactivateSupplier() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _supplierService.SetActiveAsync(_supplierId, isActive: false, _cashierId));

    [Fact]
    public async Task Cashier_CannotFinalizePurchase()
    {
        var product = await _fixture.Context.Products.FirstAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _purchaseService.FinalizePurchaseAsync(
            new CreatePurchaseRequest
            {
                SupplierId = _supplierId,
                Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 50 }],
                CreatedByUserId = _cashierId,
            }));
    }

    [Fact]
    public async Task Cashier_CannotRecordSupplierPayment() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _purchaseService.RecordPaymentAsync(
            new RecordSupplierPaymentRequest
            {
                SupplierId = _supplierId, Amount = 10, Method = PaymentMethod.Cash, RecordedByUserId = _cashierId,
            }));

    // ---------- anonymous / no user ----------

    [Fact]
    public async Task NoUser_CannotReachAnyPurchasingSurface()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _supplierService.SearchAsync(new SupplierSearchQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _supplierService.GetLedgerAsync(_supplierId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _purchaseService.SearchAsync(new PurchaseSearchQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _purchaseService.GetByIdAsync(_purchaseId, null));
    }

    // ---------- a blocked attempt must not mutate anything ----------

    [Fact]
    public async Task BlockedPurchaseAttempt_LeavesNoTrace()
    {
        var purchasesBefore = await _fixture.Context.Purchases.CountAsync();
        var movementsBefore = await _fixture.Context.StockMovements.CountAsync();
        var product = await _fixture.Context.Products.FirstAsync();
        var stockBefore = (await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _purchaseService.FinalizePurchaseAsync(
            new CreatePurchaseRequest
            {
                SupplierId = _supplierId,
                Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 99, UnitPrice = 50 }],
                CreatedByUserId = _cashierId,
            }));

        Assert.Equal(purchasesBefore, await _fixture.Context.Purchases.CountAsync());
        Assert.Equal(movementsBefore, await _fixture.Context.StockMovements.CountAsync());
        Assert.Equal(stockBefore, (await _fixture.Context.Inventories.FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
    }

    // ---------- Manager (who does hold the permission) is unaffected ----------

    [Fact]
    public async Task Manager_CanReachPurchasingSurfaces()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User
        {
            Username = "mgr-purch", FullName = "Purchasing Manager",
            PasswordHash = "x", Role = managerRole, IsActive = true,
        };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        var suppliers = await _supplierService.SearchAsync(new SupplierSearchQuery(), manager.Id);
        Assert.NotEmpty(suppliers);

        var ledger = await _supplierService.GetLedgerAsync(_supplierId, manager.Id);
        Assert.NotEmpty(ledger);

        var purchases = await _purchaseService.SearchAsync(new PurchaseSearchQuery(), manager.Id);
        Assert.NotEmpty(purchases);
    }

    public void Dispose() => _fixture.Dispose();
}
