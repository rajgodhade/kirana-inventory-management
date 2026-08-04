using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>Backs the Dashboard pivot tab: KPI tiles, all nine charts, and recent activity for the
/// selected date range (PRD §51 "Dashboard" + "Charts").</summary>
public sealed partial class DashboardTabViewModel(IDashboardService dashboardService, ManagementSession session) : ObservableObject
{
    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canViewProfit;

    // --- KPI tiles ---
    [ObservableProperty] private string _totalSalesText = "₹0.00";
    [ObservableProperty] private string _totalPurchasesText = "₹0.00";
    [ObservableProperty] private string _totalExpensesText = "₹0.00";
    [ObservableProperty] private string _grossProfitText = "—";
    [ObservableProperty] private string _netProfitText = "—";
    [ObservableProperty] private string _billCountText = "0";
    [ObservableProperty] private string _itemsSoldText = "0";
    [ObservableProperty] private string _inventoryValueText = "₹0.00";
    [ObservableProperty] private string _lowStockCountText = "0";
    [ObservableProperty] private string _outOfStockCountText = "0";
    [ObservableProperty] private string _customerOutstandingText = "₹0.00";
    [ObservableProperty] private string _supplierOutstandingText = "₹0.00";

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<ChartBarItemViewModel> DailySalesTrend { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> WeeklySales { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> MonthlySales { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> SalesVsExpensesSales { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> SalesVsExpensesExpenses { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> GrossProfitTrend { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> ProductCategorySales { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> PaymentMethodDistribution { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> TopCustomersChart { get; } = [];
    public ObservableCollection<ChartBarItemViewModel> TopSuppliersChart { get; } = [];

    public ObservableCollection<RecentSaleRow> RecentSales { get; } = [];
    public ObservableCollection<RecentPurchaseRow> RecentPurchases { get; } = [];
    public ObservableCollection<RecentReturnRow> RecentReturns { get; } = [];
    public ObservableCollection<RecentExpenseRow> RecentExpenses { get; } = [];

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var range = DateFilter.Resolve();

            var summary = await dashboardService.GetSummaryAsync(range, CurrentUserId);
            ApplySummary(summary);

            var charts = await dashboardService.GetChartsAsync(range, CurrentUserId);
            ApplyCharts(charts);

            await LoadRecentActivityAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySummary(DashboardSummary s)
    {
        CanViewProfit = s.CanViewProfit;
        TotalSalesText = FormatCurrency(s.TotalSales);
        TotalPurchasesText = FormatCurrency(s.TotalPurchases);
        TotalExpensesText = FormatCurrency(s.TotalExpenses);
        GrossProfitText = s.GrossProfit is { } gp ? FormatCurrency(gp) : "—";
        NetProfitText = s.NetProfit is { } np ? FormatCurrency(np) : "—";
        BillCountText = s.BillCount.ToString();
        ItemsSoldText = s.ItemsSold.ToString("0.###");
        InventoryValueText = FormatCurrency(s.InventoryValue);
        LowStockCountText = s.LowStockCount.ToString();
        OutOfStockCountText = s.OutOfStockCount.ToString();
        CustomerOutstandingText = FormatCurrency(s.CustomerOutstanding);
        SupplierOutstandingText = FormatCurrency(s.SupplierOutstanding);
    }

    private void ApplyCharts(DashboardCharts charts)
    {
        const double barHeight = 140;
        const double rankWidth = 160;

        Replace(DailySalesTrend, ChartViewModelFactory.BuildBars(charts.DailySalesTrend, barHeight));
        Replace(WeeklySales, ChartViewModelFactory.BuildBars(charts.WeeklySales, barHeight));
        Replace(MonthlySales, ChartViewModelFactory.BuildBars(charts.MonthlySales, barHeight));
        Replace(GrossProfitTrend, ChartViewModelFactory.BuildBars(charts.GrossProfitTrend, barHeight));
        Replace(ProductCategorySales, ChartViewModelFactory.BuildBars(charts.ProductCategorySales, barHeight));
        Replace(PaymentMethodDistribution, ChartViewModelFactory.BuildBars(charts.PaymentMethodDistribution, barHeight));

        var (sales, expenses) = ChartViewModelFactory.BuildPairedBars(charts.SalesVsExpensesSales, charts.SalesVsExpensesExpenses, barHeight);
        Replace(SalesVsExpensesSales, sales);
        Replace(SalesVsExpensesExpenses, expenses);

        Replace(TopCustomersChart, ChartViewModelFactory.BuildBars(charts.TopCustomers, rankWidth));
        Replace(TopSuppliersChart, ChartViewModelFactory.BuildBars(charts.TopSuppliers, rankWidth));
    }

    private async Task LoadRecentActivityAsync()
    {
        Replace(RecentSales, await dashboardService.GetRecentSalesAsync(CurrentUserId));
        Replace(RecentPurchases, await dashboardService.GetRecentPurchasesAsync(CurrentUserId));
        Replace(RecentReturns, await dashboardService.GetRecentReturnsAsync(CurrentUserId));
        Replace(RecentExpenses, await dashboardService.GetRecentExpensesAsync(CurrentUserId));
    }

    private static void Replace<T>(ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static string FormatCurrency(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
}
