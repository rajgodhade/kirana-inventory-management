using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.App.ViewModels.Reports;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Inventories;
using Kirana.Application.Printing;
using Kirana.Application.Promotions;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

/// <summary>
/// Read-only presentation model for the management Home screen. All commercial figures come from
/// the same report and inventory services used by the Reports and Products screens; this class only
/// turns those results into glanceable dashboard text and collections.
/// </summary>
public sealed partial class ManagementHomeViewModel(
    IKiranaDbContext db,
    IInventoryService inventoryService,
    IDashboardService dashboardService,
    ISalesReportService salesReportService,
    IProductReportService productReportService,
    IBackupService backupService,
    IPrinterDiscoveryService printerDiscovery,
    IPromotionService promotionService,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty] private string _greeting = "Welcome";
    [ObservableProperty] private string _todayDateText = string.Empty;
    [ObservableProperty] private string _userText = string.Empty;
    [ObservableProperty] private string _todaySalesText = "₹0.00";
    [ObservableProperty] private string _todaySalesCountText = "No sales yet today";
    [ObservableProperty] private string _grossProfitText = "—";
    [ObservableProperty] private string _billCountText = "0";
    [ObservableProperty] private string _itemsSoldText = "0";
    [ObservableProperty] private string _lowStockCountText = "0";
    [ObservableProperty] private string _outOfStockCountText = "0";
    [ObservableProperty] private string _outstandingUdhaarText = "₹0.00";
    [ObservableProperty] private string _supplierDueText = "₹0.00";
    [ObservableProperty] private string _grossSalesText = "₹0.00";
    [ObservableProperty] private string _discountsText = "₹0.00";
    [ObservableProperty] private string _returnsText = "₹0.00";
    [ObservableProperty] private string _netSalesText = "₹0.00";
    [ObservableProperty] private string _averageBillText = "₹0.00";
    [ObservableProperty] private string _lastBackupText = "No backup recorded";
    [ObservableProperty] private string _printerStatusText = "Checking…";
    [ObservableProperty] private string _defaultPrinterText = "—";
    [ObservableProperty] private string _runningPromotionsText = "0";
    [ObservableProperty] private string _promotionsEndingTodayText = "0";
    [ObservableProperty] private string _topPromotionText = "No promotion sales today";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    private decimal _customerOutstanding;
    private decimal _supplierOutstanding;
    private DateTime? _lastBackupUtc;

    public bool CanSeeCustomerFinancials => session.HasPermission(PermissionKeys.CustomersManage);
    public bool CanSeePurchaseFinancials => session.HasPermission(PermissionKeys.PurchasesManage);
    public bool CanSeeInventory => session.HasPermission(PermissionKeys.InventoryManage);
    public bool CanSeeReports => session.HasPermission(PermissionKeys.ReportsView);
    public bool CanSeeProfit => session.HasPermission(PermissionKeys.ReportsViewProfit);
    public bool CanManageBackups => session.HasPermission(PermissionKeys.BackupRestore);
    public bool CanManageExpenses => session.HasPermission(PermissionKeys.ExpensesManage);
    public bool CanSeePromotions => session.HasPermission(PermissionKeys.PromotionsView);

    public bool HasAlerts => BusinessAlerts.Count > 0;
    public bool HasInsights => Insights.Count > 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public bool HasTopProducts => TopSellingProducts.Count > 0;

    public ObservableCollection<RecentActivityRowViewModel> RecentActivity { get; } = [];
    public ObservableCollection<DashboardAlertViewModel> BusinessAlerts { get; } = [];
    public ObservableCollection<DashboardInsightViewModel> Insights { get; } = [];
    public ObservableCollection<ProductSalesRow> TopSellingProducts { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> SalesTrend { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> PaymentMix { get; } = [];

    public async Task InitializeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            Greeting = BuildGreeting(session.CurrentUser?.FullName);
            TodayDateText = DateTime.Now.ToString("dddd, dd MMMM yyyy");
            UserText = string.IsNullOrWhiteSpace(session.CurrentUser?.FullName)
                ? "Management session"
                : $"Signed in as {session.CurrentUser.FullName}";

            // Keep the original permission-scoped Home values available even for roles that do
            // not have Reports access.
            await LoadCoreHomeValuesAsync();
            await LoadRecentActivityAsync();
            await LoadBackupAndDeviceStatusAsync();
            if (CanSeePromotions) await LoadPromotionWidgetAsync();

            if (CanSeeReports)
            {
                await LoadCommercialDashboardAsync();
            }

            BuildAlerts();
            OnPropertyChanged(nameof(HasAlerts));
            OnPropertyChanged(nameof(HasInsights));
            OnPropertyChanged(nameof(HasRecentActivity));
            OnPropertyChanged(nameof(HasTopProducts));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Some dashboard information could not be loaded. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPromotionWidgetAsync()
    {
        var running = await promotionService.SearchAsync(new PromotionSearchQuery { RunningOnly = true }, session.CurrentUser?.Id);
        RunningPromotionsText = running.Count.ToString("N0");
        PromotionsEndingTodayText = running.Count(x => x.Schedule?.EndAtUtc.ToLocalTime().Date == DateTime.Today).ToString("N0");
        if (CanSeeReports)
        {
            var today = ReportDateRange.Resolve(ReportDatePreset.Today);
            var performance = await promotionService.GetPerformanceAsync(today.StartUtc, today.EndUtc, session.CurrentUser?.Id);
            TopPromotionText = performance.OrderByDescending(x => x.Revenue).FirstOrDefault()?.PromotionName ?? "No promotion sales today";
        }
    }

    private async Task LoadCoreHomeValuesAsync()
    {
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);
        var todaySales = await db.Sales.AsNoTracking()
            .Where(s => s.SaleDateUtc >= range.StartUtc && s.SaleDateUtc < range.EndUtc)
            .Select(s => s.GrandTotal)
            .ToListAsync();

        TodaySalesText = FormatCurrency(todaySales.Sum());
        TodaySalesCountText = todaySales.Count switch
        {
            0 => "No sales yet today",
            1 => "1 sale today",
            _ => $"{todaySales.Count} sales today",
        };
        BillCountText = todaySales.Count.ToString("N0");

        if (CanSeeInventory)
        {
            LowStockCountText = (await inventoryService.GetLowStockProductsAsync()).Count.ToString("N0");
        }

        if (CanSeeCustomerFinancials)
        {
            _customerOutstanding = await db.CustomerCredits.AsNoTracking()
                .Where(c => c.RemainingAmount > 0)
                .SumAsync(c => (decimal?)c.RemainingAmount) ?? 0m;
            OutstandingUdhaarText = FormatCurrency(_customerOutstanding);
        }

        if (CanSeePurchaseFinancials)
        {
            _supplierOutstanding = await db.Suppliers.AsNoTracking()
                .Where(s => s.OutstandingBalance > 0)
                .SumAsync(s => (decimal?)s.OutstandingBalance) ?? 0m;
            SupplierDueText = FormatCurrency(_supplierOutstanding);
        }
    }

    private async Task LoadCommercialDashboardAsync()
    {
        var userId = session.CurrentUser?.Id;
        var today = ReportDateRange.Resolve(ReportDatePreset.Today);
        var yesterday = ReportDateRange.Resolve(ReportDatePreset.Yesterday);

        var summary = await dashboardService.GetSummaryAsync(today, userId);
        TodaySalesText = FormatCurrency(summary.TotalSales);
        BillCountText = summary.BillCount.ToString("N0");
        TodaySalesCountText = summary.BillCount switch
        {
            0 => "No sales yet today",
            1 => "1 sale today",
            _ => $"{summary.BillCount:N0} sales today",
        };
        ItemsSoldText = summary.ItemsSold.ToString("N0");
        LowStockCountText = summary.LowStockCount.ToString("N0");
        OutOfStockCountText = summary.OutOfStockCount.ToString("N0");
        OutstandingUdhaarText = FormatCurrency(summary.CustomerOutstanding);
        SupplierDueText = FormatCurrency(summary.SupplierOutstanding);
        _customerOutstanding = summary.CustomerOutstanding;
        _supplierOutstanding = summary.SupplierOutstanding;
        GrossProfitText = summary.GrossProfit is { } profit ? FormatCurrency(profit) : "—";

        var salesSummary = await salesReportService.GetSummaryAsync(today, filter: null, userId);
        GrossSalesText = FormatCurrency(salesSummary.GrossSales);
        DiscountsText = FormatCurrency(salesSummary.TotalDiscounts);
        ReturnsText = FormatCurrency(salesSummary.Returns);
        NetSalesText = FormatCurrency(salesSummary.NetSales);
        AverageBillText = FormatCurrency(salesSummary.AverageBillValue);

        var yesterdaySummary = await dashboardService.GetSummaryAsync(yesterday, userId);
        BuildInsights(summary, yesterdaySummary, salesSummary);

        // Let the KPI/summary state render first; chart and ranking queries are supplementary and
        // deliberately deferred so they never hold up the operational overview.
        await Task.Yield();
        var charts = await dashboardService.GetChartsAsync(today, userId);
        Replace(SalesTrend, ChartViewModelFactory.BuildBars(charts.DailySalesTrend, 112));
        Replace(PaymentMix, ChartViewModelFactory.BuildBars(charts.PaymentMethodDistribution, 112));
        Replace(TopSellingProducts, await productReportService.GetMostSellingAsync(today, userId, take: 5));
    }

    private void BuildInsights(DashboardSummary today, DashboardSummary yesterday, SalesReportSummary salesSummary)
    {
        Insights.Clear();

        if (yesterday.TotalSales > 0)
        {
            var change = (today.TotalSales - yesterday.TotalSales) / yesterday.TotalSales * 100m;
            Insights.Add(new DashboardInsightViewModel
            {
                Glyph = change >= 0 ? "" : "",
                Title = change >= 0 ? "Sales are ahead" : "Sales are behind",
                Detail = $"{Math.Abs(change):0.#}% {(change >= 0 ? "higher" : "lower")} than yesterday so far.",
            });
        }

        if (salesSummary.BillCount > 0)
        {
            Insights.Add(new DashboardInsightViewModel
            {
                Glyph = "",
                Title = "Average bill value",
                Detail = $"Customers are spending {AverageBillText} per bill today.",
            });
        }

        if (today.LowStockCount > 0)
        {
            Insights.Add(new DashboardInsightViewModel
            {
                Glyph = "",
                Title = "Reorder opportunity",
                Detail = $"{today.LowStockCount:N0} product(s) are at or below minimum stock.",
            });
        }
    }

    private async Task LoadRecentActivityAsync()
    {
        RecentActivity.Clear();
        var sales = await db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Payments)
            .OrderByDescending(s => s.SaleDateUtc)
            .Take(6)
            .ToListAsync();

        foreach (var sale in sales)
        {
            var methods = sale.Payments.Select(p => p.Method.ToString()).Distinct().ToList();
            RecentActivity.Add(new RecentActivityRowViewModel
            {
                Title = sale.InvoiceNumber,
                Customer = sale.Customer?.Name ?? "Walk-in customer",
                Subtitle = sale.SaleDateUtc.ToLocalTime().ToString("dd MMM, hh:mm tt"),
                PaymentMethod = methods.Count == 0 ? "Payment not recorded" : string.Join(" + ", methods),
                Status = sale.Status.ToString(),
                Amount = FormatCurrency(sale.GrandTotal),
            });
        }
    }

    private async Task LoadBackupAndDeviceStatusAsync()
    {
        if (CanManageBackups)
        {
            var latest = (await backupService.GetHistoryAsync())
                .OrderByDescending(x => x.Record.CreatedAtUtc)
                .FirstOrDefault();
            _lastBackupUtc = latest?.Record.CreatedAtUtc;
            LastBackupText = latest is null
                ? "No backup recorded"
                : $"Last backup {latest.Record.CreatedAtUtc.ToLocalTime():dd MMM, hh:mm tt}";
        }

        try
        {
            var printers = printerDiscovery.GetInstalledPrinterNames();
            PrinterStatusText = printers.Count switch
            {
                0 => "No printer detected",
                1 => "1 printer available",
                _ => $"{printers.Count} printers available",
            };
            DefaultPrinterText = printerDiscovery.GetDefaultPrinterName() ?? "No default printer";
        }
        catch
        {
            PrinterStatusText = "Printer status unavailable";
            DefaultPrinterText = "Check Windows printer settings";
        }
    }

    private void BuildAlerts()
    {
        BusinessAlerts.Clear();
        if (CanSeeInventory && int.TryParse(LowStockCountText.Replace(",", string.Empty), out var lowStock) && lowStock > 0)
        {
            BusinessAlerts.Add(new DashboardAlertViewModel
            {
                Glyph = "", Title = $"{lowStock:N0} low-stock product(s)",
                Detail = "Review reorder levels before products run out.", Action = "Review products", Destination = "Products",
            });
        }

        if (CanSeeInventory && int.TryParse(OutOfStockCountText.Replace(",", string.Empty), out var outOfStock) && outOfStock > 0)
        {
            BusinessAlerts.Add(new DashboardAlertViewModel
            {
                Glyph = "", Title = $"{outOfStock:N0} product(s) out of stock",
                Detail = "These products cannot be billed until stock is replenished.", Action = "View inventory", Destination = "Products",
            });
        }

        if (CanSeeCustomerFinancials && _customerOutstanding > 0)
        {
            BusinessAlerts.Add(new DashboardAlertViewModel
            {
                Glyph = "", Title = $"{FormatCurrency(_customerOutstanding)} customer credit outstanding",
                Detail = "Review customer balances and follow up on due udhaar.", Action = "View customers", Destination = "Customers",
            });
        }

        if (CanSeePurchaseFinancials && _supplierOutstanding > 0)
        {
            BusinessAlerts.Add(new DashboardAlertViewModel
            {
                Glyph = "", Title = $"{FormatCurrency(_supplierOutstanding)} payable to suppliers",
                Detail = "Review outstanding purchase balances before their due dates.", Action = "View purchases", Destination = "Purchases",
            });
        }

        if (CanManageBackups && (_lastBackupUtc is null || _lastBackupUtc < DateTime.UtcNow.AddDays(-1)))
        {
            BusinessAlerts.Add(new DashboardAlertViewModel
            {
                Glyph = "", Title = _lastBackupUtc is null ? "No backup recorded" : "Backup is overdue",
                Detail = "Create a current verified backup to protect store data.", Action = "Open backup", Destination = "Backup",
            });
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string FormatCurrency(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));

    private static string BuildGreeting(string? name)
    {
        var hour = DateTime.Now.Hour;
        var partOfDay = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        return string.IsNullOrWhiteSpace(name) ? partOfDay : $"{partOfDay}, {name.Split(' ')[0]}";
    }
}

public sealed class RecentActivityRowViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
}

public sealed class DashboardAlertViewModel
{
    public string Glyph { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
}

public sealed class DashboardInsightViewModel
{
    public string Glyph { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
