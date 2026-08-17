using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.CashRegisters;
using Kirana.Application.Customers;
using Kirana.Application.Expenses;
using Kirana.Application.Purchasing;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.CashRegisters;

/// <summary>
/// Phase 16A-2: money cannot move in or out of a drawer that does not exist.
///
/// <para>Cash recorded while no register is open belongs to no session, so nothing ever reconciles
/// it — the gap Phase 16A-1 documented and deferred. Every cash-impacting path now refuses; every
/// non-cash path is deliberately untouched, because a UPI sale never opens the till.</para>
///
/// <para>The service layer is the boundary under test here, not the POS. A hand-built request must
/// fail exactly as a button press would.</para>
/// </summary>
public sealed class ClosedRegisterCashPolicyTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SaleService _sales;
    private readonly SalesReturnService _returns;
    private readonly CustomerCreditService _credit;
    private readonly PurchaseService _purchases;
    private readonly SupplierService _suppliers;
    private readonly ExpenseService _expenses;
    private readonly ExpenseCategoryService _categories;
    private readonly CashRegisterService _register;
    private readonly int _ownerId;

    public ClosedRegisterCashPolicyTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        var sequences = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        _sales = new SaleService(_fixture.Context, sequences, audit, permissions);
        _returns = new SalesReturnService(_fixture.Context, sequences, audit, permissions);
        _credit = new CustomerCreditService(_fixture.Context, sequences, audit, permissions);
        _purchases = new PurchaseService(_fixture.Context, sequences, audit, permissions);
        _suppliers = new SupplierService(_fixture.Context, sequences, audit, permissions);
        _expenses = new ExpenseService(_fixture.Context, sequences, audit, permissions);
        _categories = new ExpenseCategoryService(_fixture.Context, audit, permissions);
        _register = new CashRegisterService(_fixture.Context, permissions, audit);
    }

    // ---------------- the policy itself (pure, no database) ----------------

    [Theory]
    [InlineData(PaymentMethod.Cash, true)]
    [InlineData(PaymentMethod.Upi, false)]
    [InlineData(PaymentMethod.Card, false)]
    [InlineData(PaymentMethod.CustomerCredit, false)]
    public void OnlyCash_RequiresAnOpenRegister(PaymentMethod method, bool expected) =>
        Assert.Equal(expected, CashImpactPolicy.RequiresOpenRegister(method));

    [Theory]
    [InlineData(RefundMethod.Cash, true)]
    [InlineData(RefundMethod.Upi, false)]
    [InlineData(RefundMethod.Card, false)]
    [InlineData(RefundMethod.StoreCredit, false)]
    [InlineData(RefundMethod.None, false)]
    public void OnlyACashRefund_RequiresAnOpenRegister(RefundMethod method, bool expected) =>
        Assert.Equal(expected, CashImpactPolicy.RequiresOpenRegister(method));

    [Fact]
    public void ASplitContainingCash_RequiresAnOpenRegister()
    {
        // The rule is "any tender is cash", not "the tender is cash" — a bill that takes ₹50 cash
        // and ₹50 UPI still opens the drawer.
        Assert.True(CashImpactPolicy.RequiresOpenRegister([PaymentMethod.Cash, PaymentMethod.Upi]));
        Assert.True(CashImpactPolicy.RequiresOpenRegister([PaymentMethod.Upi, PaymentMethod.Cash]));
        Assert.True(CashImpactPolicy.RequiresOpenRegister([PaymentMethod.Cash, PaymentMethod.Card]));
        Assert.True(CashImpactPolicy.RequiresOpenRegister([PaymentMethod.Cash, PaymentMethod.CustomerCredit]));
    }

    [Fact]
    public void ASplitWithNoCash_DoesNotRequireAnOpenRegister()
    {
        Assert.False(CashImpactPolicy.RequiresOpenRegister([PaymentMethod.Upi, PaymentMethod.Card]));
        Assert.False(CashImpactPolicy.RequiresOpenRegister([PaymentMethod.Card, PaymentMethod.CustomerCredit]));
        Assert.False(CashImpactPolicy.RequiresOpenRegister(Array.Empty<PaymentMethod>()));
    }

    // ---------------- sales ----------------

    [Fact]
    public async Task ACashSale_IsRefused_WhenNoRegisterIsOpen()
    {
        var product = await SeedProductAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sales.CompleteSaleAsync(CashSale(product.Id, 25m)));

        Assert.Equal(CashImpactPolicy.NoOpenRegisterMessage, error.Message);
    }

    [Fact]
    public async Task ARefusedCashSale_LeavesNothingBehind()
    {
        var product = await SeedProductAsync();
        var stockBefore = (await _fixture.Context.Inventories.SingleAsync()).QuantityOnHand;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sales.CompleteSaleAsync(CashSale(product.Id, 25m)));

        Assert.Empty(await _fixture.Context.Sales.ToListAsync());
        Assert.Empty(await _fixture.Context.SaleItems.ToListAsync());
        Assert.Empty(await _fixture.Context.Payments.ToListAsync());
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Equal(stockBefore, (await _fixture.Context.Inventories.SingleAsync()).QuantityOnHand);
        Assert.DoesNotContain(await _fixture.Context.AuditLogs.ToListAsync(), a => a.Action == "SaleCompleted");
    }

    [Fact]
    public async Task ACashSale_Succeeds_OnceTheRegisterIsOpen()
    {
        var product = await SeedProductAsync();
        await _register.OpenAsync(new(1_000m, _ownerId));

        var sale = await _sales.CompleteSaleAsync(CashSale(product.Id, 25m));

        Assert.True(sale.Id > 0);
        Assert.Equal(25m, sale.GrandTotal);
    }

    [Theory]
    [InlineData(PaymentMethod.Upi)]
    [InlineData(PaymentMethod.Card)]
    public async Task ANonCashSale_Succeeds_WithTheRegisterClosed(PaymentMethod method)
    {
        var product = await SeedProductAsync();

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = method, Amount = 25m }],
        });

        Assert.True(sale.Id > 0);
        Assert.Equal(method, Assert.Single(sale.Payments).Method);
    }

    [Fact]
    public async Task AnUdhaarSale_Succeeds_WithTheRegisterClosed()
    {
        var product = await SeedProductAsync();
        var customer = await SeedCustomerAsync();

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            CustomerId = customer.Id,
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = 25m }],
        });

        Assert.True(sale.Id > 0);
    }

    [Fact]
    public async Task ASplitContainingCash_IsRefused_WhileANonCashSplitIsNot()
    {
        var product = await SeedProductAsync(price: 100m, stock: 50m);
        var customer = await SeedCustomerAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sales.CompleteSaleAsync(new CompleteSaleRequest
            {
                Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
                CustomerId = customer.Id,
                Payments =
                [
                    new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 40m, AmountTendered = 40m },
                    new SalePaymentInput { Method = PaymentMethod.Upi, Amount = 60m },
                ],
            }));

        Assert.Empty(await _fixture.Context.Sales.ToListAsync());

        // The same bill split across two non-cash tenders goes through untouched.
        var allowed = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            CustomerId = customer.Id,
            Payments =
            [
                new SalePaymentInput { Method = PaymentMethod.Upi, Amount = 40m },
                new SalePaymentInput { Method = PaymentMethod.Card, Amount = 60m },
            ],
        });
        Assert.Equal(2, allowed.Payments.Count);
    }

    // ---------------- refunds ----------------

    [Fact]
    public async Task ACashRefund_IsRefused_WhenNoRegisterIsOpen()
    {
        var (saleId, saleItemId) = await SeedCompletedCashSaleAsync();
        await _fixture.Context.CloseOpenRegisterAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _returns.ProcessReturnAsync(ReturnRequest(saleId, saleItemId, RefundMethod.Cash)));

        Assert.Equal(CashImpactPolicy.NoOpenRegisterMessage, error.Message);
        Assert.Empty(await _fixture.Context.SalesReturns.ToListAsync());
        Assert.Empty(await _fixture.Context.SalesReturnItems.ToListAsync());
    }

    [Fact]
    public async Task ARefusedCashRefund_RestoresNoStock()
    {
        var (saleId, saleItemId) = await SeedCompletedCashSaleAsync();
        await _fixture.Context.CloseOpenRegisterAsync();
        var stockBefore = (await _fixture.Context.Inventories.SingleAsync()).QuantityOnHand;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _returns.ProcessReturnAsync(ReturnRequest(saleId, saleItemId, RefundMethod.Cash)));

        Assert.Equal(stockBefore, (await _fixture.Context.Inventories.SingleAsync()).QuantityOnHand);
    }

    [Theory]
    [InlineData(RefundMethod.StoreCredit)]
    [InlineData(RefundMethod.None)]
    public async Task ANonCashRefund_Succeeds_WithTheRegisterClosed(RefundMethod method)
    {
        var (saleId, saleItemId) = await SeedCompletedCashSaleAsync(withCustomer: true);
        await _fixture.Context.CloseOpenRegisterAsync();

        var processed = await _returns.ProcessReturnAsync(ReturnRequest(saleId, saleItemId, method));

        Assert.True(processed.Id > 0);
        Assert.Equal(method, processed.RefundMethod);
    }

    // ---------------- customer repayment ----------------

    [Fact]
    public async Task ACashRepayment_IsRefused_AndLeavesTheBalanceAlone()
    {
        var customer = await SeedCustomerWithDebtAsync(500m);
        var owedBefore = (await _fixture.Context.Customers.SingleAsync(c => c.Id == customer.Id)).CreditBalance;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _credit.RecordRepaymentAsync(new RecordCreditPaymentRequest
            {
                CustomerId = customer.Id, Amount = 200m, Method = PaymentMethod.Cash, RecordedByUserId = _ownerId,
            }));

        Assert.Equal(CashImpactPolicy.NoOpenRegisterMessage, error.Message);
        Assert.Empty(await _fixture.Context.CreditPayments.ToListAsync());
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(owedBefore, (await _fixture.Context.Customers.SingleAsync(c => c.Id == customer.Id)).CreditBalance);
    }

    [Fact]
    public async Task ANonCashRepayment_Succeeds_WithTheRegisterClosed()
    {
        var customer = await SeedCustomerWithDebtAsync(500m);

        var payment = await _credit.RecordRepaymentAsync(new RecordCreditPaymentRequest
        {
            CustomerId = customer.Id, Amount = 200m, Method = PaymentMethod.Upi, RecordedByUserId = _ownerId,
        });

        Assert.True(payment.Id > 0);
    }

    // ---------------- supplier payment ----------------

    [Fact]
    public async Task ACashSupplierPayment_IsRefused_AndLeavesThePayableAlone()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        var owedBefore = (await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id)).OutstandingBalance;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _purchases.RecordPaymentAsync(new RecordSupplierPaymentRequest
            {
                SupplierId = supplier.Id, Amount = 1_000m, Method = PaymentMethod.Cash, RecordedByUserId = _ownerId,
            }));

        Assert.Equal(CashImpactPolicy.NoOpenRegisterMessage, error.Message);
        Assert.Empty(await _fixture.Context.SupplierPayments.ToListAsync());
        _fixture.Context.ChangeTracker.Clear();
        Assert.Equal(owedBefore, (await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id)).OutstandingBalance);
    }

    [Fact]
    public async Task ANonCashSupplierPayment_Succeeds_WithTheRegisterClosed()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);

        var payment = await _purchases.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id, Amount = 1_000m, Method = PaymentMethod.Upi, RecordedByUserId = _ownerId,
        });

        Assert.True(payment.Id > 0);
    }

    // ---------------- expenses ----------------

    [Fact]
    public async Task ACashExpense_IsRefused_AndCreatesNoRow()
    {
        var category = await CategoryAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _expenses.CreateAsync(new CreateExpenseRequest
            {
                ExpenseCategoryId = category.Id, Amount = 500m,
                PaymentMethod = PaymentMethod.Cash, PerformedByUserId = _ownerId,
            }));

        Assert.Equal(CashImpactPolicy.NoOpenRegisterMessage, error.Message);
        Assert.Empty(await _fixture.Context.Expenses.ToListAsync());
        Assert.DoesNotContain(await _fixture.Context.AuditLogs.ToListAsync(), a => a.Action == "ExpenseCreated");
    }

    [Fact]
    public async Task ANonCashExpense_Succeeds_WithTheRegisterClosed()
    {
        var category = await CategoryAsync();

        var expense = await _expenses.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = category.Id, Amount = 500m,
            PaymentMethod = PaymentMethod.Upi, PerformedByUserId = _ownerId,
        });

        Assert.True(expense.Id > 0);
    }

    // ---------------- cash in / out (existing behaviour, pinned) ----------------

    [Theory]
    [InlineData(CashMovementType.CashIn)]
    [InlineData(CashMovementType.CashOut)]
    public async Task ManualDrawerMovements_StillRequireAnOpenRegister(CashMovementType type)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _register.RecordMovementAsync(new(type, 100m, "Test", _ownerId, Guid.NewGuid())));

        Assert.Empty(await _fixture.Context.CashMovements.ToListAsync());
    }

    // ---------------- concurrency ----------------

    [Fact]
    public async Task ARegisterClosedByAnotherContext_IsSeenImmediately()
    {
        using var fileFixture = new SqliteFileDbContextFixture();
        var owner = await fileFixture.SeedOwnerAsync();
        var product = await SeedProductAsync(fileFixture.Context);
        var permissions = new PermissionEnforcer(fileFixture.Context);
        var registerA = new CashRegisterService(fileFixture.Context, permissions, new EfAuditLogger(fileFixture.Context));
        var salesA = new SaleService(
            fileFixture.Context, new EfSequenceGenerator(fileFixture.Context), new EfAuditLogger(fileFixture.Context), permissions);
        await registerA.OpenAsync(new(1_000m, owner.Id));

        // Another screen closes the drawer.
        await using (var contextB = NewContext(fileFixture))
        {
            await contextB.CloseOpenRegisterAsync();
        }

        // Context A still holds the session it opened in its change tracker. A stale read here would
        // wave the sale through — this is the Phase 13C identity-map trap.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            salesA.CompleteSaleAsync(CashSale(product.Id, 25m)));
        Assert.Equal(CashImpactPolicy.NoOpenRegisterMessage, error.Message);
    }

    [Fact]
    public async Task ARegisterOpenedByAnotherContext_IsSeenImmediately()
    {
        using var fileFixture = new SqliteFileDbContextFixture();
        var owner = await fileFixture.SeedOwnerAsync();
        var product = await SeedProductAsync(fileFixture.Context);
        var permissions = new PermissionEnforcer(fileFixture.Context);
        var salesA = new SaleService(
            fileFixture.Context, new EfSequenceGenerator(fileFixture.Context), new EfAuditLogger(fileFixture.Context), permissions);

        // Refused while closed...
        await Assert.ThrowsAsync<InvalidOperationException>(() => salesA.CompleteSaleAsync(CashSale(product.Id, 25m)));

        await using (var contextB = NewContext(fileFixture))
        {
            await contextB.SeedOpenRegisterAsync(owner.Id);
        }

        // ...and allowed the moment someone else opens the drawer, with no restart in between.
        var sale = await salesA.CompleteSaleAsync(CashSale(product.Id, 25m));
        Assert.True(sale.Id > 0);
    }

    // ---------------- helpers ----------------

    private static KiranaDbContext NewContext(SqliteFileDbContextFixture fixture) =>
        new(new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite($"Data Source={fixture.Paths.DatabaseFilePath}").Options);

    private static CompleteSaleRequest CashSale(int productId, decimal amount) => new()
    {
        Lines = [new SaleLineInput { ProductId = productId, Quantity = 1 }],
        Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = amount, AmountTendered = amount }],
    };

    private CreateSalesReturnRequest ReturnRequest(int saleId, int saleItemId, RefundMethod method) => new()
    {
        SaleId = saleId,
        RefundMethod = method,
        ProcessedByUserId = _ownerId,
        Lines = [new SalesReturnLineInput { SaleItemId = saleItemId, Quantity = 1m, Disposition = ReturnDisposition.ReturnToStock }],
    };

    private Task<Product> SeedProductAsync(decimal price = 25m, decimal stock = 100m) =>
        SeedProductAsync(_fixture.Context, price, stock);

    private static async Task<Product> SeedProductAsync(KiranaDbContext context, decimal price = 25m, decimal stock = 100m)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Policy Test Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = price / 2,
            Mrp = price + 5,
            SellingPrice = price,
            IsActive = true,
        }.WithRetailPrice();
        context.Products.Add(product);
        context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await context.SaveChangesAsync();
        return product;
    }

    private async Task<Customer> SeedCustomerAsync()
    {
        var customer = new Customer
        {
            CustomerCode = $"CUS-{Guid.NewGuid():N}"[..10],
            Name = "Policy Customer",
        };
        _fixture.Context.Customers.Add(customer);
        await _fixture.Context.SaveChangesAsync();
        return customer;
    }

    /// <summary>Builds the debt through a real udhaar sale rather than hand-writing a
    /// CustomerCredit row — the credit is tied to a Sale by a required FK, and an udhaar sale needs
    /// no open register, so this sets up the "owes money, drawer closed" case honestly.</summary>
    private async Task<Customer> SeedCustomerWithDebtAsync(decimal amount)
    {
        var product = await SeedProductAsync(price: amount, stock: 10m);
        var customer = await SeedCustomerAsync();

        await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            CustomerId = customer.Id,
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = amount }],
        });

        _fixture.Context.ChangeTracker.Clear();
        return await _fixture.Context.Customers.SingleAsync(c => c.Id == customer.Id);
    }

    private async Task<Supplier> SeedSupplierWithOutstandingAsync(decimal outstanding)
    {
        var supplier = await _suppliers.CreateAsync(new CreateSupplierRequest
        {
            Name = $"Supplier {Guid.NewGuid():N}"[..18], PerformedByUserId = _ownerId,
        });
        var tracked = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        tracked.OutstandingBalance = outstanding;
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
        return tracked;
    }

    private async Task<ExpenseCategory> CategoryAsync() =>
        await _categories.CreateAsync(new CreateExpenseCategoryRequest { Name = "Rent", PerformedByUserId = _ownerId });

    /// <summary>Completes a cash sale with the register open, then hands back the ids. The caller
    /// closes the register to set up the "refund with no drawer" case.</summary>
    private async Task<(int SaleId, int SaleItemId)> SeedCompletedCashSaleAsync(bool withCustomer = false)
    {
        var product = await SeedProductAsync();
        await _register.OpenAsync(new(1_000m, _ownerId));

        var request = CashSale(product.Id, 25m);
        if (withCustomer)
        {
            var customer = await SeedCustomerAsync();
            request = new CompleteSaleRequest
            {
                Lines = request.Lines, Payments = request.Payments, CustomerId = customer.Id,
            };
        }

        var sale = await _sales.CompleteSaleAsync(request);
        return (sale.Id, sale.Items.First().Id);
    }

    public void Dispose() => _fixture.Dispose();
}
