using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels.Reports;

/// <summary>Mirror of <see cref="CustomerReportTabViewModel"/> for suppliers: outstanding balances
/// (point-in-time, via <see cref="ISupplierService"/>) plus a Top Suppliers ranking for the
/// selected period (via <see cref="IDashboardService"/>).</summary>
public sealed partial class SupplierReportTabViewModel(
    ISupplierService supplierService, IDashboardService dashboardService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _totalOutstandingText = "₹0.00";

    public ObservableCollection<Supplier> OutstandingRows { get; } = [];
    public ObservableCollection<RankedPartyRow> TopSuppliers { get; } = [];

    private int? CurrentUserId => session.CurrentUser?.Id;
    private IReadOnlyList<Supplier> _lastOutstanding = [];

    public async Task EnsureLoadedAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var suppliers = await supplierService.SearchAsync(new SupplierSearchQuery { MaxResults = 1000 }, CurrentUserId);
            var outstanding = suppliers.Where(s => s.OutstandingBalance > 0).OrderByDescending(s => s.OutstandingBalance).ToList();
            _lastOutstanding = outstanding;
            OutstandingRows.Clear();
            foreach (var r in outstanding)
            {
                OutstandingRows.Add(r);
            }

            TotalOutstandingText = Fmt(outstanding.Sum(r => r.OutstandingBalance));

            var range = DateFilter.Resolve();
            var top = await dashboardService.GetTopSuppliersAsync(range, CurrentUserId, take: 10);
            TopSuppliers.Clear();
            foreach (var r in top)
            {
                TopSuppliers.Add(r);
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
            Title = "Supplier Outstanding Summary",
            Subtitle = "As of now",
            Columns = ["Supplier", "Code", "Phone", "Outstanding"],
            Rows = _lastOutstanding.Select(r => (IReadOnlyList<string>)
                [r.Name, r.SupplierCode, r.Phone ?? "", Fmt(r.OutstandingBalance)]).ToList(),
        };
    }

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
