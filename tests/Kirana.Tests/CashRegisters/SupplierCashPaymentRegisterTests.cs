using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.CashRegisters;

/// <summary>
/// Phase 12 follow-up: a supplier payment made in physical cash must reduce the drawer on its own,
/// without the user also recording a manual Cash Out (which would double-count it).
/// </summary>
public sealed class SupplierCashPaymentRegisterTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CashRegisterService _register;
    private readonly PurchaseService _purchases;
    private readonly SupplierService _suppliers;
    private readonly int _ownerId;

    public SupplierCashPaymentRegisterTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        var sequences = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        _register = new CashRegisterService(_fixture.Context, permissions, audit);
        _purchases = new PurchaseService(_fixture.Context, sequences, audit, permissions);
        _suppliers = new SupplierService(_fixture.Context, sequences, audit, permissions);
    }

    [Fact]
    public async Task CashSupplierPayment_ReducesExpectedCash()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));

        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(3_000m, report.SupplierCashPayments);
        Assert.Equal(12_000m, report.ExpectedCash);
    }

    [Theory]
    [InlineData(PaymentMethod.Upi)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.CustomerCredit)]
    public async Task NonCashSupplierPayment_LeavesExpectedCashUntouched(PaymentMethod method)
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));

        await PayAsync(supplier.Id, 3_000m, method);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.SupplierCashPayments);
        Assert.Equal(15_000m, report.ExpectedCash);
        Assert.DoesNotContain(report.Movements, m => m.Type == CashRegisterMovementKind.SupplierPayment);

        // The ledger still moves for non-cash methods — only the physical drawer is unaffected.
        var reloaded = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        Assert.Equal(2_000m, reloaded.OutstandingBalance);
    }

    [Fact]
    public async Task CashSupplierPayment_AppearsAsSupplierPayment_NotGenericCashOut()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m, "Kumar Supplier");
        await _register.OpenAsync(new(15_000m, _ownerId));

        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        var row = Assert.Single(report.Movements);
        Assert.Equal(CashRegisterMovementKind.SupplierPayment, row.Type);
        Assert.Equal(3_000m, row.Amount);
        Assert.Equal("Kumar Supplier", row.Reason);
        Assert.Equal(0m, report.CashOut);
    }

    [Fact]
    public async Task CashSupplierPayment_CreatesNoCashMovementRow_SoItCannotDoubleCount()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));

        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        // The supplier payment *is* the cash record; mirroring it into CashMovements would let the
        // same rupees be subtracted twice.
        Assert.Empty(await _fixture.Context.CashMovements.ToListAsync());
        Assert.Equal(1, await _fixture.Context.SupplierPayments.CountAsync());
        Assert.Equal(12_000m, (await _register.GetCurrentReportAsync(_ownerId)).ExpectedCash);
    }

    [Fact]
    public async Task RepeatedPaymentsToSameSupplier_EachCountExactlyOnce()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(10_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));

        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);
        await PayAsync(supplier.Id, 2_000m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(5_000m, report.SupplierCashPayments);
        Assert.Equal(10_000m, report.ExpectedCash);
        Assert.Equal(2, report.Movements.Count(m => m.Type == CashRegisterMovementKind.SupplierPayment));
    }

    [Fact]
    public async Task RecalculatingTheReport_IsStable_AndDoesNotAccumulate()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));
        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        var first = await _register.GetCurrentReportAsync(_ownerId);
        var second = await _register.GetCurrentReportAsync(_ownerId);
        var status = await _register.GetStatusAsync();

        Assert.Equal(first.ExpectedCash, second.ExpectedCash);
        Assert.Equal(12_000m, status.ExpectedCash);
        Assert.Single(second.Movements);
    }

    [Fact]
    public async Task EndToEnd_MatchesTheWorkedExample()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m, "Kumar Supplier");
        await _register.OpenAsync(new(15_000m, _ownerId));
        _fixture.Context.Sales.Add(new Sale
        {
            InvoiceNumber = "INV-E2E-1", SaleDateUtc = DateTime.UtcNow, GrandTotal = 2_000m,
            Payments = [new Payment { Method = PaymentMethod.Cash, Amount = 2_000m }],
        });
        await _fixture.Context.SaveChangesAsync();
        await _register.RecordMovementAsync(new(CashMovementType.CashIn, 7_000m, "Owner cash deposit", _ownerId, Guid.NewGuid()));
        await _register.RecordMovementAsync(new(CashMovementType.CashOut, 5_000m, "Bank deposit", _ownerId, Guid.NewGuid()));

        Assert.Equal(19_000m, (await _register.GetCurrentReportAsync(_ownerId)).ExpectedCash);

        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        var report = await _register.GetXReportAsync(_ownerId);
        Assert.Equal(15_000m, report.OpeningCash);
        Assert.Equal(2_000m, report.CashSales);
        Assert.Equal(0m, report.CashCreditRepayments);
        Assert.Equal(7_000m, report.CashIn);
        Assert.Equal(0m, report.CashRefunds);
        Assert.Equal(3_000m, report.SupplierCashPayments);
        Assert.Equal(5_000m, report.CashOut);
        Assert.Equal(16_000m, report.ExpectedCash);

        var reloaded = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        Assert.Equal(2_000m, reloaded.OutstandingBalance);
        Assert.Single(report.Movements, m => m.Type == CashRegisterMovementKind.SupplierPayment);
    }

    [Fact]
    public async Task ClosedRegister_ZReport_KeepsSupplierCashPaymentsInTheSnapshot()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));
        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        var closed = await _register.CloseAsync(new(12_000m, _ownerId));

        Assert.Equal(3_000m, closed.SupplierCashPayments);
        Assert.Equal(12_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);

        var z = await _register.GetZReportAsync(closed.SessionId, _ownerId);
        Assert.Equal(3_000m, z.SupplierCashPayments);
        Assert.Equal(12_000m, z.ExpectedCash);
        Assert.Contains(z.Movements, m => m.Type == CashRegisterMovementKind.SupplierPayment && m.Amount == 3_000m);
    }

    [Fact]
    public async Task RegisterHistory_ReflectsSupplierCashPayments_LiveThenFrozen()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));
        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        var open = Assert.Single(await _register.GetHistoryAsync(_ownerId));
        Assert.Equal(12_000m, open.ExpectedCash);

        await _register.CloseAsync(new(12_000m, _ownerId));
        var closed = Assert.Single(await _register.GetHistoryAsync(_ownerId));
        Assert.Equal(12_000m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);
    }

    [Fact]
    public async Task PaymentsBeforeTheRegisterOpened_AreOutsideTheSessionWindow()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);

        await _register.OpenAsync(new(15_000m, _ownerId));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.SupplierCashPayments);
        Assert.Equal(15_000m, report.ExpectedCash);
    }

    [Fact]
    public async Task CashSupplierPayment_RequiresOnlyPurchasePermission_NotCashOutPermission()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));
        var cashier = await _fixture.SeedCashierAsync();

        // A cashier has neither permission, so this must still be refused by the purchase rule.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PayAsync(supplier.Id, 100m, PaymentMethod.Cash, cashier.Id));

        // The owner holds PurchasesManage; no separate CashRegisterCashOut grant is consulted.
        await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);
        Assert.Equal(12_000m, (await _register.GetCurrentReportAsync(_ownerId)).ExpectedCash);
    }

    [Fact]
    public async Task OffsettingNegativeAdjustment_RestoresTheDrawer()
    {
        var supplier = await SeedSupplierWithOutstandingAsync(5_000m);
        await _register.OpenAsync(new(15_000m, _ownerId));
        var payment = await PayAsync(supplier.Id, 3_000m, PaymentMethod.Cash);
        Assert.Equal(12_000m, (await _register.GetCurrentReportAsync(_ownerId)).ExpectedCash);

        // The service exposes no reversal API, so a correction is modelled the only way the
        // current architecture allows: an offsetting SupplierPayment row. Historical rows are
        // never deleted, and the derived drawer figure nets back to zero impact.
        _fixture.Context.SupplierPayments.Add(new SupplierPayment
        {
            SupplierId = supplier.Id,
            Amount = -payment.Amount,
            Method = PaymentMethod.Cash,
            Notes = "Reversal of overpayment",
            RecordedByUserId = _ownerId,
        });
        await _fixture.Context.SaveChangesAsync();

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.SupplierCashPayments);
        Assert.Equal(15_000m, report.ExpectedCash);
        Assert.Equal(2, await _fixture.Context.SupplierPayments.CountAsync());
    }

    [Fact]
    public async Task InitialCashPaymentAtPurchaseEntry_AlsoReducesTheDrawer()
    {
        await _register.OpenAsync(new(15_000m, _ownerId));
        var supplier = await _suppliers.CreateAsync(new CreateSupplierRequest { Name = "Direct Pay", PerformedByUserId = _ownerId });
        var product = await SeedProductAsync();

        await _purchases.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            CreatedByUserId = _ownerId,
            AmountPaid = 1_200m,
            PaymentMethod = PaymentMethod.Cash,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 10, UnitPrice = 200m }],
        });

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(1_200m, report.SupplierCashPayments);
        Assert.Equal(13_800m, report.ExpectedCash);
    }

    [Fact]
    public async Task ManualCashInAndCashOut_BehaveExactlyAsBefore()
    {
        await _register.OpenAsync(new(15_000m, _ownerId));

        await _register.RecordMovementAsync(new(CashMovementType.CashIn, 2_000m, "Float", _ownerId, Guid.NewGuid()));
        await _register.RecordMovementAsync(new(CashMovementType.CashOut, 1_000m, "Bank deposit", _ownerId, Guid.NewGuid()));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(2_000m, report.CashIn);
        Assert.Equal(1_000m, report.CashOut);
        Assert.Equal(0m, report.SupplierCashPayments);
        Assert.Equal(16_000m, report.ExpectedCash);
    }

    private async Task<Supplier> SeedSupplierWithOutstandingAsync(decimal outstanding, string name = "Kumar Supplier")
    {
        var supplier = await _suppliers.CreateAsync(new CreateSupplierRequest { Name = name, PerformedByUserId = _ownerId });
        supplier.OutstandingBalance = outstanding;
        await _fixture.Context.SaveChangesAsync();
        return supplier;
    }

    private Task<SupplierPayment> PayAsync(int supplierId, decimal amount, PaymentMethod method, int? userId = null) =>
        _purchases.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplierId,
            Amount = amount,
            Method = method,
            RecordedByUserId = userId ?? _ownerId,
        });

    private async Task<Product> SeedProductAsync()
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Register Test Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 200m,
            Mrp = 260m,
            SellingPrice = 250m,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    public void Dispose() => _fixture.Dispose();
}
