using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Application.Expenses;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.CashRegisters;

/// <summary>
/// Phase 16A-1: an expense paid in physical cash must reduce the drawer on its own, without the
/// user also recording a manual Cash Out (which would double-count it) — the same treatment
/// supplier cash payments already get.
///
/// <para>The distinguishing rule these tests exist to pin: session membership uses
/// <c>Expense.CreatedAtUtc</c> (when the record — and the cash — actually left) and NOT
/// <c>ExpenseDateUtc</c> (a user-editable accounting date that may be backdated). An implementation
/// that keys on the accounting date passes almost everything here and fails
/// <see cref="BackdatedAccountingDate_StillBelongsToTheSessionItWasRecordedIn"/>.</para>
/// </summary>
public sealed class CashExpenseRegisterTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CashRegisterService _register;
    private readonly ExpenseService _expenses;
    private readonly ExpenseCategoryService _categories;
    private readonly int _ownerId;

    public CashExpenseRegisterTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        var sequences = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        _register = new CashRegisterService(_fixture.Context, permissions, audit);
        _categories = new ExpenseCategoryService(_fixture.Context, audit, permissions);
        _expenses = new ExpenseService(_fixture.Context, sequences, audit, permissions);
    }

    // ---------------- calculation ----------------

    [Fact]
    public async Task CashExpense_ReducesExpectedCash()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));

        await CreateExpenseAsync(500m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(9_500m, report.ExpectedCash);
    }

    [Theory]
    [InlineData(PaymentMethod.Upi)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.CustomerCredit)]
    public async Task NonCashExpense_LeavesTheDrawerUntouched(PaymentMethod method)
    {
        await _register.OpenAsync(new(10_000m, _ownerId));

        await CreateExpenseAsync(500m, method);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.CashExpenses);
        Assert.Equal(10_000m, report.ExpectedCash);
        Assert.DoesNotContain(report.Movements, m => m.Type == CashRegisterMovementKind.Expense);
    }

    [Fact]
    public async Task MultipleCashExpenses_Accumulate()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));

        await CreateExpenseAsync(500m, PaymentMethod.Cash);
        await CreateExpenseAsync(250m, PaymentMethod.Cash);
        await CreateExpenseAsync(125.50m, PaymentMethod.Cash);
        await CreateExpenseAsync(9_000m, PaymentMethod.Upi);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(875.50m, report.CashExpenses);
        Assert.Equal(9_124.50m, report.ExpectedCash);
    }

    [Fact]
    public async Task TheWorkedExampleFromTheSpec_Reconciles()
    {
        // Opening 10,000 + cash sale 2,000 - cash expense 500 = 11,500 (not 12,000).
        await _register.OpenAsync(new(10_000m, _ownerId));
        await RecordCashSaleAsync(2_000m);
        await CreateExpenseAsync(500m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(2_000m, report.CashSales);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(11_500m, report.ExpectedCash);
    }

    [Fact]
    public async Task CashExpense_AppearsInTheXReport()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        await CreateExpenseAsync(500m, PaymentMethod.Cash, category: "Electricity");

        var report = await _register.GetXReportAsync(_ownerId);

        Assert.Equal(500m, report.CashExpenses);
        var row = Assert.Single(report.Movements);
        Assert.Equal(CashRegisterMovementKind.Expense, row.Type);
        Assert.Equal(500m, row.Amount);
        Assert.Contains("Electricity", row.Reason);
    }

    [Fact]
    public async Task CashExpense_IsFrozenIntoTheZSnapshot()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        await CreateExpenseAsync(500m, PaymentMethod.Cash);

        var closed = await _register.CloseAsync(new(9_500m, _ownerId));

        Assert.Equal(500m, closed.CashExpenses);
        Assert.Equal(9_500m, closed.ExpectedCash);
        Assert.Equal(0m, closed.Variance);

        var persisted = await _fixture.Context.CashRegisterSessions.AsNoTracking()
            .SingleAsync(s => s.Id == closed.SessionId);
        Assert.Equal(500m, persisted.CashExpenses);
    }

    [Fact]
    public async Task CashExpense_TightensTheCashOutOverdraftGuard()
    {
        await _register.OpenAsync(new(1_000m, _ownerId));
        await CreateExpenseAsync(400m, PaymentMethod.Cash);

        // Only ₹600 is physically left, so ₹700 must be refused...
        var tooMuch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _register.RecordMovementAsync(new(CashMovementType.CashOut, 700m, "Bank drop", _ownerId, Guid.NewGuid())));
        Assert.Contains("exceeds", tooMuch.Message);

        // ...while exactly ₹600 is allowed.
        await _register.RecordMovementAsync(new(CashMovementType.CashOut, 600m, "Bank drop", _ownerId, Guid.NewGuid()));
        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.ExpectedCash);
    }

    [Theory]
    [InlineData(9_500, 0)]      // exact
    [InlineData(9_300, -200)]   // short
    [InlineData(9_800, 300)]    // over
    public async Task VarianceStaysCorrect_WithACashExpenseInPlay(decimal counted, decimal expectedVariance)
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        await CreateExpenseAsync(500m, PaymentMethod.Cash);

        var closed = await _register.CloseAsync(new(counted, _ownerId));

        Assert.Equal(9_500m, closed.ExpectedCash);
        Assert.Equal(expectedVariance, closed.Variance);
    }

    // ---------------- session membership ----------------

    [Fact]
    public async Task ExpenseRecordedBeforeTheRegisterOpened_IsOutsideTheSession()
    {
        // Seeded directly, not through ExpenseService: from Phase 16A-2 the service refuses to
        // create a cash expense while no register is open. The window rule this test covers still
        // has to hold for rows that reach the table another way — data written before 16A-2, or a
        // restored backup — so the calculation is exercised rather than the guard.
        await SeedCashExpenseOutsideAnySessionAsync(500m, DateTime.UtcNow.AddHours(-3));
        await _register.OpenAsync(new(10_000m, _ownerId));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.CashExpenses);
        Assert.Equal(10_000m, report.ExpectedCash);
    }

    [Fact]
    public async Task ExpenseRecordedAfterTheRegisterClosed_IsOutsideTheClosedSession()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var closed = await _register.CloseAsync(new(10_000m, _ownerId));
        Assert.Equal(0m, closed.CashExpenses);

        await SeedCashExpenseOutsideAnySessionAsync(500m, DateTime.UtcNow);

        // The frozen snapshot must not move now that a later expense exists.
        var reread = await _register.GetZReportAsync(closed.SessionId, _ownerId);
        Assert.Equal(0m, reread.CashExpenses);
        Assert.Equal(10_000m, reread.ExpectedCash);
        Assert.Equal(0m, reread.Variance);
    }

    [Fact]
    public async Task BackdatedAccountingDate_StillBelongsToTheSessionItWasRecordedIn()
    {
        // The spec's example: accounting date last Tuesday, actually recorded during today's
        // session. The cash left the drawer today, so today's register owns it.
        await _register.OpenAsync(new(10_000m, _ownerId));

        await CreateExpenseAsync(500m, PaymentMethod.Cash, accountingDate: DateTime.UtcNow.AddDays(-7));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(9_500m, report.ExpectedCash);
    }

    [Fact]
    public async Task AFutureAccountingDate_DoesNotPushTheExpenseOutOfTheSession()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));

        await CreateExpenseAsync(500m, PaymentMethod.Cash, accountingDate: DateTime.UtcNow.AddDays(30));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
    }

    [Fact]
    public async Task EditingTheAccountingDate_NeverMovesTheExpenseBetweenSessions()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);

        // Drag the accounting date years away — membership must not care.
        await _expenses.UpdateAsync(expense.Id, new UpdateExpenseRequest
        {
            ExpenseCategoryId = expense.ExpenseCategoryId,
            Amount = 500m,
            ExpenseDateUtc = DateTime.UtcNow.AddYears(-2),
            PaymentMethod = PaymentMethod.Cash,
            PerformedByUserId = _ownerId,
        });

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(9_500m, report.ExpectedCash);
    }

    [Fact]
    public async Task SessionSpanningMidnight_IncludesCashExpensesRecordedAfterMidnight()
    {
        var session = await _register.OpenAsync(new(10_000m, _ownerId));
        var openedAt = DateTime.UtcNow.AddHours(-5);
        session.OpenedAtUtc = openedAt;
        session.BusinessDate = DateTime.Now.Date.AddDays(-1);
        await _fixture.Context.SaveChangesAsync();

        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);
        await SetCreatedAtAsync(expense.Id, openedAt.AddHours(4));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(session.BusinessDate, report.BusinessDate);
    }

    // ---------------- editing while the register is open ----------------

    [Fact]
    public async Task RaisingTheAmount_LowersExpectedCashFurther()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);
        Assert.Equal(9_500m, (await _register.GetCurrentReportAsync(_ownerId)).ExpectedCash);

        await UpdateAsync(expense, 700m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(700m, report.CashExpenses);
        Assert.Equal(9_300m, report.ExpectedCash);
    }

    [Fact]
    public async Task SwitchingCashToUpi_ReturnsTheMoneyToTheDrawer()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);

        await UpdateAsync(expense, 500m, PaymentMethod.Upi);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.CashExpenses);
        Assert.Equal(10_000m, report.ExpectedCash);
    }

    [Fact]
    public async Task SwitchingUpiToCash_TakesTheMoneyOutOfTheDrawer()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Upi);
        Assert.Equal(10_000m, (await _register.GetCurrentReportAsync(_ownerId)).ExpectedCash);

        await UpdateAsync(expense, 500m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(9_500m, report.ExpectedCash);
    }

    [Fact]
    public async Task DeletingACashExpense_RestoresTheDrawer()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);

        await _expenses.DeleteAsync(expense.Id, _ownerId);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.CashExpenses);
        Assert.Equal(10_000m, report.ExpectedCash);
        Assert.Empty(report.Movements);
    }

    [Fact]
    public async Task ASequenceOfEdits_LandsOnTheCurrentAuthoritativeState()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);

        await UpdateAsync(expense, 700m, PaymentMethod.Cash);
        await UpdateAsync(expense, 700m, PaymentMethod.Upi);
        await UpdateAsync(expense, 250m, PaymentMethod.Cash);
        await UpdateAsync(expense, 300m, PaymentMethod.Cash);

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(300m, report.CashExpenses);
        Assert.Equal(9_700m, report.ExpectedCash);
        Assert.Single(report.Movements);
    }

    // ---------------- closed-register protection ----------------

    [Fact]
    public async Task ACashExpenseInAClosedSession_CannotBeEdited()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);
        var closed = await _register.CloseAsync(new(9_500m, _ownerId));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateAsync(expense, 900m, PaymentMethod.Cash));
        Assert.Contains("closed register", error.Message);

        var reread = await _register.GetZReportAsync(closed.SessionId, _ownerId);
        Assert.Equal(500m, reread.CashExpenses);
        Assert.Equal(9_500m, reread.ExpectedCash);
        Assert.Equal(0m, reread.Variance);
    }

    [Fact]
    public async Task ACashExpenseInAClosedSession_CannotBeDeleted()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Cash);
        var closed = await _register.CloseAsync(new(9_500m, _ownerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _expenses.DeleteAsync(expense.Id, _ownerId));

        Assert.NotNull(await _fixture.Context.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == expense.Id));
        Assert.Equal(500m, (await _register.GetZReportAsync(closed.SessionId, _ownerId)).CashExpenses);
    }

    [Fact]
    public async Task ANonCashExpenseInAClosedSession_CannotBeFlippedToCash()
    {
        // Otherwise cash could be injected into a session whose Z report is already frozen.
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Upi);
        await _register.CloseAsync(new(10_000m, _ownerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateAsync(expense, 500m, PaymentMethod.Cash));
    }

    [Fact]
    public async Task ANonCashExpenseInAClosedSession_RemainsFreelyEditable()
    {
        // It never touched the drawer, so nothing about the reconciliation depends on it.
        await _register.OpenAsync(new(10_000m, _ownerId));
        var expense = await CreateExpenseAsync(500m, PaymentMethod.Upi);
        await _register.CloseAsync(new(10_000m, _ownerId));

        await UpdateAsync(expense, 650m, PaymentMethod.Card);

        var reloaded = await _fixture.Context.Expenses.AsNoTracking().SingleAsync(e => e.Id == expense.Id);
        Assert.Equal(650m, reloaded.Amount);
        Assert.Equal(PaymentMethod.Card, reloaded.PaymentMethod);
    }

    [Fact]
    public async Task ARejectedHistoricalEdit_LeavesTheCurrentOpenSessionAlone()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        var historical = await CreateExpenseAsync(500m, PaymentMethod.Cash);
        await _register.CloseAsync(new(9_500m, _ownerId));

        await _register.OpenAsync(new(2_000m, _ownerId));
        await CreateExpenseAsync(100m, PaymentMethod.Cash);

        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateAsync(historical, 900m, PaymentMethod.Cash));

        var current = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(100m, current.CashExpenses);
        Assert.Equal(1_900m, current.ExpectedCash);
    }

    [Fact]
    public async Task AnExpenseRecordedBetweenSessions_StaysEditable()
    {
        // Nothing reconciled it, so the closed-session guard has nothing to protect and the edit is
        // allowed. Phase 16A-2 stops NEW such expenses being created, but rows that predate it (or
        // arrive by restore) still exist, so this seeds one directly and proves it stays editable.
        await _register.OpenAsync(new(10_000m, _ownerId));
        await _register.CloseAsync(new(10_000m, _ownerId));

        var orphan = await SeedCashExpenseOutsideAnySessionAsync(500m, DateTime.UtcNow);
        await UpdateAsync(orphan, 750m, PaymentMethod.Cash);

        var reloaded = await _fixture.Context.Expenses.AsNoTracking().SingleAsync(e => e.Id == orphan.Id);
        Assert.Equal(750m, reloaded.Amount);
    }

    // ---------------- anti-double-counting ----------------

    [Fact]
    public async Task ACashExpense_CreatesNoCashMovementRow_SoItCannotDoubleCount()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));

        await CreateExpenseAsync(500m, PaymentMethod.Cash);

        Assert.Empty(await _fixture.Context.CashMovements.AsNoTracking().ToListAsync());
        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(0m, report.CashOut);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(9_500m, report.ExpectedCash);
    }

    [Fact]
    public async Task RecalculatingTheReport_IsStable_AndDoesNotAccumulate()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        await CreateExpenseAsync(500m, PaymentMethod.Cash);

        var first = await _register.GetCurrentReportAsync(_ownerId);
        var second = await _register.GetCurrentReportAsync(_ownerId);
        var third = await _register.GetCurrentReportAsync(_ownerId);

        Assert.Equal(500m, first.CashExpenses);
        Assert.Equal(first.CashExpenses, second.CashExpenses);
        Assert.Equal(second.CashExpenses, third.CashExpenses);
        Assert.Equal(9_500m, third.ExpectedCash);
    }

    [Fact]
    public async Task ExpensesAndManualCashOut_AreCountedSeparately_NotMerged()
    {
        await _register.OpenAsync(new(10_000m, _ownerId));
        await CreateExpenseAsync(500m, PaymentMethod.Cash);
        await _register.RecordMovementAsync(new(CashMovementType.CashOut, 300m, "Bank drop", _ownerId, Guid.NewGuid()));

        var report = await _register.GetCurrentReportAsync(_ownerId);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(300m, report.CashOut);
        Assert.Equal(9_200m, report.ExpectedCash);
        Assert.Equal(2, report.Movements.Count);
    }

    // ---------------- concurrency ----------------

    [Fact]
    public async Task AnExpenseCommittedByADifferentDbContext_IsVisibleToTheCalculation()
    {
        using var fileFixture = new SqliteFileDbContextFixture();
        var owner = await fileFixture.SeedOwnerAsync();
        var registerA = new CashRegisterService(
            fileFixture.Context, new PermissionEnforcer(fileFixture.Context), new EfAuditLogger(fileFixture.Context));
        await registerA.OpenAsync(new(10_000m, owner.Id));

        var options = new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite($"Data Source={fileFixture.Paths.DatabaseFilePath}").Options;
        await using (var contextB = new KiranaDbContext(options))
        {
            var categoriesB = new ExpenseCategoryService(contextB, new EfAuditLogger(contextB), new PermissionEnforcer(contextB));
            var expensesB = new ExpenseService(
                contextB, new EfSequenceGenerator(contextB), new EfAuditLogger(contextB), new PermissionEnforcer(contextB));
            var category = await categoriesB.CreateAsync(new CreateExpenseCategoryRequest { Name = "Rent", PerformedByUserId = owner.Id });
            await expensesB.CreateAsync(new CreateExpenseRequest
            {
                ExpenseCategoryId = category.Id, Amount = 500m, PaymentMethod = PaymentMethod.Cash, PerformedByUserId = owner.Id,
            });
        }

        // Context A never saw that write. A stale identity-map read would report 10,000.
        var report = await registerA.GetCurrentReportAsync(owner.Id);
        Assert.Equal(500m, report.CashExpenses);
        Assert.Equal(9_500m, report.ExpectedCash);
    }

    // ---------------- authorization ----------------

    [Fact]
    public async Task RecordingACashExpense_StillRequiresOnlyTheExpensePermission()
    {
        // A cashier holds neither ExpensesManage nor CashRegisterCashOut; nothing about routing an
        // expense through the drawer may change which permission governs it.
        var cashier = await _fixture.Context.SeedCashierAsync();
        await _register.OpenAsync(new(10_000m, _ownerId));
        var category = await CategoryAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _expenses.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = category.Id, Amount = 500m, PaymentMethod = PaymentMethod.Cash, PerformedByUserId = cashier.Id,
        }));

        Assert.Equal(0m, (await _register.GetCurrentReportAsync(_ownerId)).CashExpenses);
    }

    // ---------------- helpers ----------------

    private async Task<ExpenseCategory> CategoryAsync(string name = "Rent")
    {
        var existing = await _fixture.Context.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Name == name);
        return existing ?? await _categories.CreateAsync(new CreateExpenseCategoryRequest { Name = name, PerformedByUserId = _ownerId });
    }

    private async Task<Expense> CreateExpenseAsync(
        decimal amount, PaymentMethod method, string category = "Rent", DateTime? accountingDate = null)
    {
        var expenseCategory = await CategoryAsync(category);
        return await _expenses.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = expenseCategory.Id,
            Amount = amount,
            PaymentMethod = method,
            ExpenseDateUtc = accountingDate,
            PerformedByUserId = _ownerId,
        });
    }

    private Task<Expense> UpdateAsync(Expense expense, decimal amount, PaymentMethod method) =>
        _expenses.UpdateAsync(expense.Id, new UpdateExpenseRequest
        {
            ExpenseCategoryId = expense.ExpenseCategoryId,
            Amount = amount,
            ExpenseDateUtc = expense.ExpenseDateUtc,
            PaymentMethod = method,
            PerformedByUserId = _ownerId,
        });

    /// <summary>
    /// Writes a cash expense straight to the table with a chosen creation stamp, bypassing
    /// <c>ExpenseService</c>.
    ///
    /// <para>Needed from Phase 16A-2 onwards: the service now refuses to create a cash expense while
    /// no register is open, so a row "outside any session" can no longer be produced through it.
    /// Such rows still exist in the wild — written before 16A-2, or restored from a backup — and the
    /// window arithmetic must keep excluding them, which is what these tests check.</para>
    /// </summary>
    private async Task<Expense> SeedCashExpenseOutsideAnySessionAsync(decimal amount, DateTime createdAtUtc)
    {
        var category = await CategoryAsync();
        var expense = new Expense
        {
            ExpenseNumber = $"EXP-SEED-{Guid.NewGuid():N}"[..16],
            ExpenseDateUtc = createdAtUtc,
            ExpenseCategoryId = category.Id,
            CategoryNameSnapshot = category.Name,
            Amount = amount,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = _ownerId,
            CreatedAtUtc = createdAtUtc,
        };
        _fixture.Context.Expenses.Add(expense);
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
        return expense;
    }

    /// <summary>Rewrites the immutable creation stamp so a test can place an expense outside the
    /// session window. Only a test may do this — no production path assigns CreatedAtUtc.</summary>
    private async Task SetCreatedAtAsync(int expenseId, DateTime createdAtUtc)
    {
        var expense = await _fixture.Context.Expenses.SingleAsync(e => e.Id == expenseId);
        expense.CreatedAtUtc = createdAtUtc;
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
    }

    private async Task RecordCashSaleAsync(decimal amount)
    {
        _fixture.Context.Sales.Add(new Sale
        {
            InvoiceNumber = $"INV-CE-{Guid.NewGuid():N}"[..14],
            SaleDateUtc = DateTime.UtcNow,
            Status = SaleStatus.Completed,
            SubTotal = amount,
            GrandTotal = amount,
            CashierUserId = _ownerId,
            Payments = [new Payment { Method = PaymentMethod.Cash, Amount = amount }],
        });
        await _fixture.Context.SaveChangesAsync();
        _fixture.Context.ChangeTracker.Clear();
    }

    public void Dispose() => _fixture.Dispose();
}
