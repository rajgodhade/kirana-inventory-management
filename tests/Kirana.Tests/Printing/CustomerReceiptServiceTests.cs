using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Printing;

/// <summary>
/// Udhaar repayment receipts (PRD §31). Mirrors the Phase 5 invoice-printing guarantees: the
/// document is built from stored records, and printing — or failing to print — never mutates the
/// underlying payment.
/// </summary>
public class CustomerReceiptServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CustomerService _customerService;
    private readonly CustomerCreditService _creditService;
    private readonly CustomerReceiptService _sut;
    private readonly SaleService _saleService;
    private readonly int _ownerId;

    public CustomerReceiptServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _customerService = new CustomerService(_fixture.Context, seq, audit);
        _creditService = new CustomerCreditService(_fixture.Context, seq, audit, enforcer);
        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);
        _sut = new CustomerReceiptService(_fixture.Context, _creditService, audit);
    }

    private async Task<(Customer Customer, Sale Sale)> SeedCreditSaleAsync(decimal creditAmount = 500)
    {
        var customer = await _customerService.CreateAsync(new CreateCustomerRequest
        {
            Name = "Receipt Customer", Phone = "9800000001",
        });

        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12], Name = "Receipt Product",
            Unit = UnitOfMeasure.Piece, PurchasePrice = 60, Mrp = 110, SellingPrice = 100, IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100 });
        await _fixture.Context.SaveChangesAsync();

        var sale = await _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = creditAmount / 100 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = creditAmount }],
            CashierUserId = _ownerId,
        });

        return (customer, sale);
    }

    private Task<CreditPayment> RepayAsync(Customer customer, decimal amount, PaymentMethod method = PaymentMethod.Cash) =>
        _creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
        {
            CustomerId = customer.Id, Amount = amount, Method = method, RecordedByUserId = _ownerId,
        });

    [Fact]
    public async Task GetReceiptAsync_PopulatesStoreCustomerAndPaymentDetails()
    {
        var (customer, _) = await SeedCreditSaleAsync();
        var payment = await RepayAsync(customer, 200);

        var receipt = await _sut.GetReceiptAsync(payment.Id, _ownerId);

        Assert.Equal("Test Store", receipt.StoreName);
        Assert.Equal(customer.CustomerCode, receipt.CustomerCode);
        Assert.Equal("Receipt Customer", receipt.CustomerName);
        Assert.Equal("9800000001", receipt.CustomerPhone);
        Assert.Equal(payment.ReceiptNumber, receipt.ReceiptNumber);
        Assert.Equal(200m, receipt.AmountPaid);
        Assert.Equal(nameof(PaymentMethod.Cash), receipt.PaymentMethod);
    }

    [Fact]
    public async Task GetReceiptAsync_ShowsBalanceBeforeAndAfter()
    {
        var (customer, _) = await SeedCreditSaleAsync(creditAmount: 500);
        var payment = await RepayAsync(customer, 200);

        var receipt = await _sut.GetReceiptAsync(payment.Id, _ownerId);

        Assert.Equal(500m, receipt.BalanceBefore);
        Assert.Equal(300m, receipt.BalanceAfter);
    }

    [Fact]
    public async Task GetReceiptAsync_ListsAllocationsAgainstOriginatingInvoices()
    {
        var (customer, sale) = await SeedCreditSaleAsync(creditAmount: 300);
        var payment = await RepayAsync(customer, 120);

        var receipt = await _sut.GetReceiptAsync(payment.Id, _ownerId);

        var line = Assert.Single(receipt.Allocations);
        Assert.Equal(sale.InvoiceNumber, line.InvoiceNumber);
        Assert.Equal(120m, line.AmountApplied);
        Assert.Equal(180m, line.RemainingOnThatCredit);
    }

    [Fact]
    public async Task GetReceiptAsync_RecordsReceivedByName()
    {
        var (customer, _) = await SeedCreditSaleAsync();
        var payment = await RepayAsync(customer, 100);

        var receipt = await _sut.GetReceiptAsync(payment.Id, _ownerId);

        Assert.Equal("Test Owner", receipt.ReceivedByName);
    }

    [Fact]
    public async Task GetReceiptAsync_HandlesUpiReferenceNumber()
    {
        var (customer, _) = await SeedCreditSaleAsync();
        var payment = await _creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
        {
            CustomerId = customer.Id, Amount = 150, Method = PaymentMethod.Upi,
            ReferenceNumber = "UPI-REF-99", RecordedByUserId = _ownerId,
        });

        var receipt = await _sut.GetReceiptAsync(payment.Id, _ownerId);

        Assert.Equal(nameof(PaymentMethod.Upi), receipt.PaymentMethod);
        Assert.Equal("UPI-REF-99", receipt.ReferenceNumber);
    }

    [Fact]
    public async Task GetReceiptAsync_Throws_WhenPaymentDoesNotExist() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetReceiptAsync(9999, _ownerId));

    [Fact]
    public async Task GetReceiptAsync_IsRepeatable_ForReprint()
    {
        var (customer, _) = await SeedCreditSaleAsync();
        var payment = await RepayAsync(customer, 250);

        var first = await _sut.GetReceiptAsync(payment.Id, _ownerId);
        var second = await _sut.GetReceiptAsync(payment.Id, _ownerId);

        Assert.Equal(first.ReceiptNumber, second.ReceiptNumber);
        Assert.Equal(first.AmountPaid, second.AmountPaid);
        Assert.Equal(first.BalanceAfter, second.BalanceAfter);
    }

    [Fact]
    public async Task BuildingReceipt_DoesNotMutateThePayment()
    {
        var (customer, _) = await SeedCreditSaleAsync();
        var payment = await RepayAsync(customer, 300);
        var balanceBefore = (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id)).CreditBalance;

        await _sut.GetReceiptAsync(payment.Id, _ownerId);

        var persisted = await _fixture.Context.CreditPayments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        Assert.Equal(300m, persisted.Amount);
        Assert.Equal(balanceBefore, (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id)).CreditBalance);
    }

    [Fact]
    public async Task LogPrintAsync_WritesAuditEntry()
    {
        var (customer, _) = await SeedCreditSaleAsync();
        var payment = await RepayAsync(customer, 100);

        await _sut.LogPrintAsync(payment.Id, _ownerId);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(
            a => a.Action == "CreditReceiptPrinted" && a.EntityId == payment.Id.ToString()));
    }

    public void Dispose() => _fixture.Dispose();
}
