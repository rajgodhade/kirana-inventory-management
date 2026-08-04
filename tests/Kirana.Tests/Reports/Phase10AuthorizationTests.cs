using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>
/// Proves every Phase 10 surface is gated at the Application layer, not just hidden in the UI, and
/// that no new permission keys were introduced: dashboard/report reads reuse
/// <see cref="PermissionKeys.ReportsView"/>, profit-bearing figures additionally require
/// <see cref="PermissionKeys.ReportsViewProfit"/>, inventory valuation reuses
/// <see cref="PermissionKeys.PricingViewPurchasePrice"/>, and expense reports reuse
/// <see cref="PermissionKeys.ExpensesManage"/> — exactly the permissions PRD Phase 10 said to reuse.
/// </summary>
public class Phase10AuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly int _ownerId;
    private int _managerId;
    private int _cashierId;

    public Phase10AuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        _cashierId = (await _fixture.SeedCashierAsync()).Id;

        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-p10", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();
        _managerId = manager.Id;
    }

    private static ReportDateRange TodayRange() => ReportDateRange.Resolve(ReportDatePreset.Today);

    [Fact]
    public async Task NoUser_CannotReachAnyPhase10Surface()
    {
        var enforcer = new PermissionEnforcer(_fixture.Context);
        var inventoryService = new InventoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), enforcer);
        var profit = new ProfitReportService(_fixture.Context, enforcer);
        var dashboard = new DashboardService(_fixture.Context, inventoryService, profit, enforcer);
        var sales = new SalesReportService(_fixture.Context, enforcer);
        var products = new ProductReportService(_fixture.Context, enforcer);
        var inventoryReports = new InventoryReportService(_fixture.Context, inventoryService, enforcer);
        var expenseReports = new ExpenseReportService(_fixture.Context, enforcer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dashboard.GetSummaryAsync(TodayRange(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sales.GetSummaryAsync(TodayRange(), null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sales.GetGstReportAsync(TodayRange(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => products.GetMostSellingAsync(TodayRange(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventoryReports.GetCurrentInventoryAsync(null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => expenseReports.GetDailyAsync(TodayRange(), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => profit.GetSummaryAsync(TodayRange(), null));
    }

    [Fact]
    public async Task Cashier_CannotReachReports()
    {
        var enforcer = new PermissionEnforcer(_fixture.Context);
        var inventoryService = new InventoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), enforcer);
        var profit = new ProfitReportService(_fixture.Context, enforcer);
        var dashboard = new DashboardService(_fixture.Context, inventoryService, profit, enforcer);
        var sales = new SalesReportService(_fixture.Context, enforcer);
        var products = new ProductReportService(_fixture.Context, enforcer);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dashboard.GetSummaryAsync(TodayRange(), _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sales.GetSummaryAsync(TodayRange(), null, _cashierId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => products.GetMostSellingAsync(TodayRange(), _cashierId));
    }

    [Fact]
    public async Task Manager_CanReachGeneralReports_ButNotProfitOrValuation()
    {
        var enforcer = new PermissionEnforcer(_fixture.Context);
        var inventoryService = new InventoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), enforcer);
        var profit = new ProfitReportService(_fixture.Context, enforcer);
        var dashboard = new DashboardService(_fixture.Context, inventoryService, profit, enforcer);
        var sales = new SalesReportService(_fixture.Context, enforcer);
        var inventoryReports = new InventoryReportService(_fixture.Context, inventoryService, enforcer);
        var expenseReports = new ExpenseReportService(_fixture.Context, enforcer);

        // General reports: Manager holds ReportsView.
        var summary = await dashboard.GetSummaryAsync(TodayRange(), _managerId);
        Assert.NotNull(summary);
        Assert.False(summary.CanViewProfit);

        await sales.GetSummaryAsync(TodayRange(), null, _managerId);
        await expenseReports.GetDailyAsync(TodayRange(), _managerId); // Manager holds ExpensesManage too

        // Profit and valuation: Manager was deliberately NOT given these (PRD §6, §9).
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => profit.GetSummaryAsync(TodayRange(), _managerId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventoryReports.GetValuationAsync(_managerId));
    }

    [Fact]
    public async Task Owner_CanReachEveryPhase10Surface()
    {
        var enforcer = new PermissionEnforcer(_fixture.Context);
        var inventoryService = new InventoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), enforcer);
        var profit = new ProfitReportService(_fixture.Context, enforcer);
        var dashboard = new DashboardService(_fixture.Context, inventoryService, profit, enforcer);
        var sales = new SalesReportService(_fixture.Context, enforcer);
        var products = new ProductReportService(_fixture.Context, enforcer);
        var inventoryReports = new InventoryReportService(_fixture.Context, inventoryService, enforcer);
        var expenseReports = new ExpenseReportService(_fixture.Context, enforcer);

        Assert.NotNull(await dashboard.GetSummaryAsync(TodayRange(), _ownerId));
        Assert.NotNull(await sales.GetSummaryAsync(TodayRange(), null, _ownerId));
        Assert.NotNull(await profit.GetSummaryAsync(TodayRange(), _ownerId));
        Assert.NotNull(await inventoryReports.GetValuationAsync(_ownerId));
        Assert.NotNull(await products.GetMostSellingAsync(TodayRange(), _ownerId));
        Assert.NotNull(await expenseReports.GetDailyAsync(TodayRange(), _ownerId));
    }

    public void Dispose() => _fixture.Dispose();
}
