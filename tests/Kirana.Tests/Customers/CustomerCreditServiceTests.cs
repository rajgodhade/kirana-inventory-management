using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Customers;

/// <summary>
/// The financial heart of Phase 8: credit sales creating Udhaar, repayments drawing it down, and the
/// ledger that has to reconcile with both. Credit sales go through the real <see cref="SaleService"/>
/// rather than hand-built rows, so these tests also prove the Phase 4 sale path and the Phase 8
/// repayment path agree with each other.
/// </summary>
public class CustomerCreditServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CustomerService _customerService;
    private readonly CustomerCreditService _sut;
    private readonly SaleService _saleService;
    private int _ownerId;

    public CustomerCreditServiceTests()
    {
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _customerService = new CustomerService(_fixture.Context, seq, audit);
        _sut = new CustomerCreditService(_fixture.Context, seq, audit, enforcer);
        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);

        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
    }

    // ---------- helpers ----------

    private async Task<Customer> SeedCustomerAsync(string name = "Udhaar Customer") =>
        await _customerService.CreateAsync(new CreateCustomerRequest { Name = name });

    private async Task<Product> SeedProductAsync(decimal price = 100, decimal stock = 1000)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Credit Test Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = price * 0.8m,
            Mrp = price + 5,
            SellingPrice = price,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    /// <summary>Completes a real sale where <paramref name="creditAmount"/> goes on Udhaar and the
    /// rest (if any) is paid in cash — i.e. a full credit sale or a split payment.</summary>
    private async Task<Sale> SellOnCreditAsync(
        Customer customer, Product product, decimal quantity, decimal creditAmount, decimal cashAmount = 0)
    {
        var payments = new List<SalePaymentInput>();
        if (cashAmount > 0)
        {
            payments.Add(new SalePaymentInput { Method = PaymentMethod.Cash, Amount = cashAmount, AmountTendered = cashAmount });
        }

        payments.Add(new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = creditAmount });

        return await _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity }],
            Payments = payments,
            CashierUserId = _ownerId,
        });
    }

    private Task<CreditPayment> RepayAsync(Customer customer, decimal amount) =>
        _sut.RecordRepaymentAsync(new RecordCreditPaymentRequest
        {
            CustomerId = customer.Id, Amount = amount, Method = PaymentMethod.Cash, RecordedByUserId = _ownerId,
        });

    private async Task<decimal> BalanceAsync(int customerId) =>
        (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customerId)).CreditBalance;

    // ---------- credit sales ----------

    [Fact]
    public async Task FullCreditSale_CreatesCreditAndRaisesBalance()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);

        var sale = await SellOnCreditAsync(customer, product, quantity: 3, creditAmount: 300);

        Assert.Equal(300m, await BalanceAsync(customer.Id));

        var credit = await _fixture.Context.CustomerCredits.SingleAsync();
        Assert.Equal(sale.Id, credit.SaleId);
        Assert.Equal(300m, credit.Amount);
        Assert.Equal(300m, credit.RemainingAmount);
    }

    [Fact]
    public async Task SplitPayment_OnlyUnpaidPortionBecomesUdhaar()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);

        var sale = await SellOnCreditAsync(customer, product, quantity: 5, creditAmount: 200, cashAmount: 300);

        Assert.Equal(200m, await BalanceAsync(customer.Id));

        var credit = await _fixture.Context.CustomerCredits.SingleAsync();
        Assert.Equal(200m, credit.Amount);

        // The invoice itself still records the full value and both tenders.
        var persisted = await _fixture.Context.Sales.Include(s => s.Payments).FirstAsync(s => s.Id == sale.Id);
        Assert.Equal(500m, persisted.GrandTotal);
        Assert.Equal(2, persisted.Payments.Count);
        Assert.Equal(500m, persisted.Payments.Sum(p => p.Amount));
    }

    [Fact]
    public async Task CreditSale_RequiresCustomer()
    {
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = null,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = 100 }],
            CashierUserId = _ownerId,
        }));
    }

    // ---------- repayments ----------

    [Fact]
    public async Task PartialRepayment_ReducesBalanceAndCredit()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 5, creditAmount: 500);

        var payment = await RepayAsync(customer, 200);

        Assert.Equal(300m, await BalanceAsync(customer.Id));
        Assert.Equal(300m, (await _fixture.Context.CustomerCredits.AsNoTracking().SingleAsync()).RemainingAmount);
        Assert.Equal(200m, payment.Amount);
        Assert.StartsWith("RCPT-", payment.ReceiptNumber);
    }

    [Fact]
    public async Task MultipleRepayments_SettleBalanceToZero()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 6, creditAmount: 600);

        await RepayAsync(customer, 100);
        await RepayAsync(customer, 250);
        await RepayAsync(customer, 250);

        Assert.Equal(0m, await BalanceAsync(customer.Id));
        Assert.Equal(0m, (await _fixture.Context.CustomerCredits.AsNoTracking().SingleAsync()).RemainingAmount);
        Assert.Equal(3, await _fixture.Context.CreditPayments.CountAsync());
    }

    [Fact]
    public async Task Repayment_SettlesOldestCreditFirst()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);

        var firstSale = await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);
        var secondSale = await SellOnCreditAsync(customer, product, quantity: 3, creditAmount: 300);

        // Enough to clear the first credit and bite into the second.
        await RepayAsync(customer, 250);

        var credits = await _fixture.Context.CustomerCredits.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        Assert.Equal(0m, credits.Single(c => c.SaleId == firstSale.Id).RemainingAmount);
        Assert.Equal(250m, credits.Single(c => c.SaleId == secondSale.Id).RemainingAmount);
    }

    [Fact]
    public async Task Repayment_RecordsAllocationsTraceableToOriginatingInvoice()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        var firstSale = await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);
        var secondSale = await SellOnCreditAsync(customer, product, quantity: 3, creditAmount: 300);

        var payment = await RepayAsync(customer, 250);

        var allocations = await _fixture.Context.CreditPaymentAllocations
            .Include(a => a.CustomerCredit)
            .Where(a => a.CreditPaymentId == payment.Id)
            .ToListAsync();

        Assert.Equal(2, allocations.Count);
        Assert.Equal(250m, allocations.Sum(a => a.Amount));
        Assert.Equal(200m, allocations.Single(a => a.CustomerCredit.SaleId == firstSale.Id).Amount);
        Assert.Equal(50m, allocations.Single(a => a.CustomerCredit.SaleId == secondSale.Id).Amount);
    }

    [Fact]
    public async Task ExactFullRepayment_ClearsEverything()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 4, creditAmount: 400);

        await RepayAsync(customer, 400);

        Assert.Equal(0m, await BalanceAsync(customer.Id));
        Assert.Empty(await _sut.GetOpenCreditsAsync(customer.Id, _ownerId));
    }

    // ---------- overpayment prevention ----------

    [Fact]
    public async Task Repayment_Throws_WhenExceedingOutstanding()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RepayAsync(customer, 200.02m));
        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repayment_Throws_WhenCustomerOwesNothing()
    {
        var customer = await SeedCustomerAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RepayAsync(customer, 50));
    }

    [Fact]
    public async Task Repayment_Throws_OnNonPositiveAmount()
    {
        var customer = await SeedCustomerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => RepayAsync(customer, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => RepayAsync(customer, -10));
    }

    [Fact]
    public async Task OverpaymentAttempt_LeavesNoTrace()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RepayAsync(customer, 5000));

        Assert.Equal(200m, await BalanceAsync(customer.Id));
        Assert.Empty(await _fixture.Context.CreditPayments.ToListAsync());
        Assert.Empty(await _fixture.Context.CreditPaymentAllocations.ToListAsync());
        Assert.Equal(200m, (await _fixture.Context.CustomerCredits.AsNoTracking().SingleAsync()).RemainingAmount);
    }

    [Fact]
    public async Task SequentialRepayments_CannotCollectivelyExceedOutstanding()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 3, creditAmount: 300);

        await RepayAsync(customer, 300);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RepayAsync(customer, 1));
        Assert.Equal(0m, await BalanceAsync(customer.Id));
    }

    // ---------- historical invoice integrity ----------

    [Fact]
    public async Task Repayment_NeverAltersTheOriginatingInvoice()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        var sale = await SellOnCreditAsync(customer, product, quantity: 4, creditAmount: 400);

        var before = await _fixture.Context.Sales.AsNoTracking()
            .Include(s => s.Items).Include(s => s.Payments).FirstAsync(s => s.Id == sale.Id);

        await RepayAsync(customer, 150);
        await RepayAsync(customer, 250);

        var after = await _fixture.Context.Sales.AsNoTracking()
            .Include(s => s.Items).Include(s => s.Payments).FirstAsync(s => s.Id == sale.Id);

        Assert.Equal(before.InvoiceNumber, after.InvoiceNumber);
        Assert.Equal(before.GrandTotal, after.GrandTotal);
        Assert.Equal(before.SaleDateUtc, after.SaleDateUtc);
        Assert.Equal(before.Items.Count, after.Items.Count);
        Assert.Equal(before.Payments.Sum(p => p.Amount), after.Payments.Sum(p => p.Amount));
    }

    [Fact]
    public async Task CreditAmount_IsImmutable_WhileRemainingDrawsDown()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 5, creditAmount: 500);

        await RepayAsync(customer, 200);

        var credit = await _fixture.Context.CustomerCredits.AsNoTracking().SingleAsync();
        Assert.Equal(500m, credit.Amount);
        Assert.Equal(300m, credit.RemainingAmount);
    }

    // ---------- ledger ----------

    [Fact]
    public async Task Ledger_ShowsCreditsAndRepaymentsWithRunningBalance()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 3, creditAmount: 300);
        await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);
        await RepayAsync(customer, 150);

        var ledger = await _sut.GetLedgerAsync(customer.Id, _ownerId);

        Assert.Equal(3, ledger.Count);
        Assert.Equal(300m, ledger[0].RunningBalance);
        Assert.Equal(500m, ledger[1].RunningBalance);
        Assert.Equal(350m, ledger[2].RunningBalance);
        Assert.Equal("Repayment", ledger[2].EntryType);
        Assert.Equal(150m, ledger[2].CreditAmount);
    }

    [Fact]
    public async Task Ledger_FinalRunningBalance_MatchesCustomerBalance()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 7, creditAmount: 700);
        await RepayAsync(customer, 125);
        await RepayAsync(customer, 275);

        var ledger = await _sut.GetLedgerAsync(customer.Id, _ownerId);

        Assert.Equal(await BalanceAsync(customer.Id), ledger[^1].RunningBalance);
    }

    [Fact]
    public async Task Ledger_ReferencesOriginatingInvoiceNumber()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        var sale = await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);

        var ledger = await _sut.GetLedgerAsync(customer.Id, _ownerId);

        Assert.Equal(sale.InvoiceNumber, ledger[0].Reference);
    }

    [Fact]
    public async Task Ledger_IsEmpty_ForCustomerWithNoCreditActivity()
    {
        var customer = await SeedCustomerAsync();

        Assert.Empty(await _sut.GetLedgerAsync(customer.Id, _ownerId));
    }

    // ---------- history & summary ----------

    [Fact]
    public async Task PurchaseHistory_ReturnsCustomerSales()
    {
        var customer = await SeedCustomerAsync();
        var other = await SeedCustomerAsync("Someone Else");
        var product = await SeedProductAsync(price: 100);

        await SellOnCreditAsync(customer, product, quantity: 1, creditAmount: 100);
        await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);
        await SellOnCreditAsync(other, product, quantity: 1, creditAmount: 100);

        var history = await _sut.GetPurchaseHistoryAsync(customer.Id, _ownerId);

        Assert.Equal(2, history.Count);
        Assert.All(history, s => Assert.Equal(customer.Id, s.CustomerId));
    }

    [Fact]
    public async Task PurchaseHistory_IncludesFullyPaidCashSales()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);

        await _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 2 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 200, AmountTendered = 200 }],
            CashierUserId = _ownerId,
        });

        Assert.Single(await _sut.GetPurchaseHistoryAsync(customer.Id, _ownerId));
        Assert.Equal(0m, await BalanceAsync(customer.Id));
    }

    [Fact]
    public async Task RepaymentHistory_ReturnsPaymentsNewestFirst()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 5, creditAmount: 500);

        await RepayAsync(customer, 100);
        await RepayAsync(customer, 200);

        var history = await _sut.GetRepaymentHistoryAsync(customer.Id, _ownerId);

        Assert.Equal(2, history.Count);
        Assert.Equal(300m, history.Sum(p => p.Amount));
    }

    [Fact]
    public async Task OutstandingSummary_ListsOnlyCustomersWhoOwe()
    {
        var owing = await SeedCustomerAsync("Owes Money");
        var settled = await SeedCustomerAsync("Settled Up");
        var never = await SeedCustomerAsync("Never Borrowed");
        var product = await SeedProductAsync(price: 100);

        await SellOnCreditAsync(owing, product, quantity: 4, creditAmount: 400);
        await SellOnCreditAsync(settled, product, quantity: 2, creditAmount: 200);
        await RepayAsync(settled, 200);

        var summary = await _sut.GetOutstandingSummaryAsync(_ownerId);

        var entry = Assert.Single(summary);
        Assert.Equal(owing.Id, entry.CustomerId);
        Assert.Equal(400m, entry.OutstandingAmount);
        Assert.DoesNotContain(summary, s => s.CustomerId == settled.Id || s.CustomerId == never.Id);
    }

    [Fact]
    public async Task OutstandingSummary_AggregatesMultipleCreditsPerCustomer()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);
        await SellOnCreditAsync(customer, product, quantity: 3, creditAmount: 300);

        var entry = Assert.Single(await _sut.GetOutstandingSummaryAsync(_ownerId));

        Assert.Equal(500m, entry.OutstandingAmount);
        Assert.Equal(2, entry.OpenCreditCount);
    }

    // ---------- audit ----------

    [Fact]
    public async Task Repayment_WritesAuditEntry()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 2, creditAmount: 200);

        var payment = await RepayAsync(customer, 120);

        var audit = await _fixture.Context.AuditLogs.SingleOrDefaultAsync(
            a => a.Action == "CreditRepaymentRecorded" && a.EntityId == payment.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal(_ownerId, audit!.UserId);
        Assert.Contains(payment.ReceiptNumber, audit.NewValue);
    }

    [Fact]
    public async Task ReceiptNumbers_AreSequentialAndUnique()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100);
        await SellOnCreditAsync(customer, product, quantity: 10, creditAmount: 1000);

        var first = await RepayAsync(customer, 100);
        var second = await RepayAsync(customer, 100);
        var third = await RepayAsync(customer, 100);

        Assert.Equal("RCPT-000001", first.ReceiptNumber);
        Assert.Equal("RCPT-000002", second.ReceiptNumber);
        Assert.Equal("RCPT-000003", third.ReceiptNumber);
    }

    public void Dispose() => _fixture.Dispose();
}
