using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Expenses;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>Expense reports (PRD §51): Daily/Monthly/Category-wise for the selected date range plus
/// a 6-month trend that ignores the filter, mirroring the Dashboard's own trend charts.</summary>
public sealed partial class ExpenseReportTabViewModel(
    IExpenseReportService expenseReportService, IExpenseService expenseService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    public IReadOnlyList<string> ReportTypes { get; } = ["Daily", "Monthly", "Category-wise", "Trend (6 mo)"];

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty] private string _selectedReportType = "Daily";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _totalText = "₹0.00";

    public ObservableCollection<GenericReportRowViewModel> Rows { get; } = [];

    private int? CurrentUserId => session.CurrentUser?.Id;
    private ReportExportData? _lastExport;

    public async Task EnsureLoadedAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            Rows.Clear();
            var range = DateFilter.Resolve();

            switch (SelectedReportType)
            {
                case "Daily":
                    LoadDaily(await expenseReportService.GetDailyAsync(range, CurrentUserId));
                    break;
                case "Monthly":
                    LoadMonthly(await expenseReportService.GetMonthlyAsync(range, CurrentUserId), "Monthly Expenses");
                    break;
                case "Category-wise":
                    await LoadCategoryAsync(range);
                    break;
                case "Trend (6 mo)":
                    LoadMonthly(await expenseReportService.GetTrendAsync(6, CurrentUserId), "Expense Trend (6 mo)");
                    break;
            }
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

    private void LoadDaily(IReadOnlyList<ExpenseDailyRow> rows)
    {
        TotalText = Fmt(rows.Sum(r => r.Amount));
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.Date.ToString("dd-MMM-yyyy"),
                Column1Label = "Amount",
                Column1Value = Fmt(r.Amount),
                Column2Label = "Count",
                Column2Value = r.Count.ToString(),
            });
        }

        _lastExport = new ReportExportData
        {
            Title = "Daily Expenses",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Date", "Amount", "Count"],
            Rows = rows.Select(r => (IReadOnlyList<string>)[r.Date.ToString("dd-MMM-yyyy"), Fmt(r.Amount), r.Count.ToString()]).ToList(),
        };
    }

    private void LoadMonthly(IReadOnlyList<ExpenseMonthlyRow> rows, string title)
    {
        TotalText = Fmt(rows.Sum(r => r.Amount));
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.Label,
                Column1Label = "Amount",
                Column1Value = Fmt(r.Amount),
                Column2Label = "Count",
                Column2Value = r.Count.ToString(),
            });
        }

        _lastExport = new ReportExportData
        {
            Title = title,
            Subtitle = title.StartsWith("Expense Trend") ? "Trailing 6 months" : DateFilter.Resolve().Label,
            Columns = ["Month", "Amount", "Count"],
            Rows = rows.Select(r => (IReadOnlyList<string>)[r.Label, Fmt(r.Amount), r.Count.ToString()]).ToList(),
        };
    }

    private async Task LoadCategoryAsync(ReportDateRange range)
    {
        var totals = await expenseService.GetTotalsAsync(
            new ExpenseSearchQuery { FromDateUtc = range.StartUtc, ToDateUtc = range.EndUtc.AddTicks(-1), MaxResults = int.MaxValue },
            CurrentUserId);

        TotalText = Fmt(totals.TotalAmount);
        foreach (var r in totals.ByCategory)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.CategoryName,
                Column1Label = "Amount",
                Column1Value = Fmt(r.TotalAmount),
                Column2Label = "Count",
                Column2Value = r.Count.ToString(),
            });
        }

        _lastExport = new ReportExportData
        {
            Title = "Category-wise Expenses",
            Subtitle = range.Label,
            Columns = ["Category", "Amount", "Count"],
            Rows = totals.ByCategory.Select(r => (IReadOnlyList<string>)[r.CategoryName, Fmt(r.TotalAmount), r.Count.ToString()]).ToList(),
        };
    }

    public ReportExportData BuildExportData() => _lastExport ?? throw new InvalidOperationException("Load the report before exporting.");

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
