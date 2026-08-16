using Kirana.Application.Authentication;
using Kirana.Application.Expenses;
using Kirana.Application.Reports;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Reports;

public class ExpenseReportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ExpenseReportService _sut;
    private readonly ExpenseService _expenseService;
    private readonly ExpenseCategoryService _categoryService;
    private readonly int _ownerId;

    public ExpenseReportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _sut = new ExpenseReportService(_fixture.Context, enforcer);
        _expenseService = new ExpenseService(_fixture.Context, seq, audit, enforcer);
        _categoryService = new ExpenseCategoryService(_fixture.Context, audit, enforcer);
    }

    private async Task<int> CategoryAsync(string name = "Rent") =>
        (await _categoryService.CreateAsync(new CreateExpenseCategoryRequest { Name = name, PerformedByUserId = _ownerId })).Id;

    private Task RecordAsync(int categoryId, decimal amount, DateTime? dateUtc = null) =>
        _expenseService.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = categoryId, Amount = amount, ExpenseDateUtc = dateUtc, PerformedByUserId = _ownerId,
        });

    [Fact]
    public async Task Daily_GroupsByLocalCalendarDay()
    {
        var category = await CategoryAsync();
        await RecordAsync(category, 100);
        await RecordAsync(category, 50);

        var rows = await _sut.GetDailyAsync(ReportDateRange.Resolve(ReportDatePreset.Today), _ownerId);

        var row = Assert.Single(rows);
        Assert.Equal(150m, row.Amount);
        Assert.Equal(2, row.Count);
    }

    [Fact]
    public async Task Monthly_GroupsAcrossTheWholeMonth()
    {
        var category = await CategoryAsync();
        await RecordAsync(category, 200);

        var rows = await _sut.GetMonthlyAsync(ReportDateRange.Resolve(ReportDatePreset.ThisMonth), _ownerId);

        var row = Assert.Single(rows);
        Assert.Equal(200m, row.Amount);
        Assert.Equal(DateTime.Now.Month, row.Month);
    }

    [Fact]
    public async Task Trend_FillsMonthsWithNoExpensesAsZero()
    {
        var category = await CategoryAsync();
        await RecordAsync(category, 500); // this month only

        var rows = await _sut.GetTrendAsync(months: 3, _ownerId);

        Assert.Equal(3, rows.Count);
        Assert.Equal(500m, rows[^1].Amount); // current month is the last bucket
        Assert.Equal(0m, rows[0].Amount);    // two months ago had nothing
    }

    [Fact]
    public async Task RequiresExpensesManagePermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetDailyAsync(range, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetMonthlyAsync(range, cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetTrendAsync(3, cashier.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
