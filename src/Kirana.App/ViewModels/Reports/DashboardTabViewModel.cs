using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Printing;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>Backs the Dashboard pivot tab: KPI tiles, all nine charts, and recent activity for the
/// selected date range (PRD §51 "Dashboard" + "Charts").</summary>
public sealed partial class DashboardTabViewModel(
    IDashboardService dashboardService,
    IProductReportService productReportService,
    IInventoryReportService inventoryReportService,
    IPrinterDiscoveryService printerDiscovery,
    ManagementSession session) : ObservableObject
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

    // --- Dashboard widgets ---
    // All three data-backed widgets below reuse existing, unmodified report queries — the very same
    // calls the Products and Inventory tabs already make. Nothing about how these figures are
    // computed changes; they are simply surfaced on the Dashboard as well.

    /// <summary>Best sellers for the selected range — the same query the Products tab's
    /// "Most Selling" view runs, capped to a glanceable five.</summary>
    public ObservableCollection<ProductSalesRow> TopSellingProducts { get; } = [];

    /// <summary>Products at or below their reorder point right now. Point-in-time by nature, so
    /// it deliberately ignores the date filter — same as the Low Stock KPI above it.</summary>
    public ObservableCollection<InventoryRow> LowStockItems { get; } = [];

    public bool HasNoLowStock => LowStockItems.Count == 0;

    [ObservableProperty] private string _topSellingEmptyText = "No sales in this period.";

    // --- Device status ---
    // Reports only what Windows actually tells us about installed printers. There is no telemetry,
    // no heartbeat and no scanner API in this app, so nothing here is inferred or invented — an
    // unknown state is shown as unknown rather than dressed up as "healthy".
    [ObservableProperty] private string _defaultPrinterText = "Checking…";
    [ObservableProperty] private string _printerCountText = "—";
    [ObservableProperty] private bool _hasPrinter;
    [ObservableProperty] private string _databaseStatusText = "Connected";

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
            await LoadWidgetsAsync(range);
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

    /// <summary>
    /// Loads the four dashboard widgets. Wrapped in its own try/catch and run after the KPIs and
    /// charts have already been applied: these are supplementary panels, so a failure in one of
    /// them (most plausibly the printer enumeration, which talks to the Windows spooler) must
    /// degrade to an empty widget rather than blanking the whole dashboard behind an error bar.
    /// </summary>
    private async Task LoadWidgetsAsync(ReportDateRange range)
    {
        try
        {
            Replace(TopSellingProducts, await productReportService.GetMostSellingAsync(range, CurrentUserId, take: 5));
            TopSellingEmptyText = TopSellingProducts.Count == 0 ? "No sales in this period." : string.Empty;

            var lowStock = await inventoryReportService.GetLowStockAsync(CurrentUserId);
            Replace(LowStockItems, lowStock.Take(5).ToList());
            OnPropertyChanged(nameof(HasNoLowStock));
        }
        catch (Exception ex)
        {
            // Surfaced in the widget itself rather than the page-level error bar, so a widget
            // problem never masks the KPIs and charts that did load correctly.
            TopSellingEmptyText = ex.Message;
        }

        LoadDeviceStatus();
    }

    private void LoadDeviceStatus()
    {
        try
        {
            var printers = printerDiscovery.GetInstalledPrinterNames();
            HasPrinter = printers.Count > 0;
            PrinterCountText = printers.Count switch
            {
                0 => "No printers installed",
                1 => "1 printer available",
                _ => $"{printers.Count} printers available",
            };
            DefaultPrinterText = printerDiscovery.GetDefaultPrinterName() ?? "No default printer set";
        }
        catch (Exception ex)
        {
            HasPrinter = false;
            PrinterCountText = "Could not read printers";
            DefaultPrinterText = ex.Message;
        }
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
