using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Customers;

/// <summary>
/// Proves the Phase 8 permission split holds at the Application layer, not just in the UI:
/// customer <em>master data</em> stays reachable so logged-out Billing Mode can still look up and add
/// a customer (PRD §4), while every customer <em>financial</em> surface — ledger, outstanding
/// balances, repayment history, receipts — requires <see cref="PermissionKeys.CustomersManage"/>.
/// </summary>
public class CustomersAuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CustomerService _customerService;
    private readonly CustomerCreditService _creditService;
    private readonly CustomerReceiptService _receiptService;
    private readonly int _ownerId;
    private int _cashierId;
    private int _customerId;
    private int _paymentId;

    public CustomersAuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _customerService = new CustomerService(_fixture.Context, seq, audit);
        _creditService = new CustomerCreditService(_fixture.Context, seq, audit, enforcer);
        _receiptService = new CustomerReceiptService(_fixture.Context, _creditService, audit);

        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        _cashierId = (await _fixture.SeedCashierAsync()).Id;

        var customer = await _customerService.CreateAsync(new CreateCustomerRequest { Name = "Gated Customer" });
        _customerId = customer.Id;

        var product = new Product
        {
            ProductCode = "PRD-CGATE1", Name = "Gated Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 50, Mrp = 70, SellingPrice = 100, IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100 });
        await _fixture.Context.SaveChangesAsync();

        var seq = new EfSequenceGenerator(_fixture.Context);
        var saleService = new SaleService(
            _fixture.Context, seq, new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));

        await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = _customerId,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 5 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = 500 }],
            CashierUserId = _ownerId,
        });

        var payment = await _creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
        {
            CustomerId = _customerId, Amount = 100, Method = PaymentMethod.Cash, RecordedByUserId = _ownerId,
        });
        _paymentId = payment.Id;
    }

    // ---------- financial reads are gated ----------

    [Fact]
    public async Task Cashier_CannotReadCustomerLedger() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetLedgerAsync(_customerId, _cashierId));

    [Fact]
    public async Task Cashier_CannotReadOutstandingSummary() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetOutstandingSummaryAsync(_cashierId));

    [Fact]
    public async Task Cashier_CannotReadCustomerOverview() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.SearchOverviewAsync(new CustomerSearchQuery(), _cashierId));

    [Fact]
    public async Task Cashier_CannotReadPurchaseHistory() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetPurchaseHistoryAsync(_customerId, _cashierId));

    [Fact]
    public async Task Cashier_CannotReadRepaymentHistory() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetRepaymentHistoryAsync(_customerId, _cashierId));

    [Fact]
    public async Task Cashier_CannotReadOpenCredits() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetOpenCreditsAsync(_customerId, _cashierId));

    [Fact]
    public async Task Cashier_CannotReadRepaymentById() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetRepaymentByIdAsync(_paymentId, _cashierId));

    [Fact]
    public async Task Cashier_CannotBuildCustomerReceipt() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _receiptService.GetReceiptAsync(_paymentId, _cashierId));

    // ---------- financial writes are gated ----------

    [Fact]
    public async Task Cashier_CannotRecordRepayment() =>
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
            {
                CustomerId = _customerId, Amount = 50, Method = PaymentMethod.Cash, RecordedByUserId = _cashierId,
            }));

    [Fact]
    public async Task BlockedRepayment_LeavesNoTrace()
    {
        var balanceBefore = (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == _customerId)).CreditBalance;
        var paymentsBefore = await _fixture.Context.CreditPayments.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
            {
                CustomerId = _customerId, Amount = 400, Method = PaymentMethod.Cash, RecordedByUserId = _cashierId,
            }));

        Assert.Equal(balanceBefore, (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == _customerId)).CreditBalance);
        Assert.Equal(paymentsBefore, await _fixture.Context.CreditPayments.CountAsync());
    }

    // ---------- anonymous ----------

    [Fact]
    public async Task NoUser_CannotReachAnyCustomerFinancialSurface()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _creditService.GetLedgerAsync(_customerId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _creditService.GetOutstandingSummaryAsync(null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _creditService.SearchOverviewAsync(new CustomerSearchQuery(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _creditService.GetRepaymentHistoryAsync(_customerId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _receiptService.GetReceiptAsync(_paymentId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
            {
                CustomerId = _customerId, Amount = 10, Method = PaymentMethod.Cash, RecordedByUserId = null,
            }));
    }

    // ---------- POS master-data paths stay open ----------

    [Fact]
    public async Task LoggedOutPos_CanStillSearchAndCreateCustomers()
    {
        // Billing Mode runs with no authenticated user; blocking this would break walk-in billing.
        var created = await _customerService.CreateAsync(new CreateCustomerRequest { Name = "Walk-in Added At Till" });
        Assert.NotEmpty(created.CustomerCode);

        Assert.NotEmpty(await _customerService.SearchAsync(new CustomerSearchQuery { SearchText = "Walk-in" }));
        Assert.NotNull(await _customerService.GetByIdAsync(_customerId));
    }

    [Fact]
    public async Task CustomerMasterData_DoesNotExposeFinancialDetail()
    {
        // The entity carries a denormalized CreditBalance, so the guarantee that matters is that the
        // ledger/allocation detail behind it is unreachable without the permission — asserted above.
        // This test pins the boundary: master-data reads succeed, financial reads for the same
        // customer and the same caller do not.
        var customer = await _customerService.GetByIdAsync(_customerId);
        Assert.NotNull(customer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _creditService.GetLedgerAsync(_customerId, _cashierId));
    }

    // ---------- Manager holds the permission ----------

    [Fact]
    public async Task Manager_CanReachCustomerFinancialSurfaces()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User
        {
            Username = "mgr-cust", FullName = "Customer Manager",
            PasswordHash = "x", Role = managerRole, IsActive = true,
        };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        Assert.NotEmpty(await _creditService.GetLedgerAsync(_customerId, manager.Id));
        Assert.NotEmpty(await _creditService.GetOutstandingSummaryAsync(manager.Id));
        Assert.NotEmpty(await _creditService.GetRepaymentHistoryAsync(_customerId, manager.Id));
        Assert.NotNull(await _receiptService.GetReceiptAsync(_paymentId, manager.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
