using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Expenses;
using Kirana.Application.Purchasing;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Returns;

/// <summary>
/// Proves the Phase 9 surfaces are gated at the Application layer, not just hidden in the UI.
/// Phase 9 deliberately adds no new permission keys: returns and refunds reuse
/// <see cref="PermissionKeys.SalesProcessRefund"/>, purchase returns reuse
/// <see cref="PermissionKeys.PurchasesManage"/>, and expenses reuse
/// <see cref="PermissionKeys.ExpensesManage"/> — all three already exist and are already mapped to
/// Owner and Manager but not Cashier.
/// </summary>
public class Phase9AuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SalesReturnService _salesReturns;
    private readonly PurchaseReturnService _purchaseReturns;
    private readonly ExpenseService _expenses;
    private readonly ExpenseCategoryService _categories;
    private readonly int _ownerId;
    private int _cashierId;
    private int _saleId;
    private int _saleItemId;
    private int _purchaseId;
    private int _purchaseItemId;
    private int _categoryId;
    private int _expenseId;

    public Phase9AuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _salesReturns = new SalesReturnService(_fixture.Context, seq, audit, enforcer);
        _purchaseReturns = new PurchaseReturnService(_fixture.Context, seq, audit, enforcer);
        _expenses = new ExpenseService(_fixture.Context, seq, audit, enforcer);
        _categories = new ExpenseCategoryService(_fixture.Context, audit, enforcer);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        _cashierId = (await _fixture.SeedCashierAsync()).Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        var product = new Product
        {
            ProductCode = "PRD-AUTH01", Name = "Gated Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 50, Mrp = 120, SellingPrice = 100, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100 });
        await _fixture.Context.SaveChangesAsync();

        var sale = await new SaleService(_fixture.Context, seq, audit, enforcer).CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 5 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 500, AmountTendered = 500 }],
            CashierUserId = _ownerId,
        });
        _saleId = sale.Id;
        _saleItemId = (await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == sale.Id)).Id;

        var supplier = await new SupplierService(_fixture.Context, seq, audit, enforcer)
            .CreateAsync(new CreateSupplierRequest { Name = "Gated Supplier", PerformedByUserId = _ownerId });

        var purchase = await new PurchaseService(_fixture.Context, seq, audit, enforcer)
            .FinalizePurchaseAsync(new CreatePurchaseRequest
            {
                SupplierId = supplier.Id,
                Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 10, UnitPrice = 50 }],
                CreatedByUserId = _ownerId,
            });
        _purchaseId = purchase.Id;
        _purchaseItemId = (await _fixture.Context.PurchaseItems.AsNoTracking().FirstAsync(i => i.PurchaseId == purchase.Id)).Id;

        var category = await _categories.CreateAsync(new CreateExpenseCategoryRequest { Name = "Rent", PerformedByUserId = _ownerId });
        _categoryId = category.Id;

        var expense = await _expenses.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = _categoryId, Amount = 5000, PerformedByUserId = _ownerId,
        });
        _expenseId = expense.Id;
    }

    // ---------------------------------------------------------------- sales returns

    [Fact]
    public async Task Cashier_CannotLookUpSalesToReturn() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _salesReturns.FindReturnableSalesAsync(new SaleLookupQuery(), _cashierId));

    [Fact]
    public async Task Cashier_CannotReadAReturnableSale() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _salesReturns.GetReturnableSaleAsync(_saleId, _cashierId));

    [Fact]
    public async Task Cashier_CannotSearchOrReadSalesReturns()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _salesReturns.SearchAsync(new SalesReturnSearchQuery(), _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _salesReturns.GetByIdAsync(1, _cashierId));
    }

    [Fact]
    public async Task Cashier_CannotProcessASalesReturn() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _salesReturns.ProcessReturnAsync(new CreateSalesReturnRequest
            {
                SaleId = _saleId,
                Lines = [new SalesReturnLineInput { SaleItemId = _saleItemId, Quantity = 1 }],
                ProcessedByUserId = _cashierId,
            }));

    [Fact]
    public async Task BlockedSalesReturn_LeavesNoStockOrRecordBehind()
    {
        var stockBefore = (await _fixture.Context.Inventories.AsNoTracking().FirstAsync()).QuantityOnHand;
        var movementsBefore = await _fixture.Context.StockMovements.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _salesReturns.ProcessReturnAsync(new CreateSalesReturnRequest
            {
                SaleId = _saleId,
                Lines = [new SalesReturnLineInput { SaleItemId = _saleItemId, Quantity = 3 }],
                ProcessedByUserId = _cashierId,
            }));

        Assert.Equal(stockBefore, (await _fixture.Context.Inventories.AsNoTracking().FirstAsync()).QuantityOnHand);
        Assert.Equal(movementsBefore, await _fixture.Context.StockMovements.CountAsync());
        Assert.Empty(await _fixture.Context.SalesReturns.ToListAsync());
    }

    // ---------------------------------------------------------------- purchase returns

    [Fact]
    public async Task Cashier_CannotReachPurchaseReturns()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseReturns.FindReturnablePurchasesAsync(null, _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseReturns.GetReturnablePurchaseAsync(_purchaseId, _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseReturns.SearchAsync(new PurchaseReturnSearchQuery(), _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseReturns.ProcessReturnAsync(new CreatePurchaseReturnRequest
            {
                PurchaseId = _purchaseId,
                Lines = [new PurchaseReturnLineInput { PurchaseItemId = _purchaseItemId, Quantity = 1 }],
                ProcessedByUserId = _cashierId,
            }));
    }

    [Fact]
    public async Task BlockedPurchaseReturn_LeavesSupplierBalanceUntouched()
    {
        var balanceBefore = (await _fixture.Context.Suppliers.AsNoTracking().FirstAsync()).OutstandingBalance;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseReturns.ProcessReturnAsync(new CreatePurchaseReturnRequest
            {
                PurchaseId = _purchaseId,
                Lines = [new PurchaseReturnLineInput { PurchaseItemId = _purchaseItemId, Quantity = 5 }],
                ProcessedByUserId = _cashierId,
            }));

        Assert.Equal(balanceBefore, (await _fixture.Context.Suppliers.AsNoTracking().FirstAsync()).OutstandingBalance);
        Assert.Empty(await _fixture.Context.PurchaseReturns.ToListAsync());
    }

    // ---------------------------------------------------------------- expenses

    [Fact]
    public async Task Cashier_CannotReachExpenses()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _expenses.SearchAsync(new ExpenseSearchQuery(), _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _expenses.GetTotalsAsync(new ExpenseSearchQuery(), _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _expenses.GetByIdAsync(_expenseId, _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _expenses.CreateAsync(new CreateExpenseRequest
            {
                ExpenseCategoryId = _categoryId, Amount = 100, PerformedByUserId = _cashierId,
            }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _expenses.DeleteAsync(_expenseId, _cashierId));
    }

    [Fact]
    public async Task Cashier_CannotReachExpenseCategories()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _categories.GetAllAsync(false, _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _categories.CreateAsync(new CreateExpenseCategoryRequest { Name = "X", PerformedByUserId = _cashierId }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _categories.DeleteAsync(_categoryId, _cashierId));
    }

    // ---------------------------------------------------------------- anonymous

    [Fact]
    public async Task NoUser_CannotReachAnyPhase9Surface()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _salesReturns.SearchAsync(new SalesReturnSearchQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _purchaseReturns.SearchAsync(new PurchaseReturnSearchQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _expenses.SearchAsync(new ExpenseSearchQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _categories.GetAllAsync(false, null));
    }

    // ---------------------------------------------------------------- manager holds them

    [Fact]
    public async Task Manager_CanReachAllPhase9Surfaces()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User
        {
            Username = "mgr-phase9", FullName = "Phase 9 Manager",
            PasswordHash = "x", Role = managerRole, IsActive = true,
        };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        Assert.NotNull(await _salesReturns.GetReturnableSaleAsync(_saleId, manager.Id));
        Assert.NotNull(await _purchaseReturns.GetReturnablePurchaseAsync(_purchaseId, manager.Id));
        Assert.NotEmpty(await _expenses.SearchAsync(new ExpenseSearchQuery(), manager.Id));
        Assert.NotEmpty(await _categories.GetAllAsync(false, manager.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
