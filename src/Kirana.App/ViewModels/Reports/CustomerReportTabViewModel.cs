using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>
/// Customer reports (PRD §51): the outstanding-Udhaar summary (point-in-time, ignores the date
/// filter — same convention as the Dashboard's own outstanding tile) plus a Top Customers ranking
/// for the selected period, built by reusing <see cref="ICustomerCreditService"/> from Phase 8 and
/// <see cref="IDashboardService"/> from earlier in this phase rather than duplicating either query.
/// </summary>
public sealed partial class CustomerReportTabViewModel(
    ICustomerCreditService customerCreditService, IDashboardService dashboardService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _totalOutstandingText = "₹0.00";

    public ObservableCollection<CustomerOutstandingSummary> OutstandingRows { get; } = [];
    public ObservableCollection<RankedPartyRow> TopCustomers { get; } = [];

    private int? CurrentUserId => session.CurrentUser?.Id;
    private IReadOnlyList<CustomerOutstandingSummary> _lastOutstanding = [];

    public async Task EnsureLoadedAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var outstanding = await customerCreditService.GetOutstandingSummaryAsync(CurrentUserId);
            _lastOutstanding = outstanding;
            OutstandingRows.Clear();
            foreach (var r in outstanding)
            {
                OutstandingRows.Add(r);
            }

            TotalOutstandingText = Fmt(outstanding.Sum(r => r.OutstandingAmount));

            var range = DateFilter.Resolve();
            var top = await dashboardService.GetTopCustomersAsync(range, CurrentUserId, take: 10);
            TopCustomers.Clear();
            foreach (var r in top)
            {
                TopCustomers.Add(r);
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

    public ReportExportData BuildExportData()
    {
        return new ReportExportData
        {
            Title = "Customer Outstanding Summary",
            Subtitle = "As of now",
            Columns = ["Customer", "Code", "Phone", "Outstanding", "Open Credits", "Oldest Unpaid"],
            Rows = _lastOutstanding.Select(r => (IReadOnlyList<string>)
                [r.Name, r.CustomerCode, r.Phone ?? "", Fmt(r.OutstandingAmount), r.OpenCreditCount.ToString(),
                 r.OldestUnpaidDateUtc?.ToLocalTime().ToString("dd-MMM-yyyy") ?? "—"]).ToList(),
        };
    }

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
