using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.CashRegisters;

public sealed class CashRegisterServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CashRegisterService _service;

    public CashRegisterServiceTests()
    {
        _service = new CashRegisterService(
            _fixture.Context,
            new PermissionEnforcer(_fixture.Context),
            new EfAuditLogger(_fixture.Context));
    }

    [Fact]
    public async Task Open_RejectsNegativeAmountAndDuplicateActiveSession()
    {
        var owner = await _fixture.SeedOwnerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.OpenAsync(new(-1m, owner.Id)));
        var opened = await _service.OpenAsync(new(500m, owner.Id));
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.OpenAsync(new(0m, owner.Id)));

        Assert.Equal(CashRegisterStatus.Open, opened.Status);
        Assert.Contains("already open", duplicate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task XReport_CalculatesOnlyPhysicalCashAndDoesNotCloseSession()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var customer = await SeedTransactionsAsync(owner.Id);
        await _service.OpenAsync(new(100m, owner.Id));
        await SeedActivityAfterOpenAsync(owner.Id, customer);

        var report = await _service.GetXReportAsync(owner.Id, 240m);

        Assert.Equal(225m, report.TotalSales);
        Assert.Equal(1, report.BillCount);
        Assert.Equal(100m, report.CashSales);
        Assert.Equal(50m, report.UpiSales);
        Assert.Equal(25m, report.CardSales);
        Assert.Equal(50m, report.CustomerCreditSales);
        Assert.Equal(15m, report.TotalReturns);
        Assert.Equal(10m, report.CashRefunds);
        Assert.Equal(30m, report.CashCreditRepayments);
        Assert.Equal(20m, report.CashIn);
        Assert.Equal(5m, report.CashOut);
        Assert.Equal(235m, report.ExpectedCash);
        Assert.Equal(5m, report.Variance);
        Assert.Equal(CashRegisterStatus.Open, report.Status);
        Assert.True((await _service.GetStatusAsync()).IsOpen);
    }

    [Fact]
    public async Task Movement_IsIdempotentAndRequiresReasonAndPermission()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        await _service.OpenAsync(new(0m, owner.Id));
        var operationId = Guid.NewGuid();

        var first = await _service.RecordMovementAsync(new(CashMovementType.CashIn, 25m, "Float top-up", owner.Id, operationId));
        var retry = await _service.RecordMovementAsync(new(CashMovementType.CashIn, 25m, "Float top-up", owner.Id, operationId));

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await _fixture.Context.CashMovements.CountAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RecordMovementAsync(
            new(CashMovementType.CashIn, 1m, " ", owner.Id, Guid.NewGuid())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.RecordMovementAsync(
            new(CashMovementType.CashOut, 1m, "Till expense", cashier.Id, Guid.NewGuid())));
    }

    [Fact]
    public async Task Close_StoresImmutableZSnapshotAndVariance()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(100m, owner.Id));
        var sale = new Sale
        {
            InvoiceNumber = "INV-Z-1", SaleDateUtc = DateTime.UtcNow, GrandTotal = 80m,
            Payments = [new Payment { Method = PaymentMethod.Cash, Amount = 80m }],
        };
        _fixture.Context.Sales.Add(sale);
        await _fixture.Context.SaveChangesAsync();

        var closed = await _service.CloseAsync(new(175m, owner.Id));
        sale.GrandTotal = 900m;
        sale.Payments.Single().Amount = 900m;
        await _fixture.Context.SaveChangesAsync();
        var reopenedZ = await _service.GetZReportAsync(closed.SessionId, owner.Id);

        Assert.Equal(CashRegisterStatus.Closed, closed.Status);
        Assert.Equal(180m, closed.ExpectedCash);
        Assert.Equal(175m, closed.ActualCash);
        Assert.Equal(-5m, closed.Variance);
        Assert.Equal(80m, reopenedZ.CashSales);
        Assert.Equal(180m, reopenedZ.ExpectedCash);
        Assert.False((await _service.GetStatusAsync()).IsOpen);
    }

    [Fact]
    public async Task ClosedSession_CannotBeClosedAgain_AndNewSessionCanOpen()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(0m, owner.Id));
        await _service.CloseAsync(new(0m, owner.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CloseAsync(new(0m, owner.Id)));
        var next = await _service.OpenAsync(new(10m, owner.Id));

        Assert.Equal(CashRegisterStatus.Open, next.Status);
        Assert.Equal(2, await _fixture.Context.CashRegisterSessions.CountAsync());
    }

    [Fact]
    public async Task Operations_CreateRequiredAuditEntries()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(20m, owner.Id));
        await _service.RecordMovementAsync(new(CashMovementType.CashIn, 5m, "Change", owner.Id, Guid.NewGuid()));
        await _service.GetXReportAsync(owner.Id);
        await _service.CloseAsync(new(25m, owner.Id));

        var actions = await _fixture.Context.AuditLogs.Select(x => x.Action).ToListAsync();
        Assert.Contains("CashRegisterOpened", actions);
        Assert.Contains("CashRegisterCashIn", actions);
        Assert.Contains("CashRegisterXReportViewed", actions);
        Assert.Contains("CashRegisterClosed", actions);
        Assert.Contains("CashRegisterZReportGenerated", actions);
    }

    [Theory]
    [InlineData(150, 0)]
    [InlineData(140, -10)]
    [InlineData(165, 15)]
    public async Task Close_RecordsMatchShortAndOverVariance(decimal counted, decimal variance)
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(100m, owner.Id));
        _fixture.Context.Sales.Add(new Sale
        {
            InvoiceNumber = $"INV-V-{counted}", SaleDateUtc = DateTime.UtcNow, GrandTotal = 50m,
            Payments = [new Payment { Method = PaymentMethod.Cash, Amount = 50m }],
        });
        await _fixture.Context.SaveChangesAsync();

        var report = await _service.CloseAsync(new(counted, owner.Id));

        Assert.Equal(150m, report.ExpectedCash);
        Assert.Equal(variance, report.Variance);
    }

    [Fact]
    public async Task NonCashRefund_DoesNotReduceExpectedCash()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(100m, owner.Id));
        var sale = new Sale
        {
            InvoiceNumber = "INV-UPI-REFUND", SaleDateUtc = DateTime.UtcNow, GrandTotal = 200m,
            Payments = [new Payment { Method = PaymentMethod.Upi, Amount = 200m }],
        };
        _fixture.Context.Sales.Add(sale);
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.SalesReturns.Add(new SalesReturn
        {
            ReturnNumber = "RET-UPI", SaleId = sale.Id, InvoiceNumberSnapshot = sale.InvoiceNumber,
            ReturnDateUtc = DateTime.UtcNow, TotalReturnAmount = 75m, RefundAmount = 75m,
            RefundMethod = RefundMethod.Upi, ProcessedByUserId = owner.Id,
        });
        await _fixture.Context.SaveChangesAsync();

        var report = await _service.GetCurrentReportAsync(owner.Id);

        Assert.Equal(75m, report.TotalReturns);
        Assert.Equal(0m, report.CashRefunds);
        Assert.Equal(100m, report.ExpectedCash);
    }

    [Fact]
    public async Task ManyTransactions_AreAggregatedWithoutChangingTenderRules()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(1000m, owner.Id));
        for (var i = 0; i < 200; i++)
        {
            _fixture.Context.Sales.Add(new Sale
            {
                InvoiceNumber = $"INV-BULK-{i:000}", SaleDateUtc = DateTime.UtcNow, GrandTotal = 30m,
                Payments =
                [
                    new Payment { Method = PaymentMethod.Cash, Amount = 10m },
                    new Payment { Method = PaymentMethod.Upi, Amount = 20m },
                ],
            });
        }
        await _fixture.Context.SaveChangesAsync();

        var report = await _service.GetCurrentReportAsync(owner.Id);

        Assert.Equal(200, report.BillCount);
        Assert.Equal(6000m, report.TotalSales);
        Assert.Equal(2000m, report.CashSales);
        Assert.Equal(4000m, report.UpiSales);
        Assert.Equal(3000m, report.ExpectedCash);
    }

    [Fact]
    public async Task SessionSpanningMidnight_KeepsOpeningBusinessDateAndIncludesLaterTransactions()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var session = await _service.OpenAsync(new(10m, owner.Id));
        var openingBusinessDate = DateTime.Today.AddDays(-1);
        session.BusinessDate = openingBusinessDate;
        session.OpenedAtUtc = DateTime.UtcNow.AddHours(-4);
        _fixture.Context.Sales.Add(new Sale
        {
            InvoiceNumber = "INV-AFTER-MIDNIGHT", SaleDateUtc = DateTime.UtcNow, GrandTotal = 25m,
            Payments = [new Payment { Method = PaymentMethod.Cash, Amount = 25m }],
        });
        await _fixture.Context.SaveChangesAsync();

        var report = await _service.GetCurrentReportAsync(owner.Id);

        Assert.Equal(openingBusinessDate, report.BusinessDate);
        Assert.Equal(35m, report.ExpectedCash);
    }

    [Fact]
    public async Task RecordMovementAsync_RejectsCashOut_ThatExceedsCurrentDrawerBalance()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(100m, owner.Id));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordMovementAsync(
            new(CashMovementType.CashOut, 100.01m, "Bank deposit", owner.Id, Guid.NewGuid())));

        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _fixture.Context.CashMovements.ToListAsync());
        Assert.Equal(100m, (await _service.GetStatusAsync()).ExpectedCash);
    }

    [Fact]
    public async Task RecordMovementAsync_AllowsCashOut_UpToExactlyTheDrawerBalance()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(100m, owner.Id));

        await _service.RecordMovementAsync(new(CashMovementType.CashOut, 100m, "Bank deposit", owner.Id, Guid.NewGuid()));

        Assert.Equal(0m, (await _service.GetStatusAsync()).ExpectedCash);
    }

    [Fact]
    public async Task RecordMovementAsync_CashOutLimit_AccountsForCashInAlreadyRecorded()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(100m, owner.Id));
        await _service.RecordMovementAsync(new(CashMovementType.CashIn, 50m, "Float top-up", owner.Id, Guid.NewGuid()));

        // 150 in the drawer now (100 opening + 50 cash-in); 150 should be accepted, 150.01 rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordMovementAsync(
            new(CashMovementType.CashOut, 150.01m, "Bank deposit", owner.Id, Guid.NewGuid())));
        await _service.RecordMovementAsync(new(CashMovementType.CashOut, 150m, "Bank deposit", owner.Id, Guid.NewGuid()));

        Assert.Equal(0m, (await _service.GetStatusAsync()).ExpectedCash);
    }

    [Fact]
    public async Task ClosedRegister_RejectsNewManualMovement()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _service.OpenAsync(new(0m, owner.Id));
        await _service.CloseAsync(new(0m, owner.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RecordMovementAsync(
            new(CashMovementType.CashIn, 10m, "Late movement", owner.Id, Guid.NewGuid())));
    }

    [Fact]
    public async Task OpenSessionAndHistory_SurviveNewDbContext()
    {
        using var fileFixture = new SqliteFileDbContextFixture();
        var owner = await fileFixture.SeedOwnerAsync();
        var first = new CashRegisterService(fileFixture.Context, new PermissionEnforcer(fileFixture.Context), new EfAuditLogger(fileFixture.Context));
        var opened = await first.OpenAsync(new(321m, owner.Id));
        fileFixture.Context.ChangeTracker.Clear();

        var options = new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite($"Data Source={fileFixture.Paths.DatabaseFilePath}").Options;
        await using var restartedContext = new KiranaDbContext(options);
        var restarted = new CashRegisterService(restartedContext, new PermissionEnforcer(restartedContext), new EfAuditLogger(restartedContext));

        var status = await restarted.GetStatusAsync();
        var history = await restarted.GetHistoryAsync(owner.Id);

        Assert.True(status.IsOpen);
        Assert.Equal(opened.Id, status.SessionId);
        Assert.Equal(321m, status.OpeningCash);
        Assert.Contains(history, x => x.Id == opened.Id && x.Status == CashRegisterStatus.Open);
    }

    [Fact]
    public async Task History_UsesLiveExpectedCashWhileOpen_AndClosingSnapshotAfterClose()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var session = await _service.OpenAsync(new(10_000m, owner.Id));
        var sale = new Sale
        {
            InvoiceNumber = "INV-HISTORY-LIVE-CASH",
            SaleDateUtc = DateTime.UtcNow,
            GrandTotal = 1_000m,
            Payments = [new Payment { Method = PaymentMethod.Cash, Amount = 1_000m }],
        };
        _fixture.Context.Sales.Add(sale);
        await _fixture.Context.SaveChangesAsync();
        await _service.RecordMovementAsync(new(
            CashMovementType.CashIn, 2_000m, "Add float", owner.Id, Guid.NewGuid()));
        await _service.RecordMovementAsync(new(
            CashMovementType.CashOut, 5_000m, "Bank deposit", owner.Id, Guid.NewGuid()));

        var openHistory = Assert.Single(await _service.GetHistoryAsync(owner.Id));
        var xReport = await _service.GetXReportAsync(owner.Id);

        Assert.Equal(session.Id, openHistory.Id);
        Assert.Equal(CashRegisterStatus.Open, openHistory.Status);
        Assert.Equal(8_000m, openHistory.ExpectedCash);
        Assert.Null(openHistory.ActualCash);
        Assert.Null(openHistory.Variance);
        Assert.Equal(8_000m, xReport.ExpectedCash);

        await _service.CloseAsync(new(8_000m, owner.Id));
        var closedHistory = Assert.Single(await _service.GetHistoryAsync(owner.Id));

        Assert.Equal(CashRegisterStatus.Closed, closedHistory.Status);
        Assert.Equal(8_000m, closedHistory.ExpectedCash);
        Assert.Equal(8_000m, closedHistory.ActualCash);
        Assert.Equal(0m, closedHistory.Variance);

        sale.Payments.Single().Amount = 9_000m;
        sale.GrandTotal = 9_000m;
        await _fixture.Context.SaveChangesAsync();
        var immutableHistory = Assert.Single(await _service.GetHistoryAsync(owner.Id));

        Assert.Equal(8_000m, immutableHistory.ExpectedCash);
        Assert.Equal(8_000m, immutableHistory.ActualCash);
        Assert.Equal(0m, immutableHistory.Variance);
    }

    private async Task<Customer> SeedTransactionsAsync(int ownerId)
    {
        var customer = new Customer { CustomerCode = "CASH-TEST", Name = "Register Customer", Phone = "9999999999" };
        _fixture.Context.Customers.Add(customer);
        await _fixture.Context.SaveChangesAsync();
        return customer;
    }

    private async Task SeedActivityAfterOpenAsync(int ownerId, Customer customer)
    {
        var sale = new Sale
        {
            InvoiceNumber = "INV-CASH-1", CustomerId = customer.Id, SaleDateUtc = DateTime.UtcNow,
            GrandTotal = 225m,
            Payments =
            [
                new Payment { Method = PaymentMethod.Cash, Amount = 100m },
                new Payment { Method = PaymentMethod.Upi, Amount = 50m },
                new Payment { Method = PaymentMethod.Card, Amount = 25m },
                new Payment { Method = PaymentMethod.CustomerCredit, Amount = 50m },
            ],
        };
        _fixture.Context.Sales.Add(sale);
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.SalesReturns.Add(new SalesReturn
        {
            ReturnNumber = "RET-CASH-1", SaleId = sale.Id, InvoiceNumberSnapshot = sale.InvoiceNumber,
            CustomerId = customer.Id, ReturnDateUtc = DateTime.UtcNow, TotalReturnAmount = 15m,
            RefundAmount = 10m, RefundMethod = RefundMethod.Cash, ProcessedByUserId = ownerId,
        });
        _fixture.Context.CreditPayments.Add(new CreditPayment
        {
            ReceiptNumber = "CP-CASH-1", CustomerId = customer.Id, Amount = 30m,
            Method = PaymentMethod.Cash, PaymentDateUtc = DateTime.UtcNow, RecordedByUserId = ownerId,
        });
        await _fixture.Context.SaveChangesAsync();
        await _service.RecordMovementAsync(new(CashMovementType.CashIn, 20m, "Petty cash returned", ownerId, Guid.NewGuid()));
        await _service.RecordMovementAsync(new(CashMovementType.CashOut, 5m, "Tea expense", ownerId, Guid.NewGuid()));
    }

    public void Dispose() => _fixture.Dispose();
}
