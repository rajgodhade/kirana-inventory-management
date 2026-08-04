using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>Profit report (PRD §51): revenue, COGS, gross/net profit for the selected date range.
/// Owner-only — <see cref="ReportsHubPage"/> hides this tab entirely without
/// <c>ReportsViewProfit</c>, and <see cref="ProfitReportService"/> re-checks the same permission
/// server-side, so this view model never has to defend against being reached without it.</summary>
public sealed partial class ProfitReportTabViewModel(IProfitReportService profitReportService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _revenueText = "₹0.00";
    [ObservableProperty] private string _grossSalesText = "₹0.00";
    [ObservableProperty] private string _returnsText = "₹0.00";
    [ObservableProperty] private string _cogsText = "₹0.00";
    [ObservableProperty] private string _grossProfitText = "₹0.00";
    [ObservableProperty] private string _expensesText = "₹0.00";
    [ObservableProperty] private string _netProfitText = "₹0.00";

    private int? CurrentUserId => session.CurrentUser?.Id;
    private ProfitSummary? _lastSummary;

    public async Task EnsureLoadedAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var range = DateFilter.Resolve();
            var s = await profitReportService.GetSummaryAsync(range, CurrentUserId);
            _lastSummary = s;

            RevenueText = Fmt(s.Revenue);
            GrossSalesText = Fmt(s.GrossSales);
            ReturnsText = Fmt(s.Returns);
            CogsText = Fmt(s.CostOfGoodsSold);
            GrossProfitText = Fmt(s.GrossProfit);
            ExpensesText = Fmt(s.Expenses);
            NetProfitText = Fmt(s.NetProfit);
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

    public ReportExportData BuildExportData()
    {
        var s = _lastSummary ?? throw new InvalidOperationException("Load the report before exporting.");
        return new ReportExportData
        {
            Title = "Profit Report",
            Subtitle = $"{DateFilter.Resolve().Label} — COGS uses current product purchase price (estimated)",
            Columns = ["Metric", "Amount"],
            Rows =
            [
                ["Gross Sales", Fmt(s.GrossSales)],
                ["Returns", Fmt(s.Returns)],
                ["Revenue (Net Sales)", Fmt(s.Revenue)],
                ["Cost of Goods Sold (Est.)", Fmt(s.CostOfGoodsSold)],
                ["Gross Profit (Est.)", Fmt(s.GrossProfit)],
                ["Expenses", Fmt(s.Expenses)],
                ["Net Profit (Est.)", Fmt(s.NetProfit)],
            ],
        };
    }

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
