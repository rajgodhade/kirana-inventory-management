using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels.Reports;

public sealed partial class ProductReportTabViewModel(IProductReportService productReportService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    // "Highest Profit" is omitted entirely for a user without ReportsViewProfit, rather than shown
    // and then failing when selected — the same "don't advertise what you can't reach" stance the
    // nav pane takes.
    public IReadOnlyList<string> ReportTypes { get; } = session.HasPermission(PermissionKeys.ReportsViewProfit)
        ? ["Most Selling", "Least Selling", "Highest Revenue", "Highest Profit", "Slow Moving", "Dead Stock", "Category-wise Sales", "Brand-wise Sales"]
        : ["Most Selling", "Least Selling", "Highest Revenue", "Slow Moving", "Dead Stock", "Category-wise Sales", "Brand-wise Sales"];

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfitReportSelected))]
    private string _selectedReportType = "Most Selling";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public bool IsProfitReportSelected => SelectedReportType == "Highest Profit";
    public bool CanViewProfit => session.HasPermission(PermissionKeys.ReportsViewProfit);

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
            var range = DateFilter.Resolve();
            Rows.Clear();

            switch (SelectedReportType)
            {
                case "Most Selling":
                    await LoadSalesRowsAsync(await productReportService.GetMostSellingAsync(range, CurrentUserId), "Qty Sold", "Revenue");
                    break;
                case "Least Selling":
                    await LoadSalesRowsAsync(await productReportService.GetLeastSellingAsync(range, CurrentUserId), "Qty Sold", "Revenue");
                    break;
                case "Highest Revenue":
                    await LoadSalesRowsAsync(await productReportService.GetHighestRevenueAsync(range, CurrentUserId), "Qty Sold", "Revenue");
                    break;
                case "Highest Profit":
                    await LoadSalesRowsAsync(await productReportService.GetHighestProfitProductsAsync(range, CurrentUserId), "Qty Sold", "Revenue", showProfit: true);
                    break;
                case "Slow Moving":
                    await LoadSalesRowsAsync(await productReportService.GetSlowMovingAsync(range, CurrentUserId), "Qty Sold", "Revenue");
                    break;
                case "Dead Stock":
                    LoadDeadStockRows(await productReportService.GetDeadStockAsync(range, CurrentUserId));
                    break;
                case "Category-wise Sales":
                    LoadCategoryRows(await productReportService.GetCategoryWiseSalesAsync(range, CurrentUserId));
                    break;
                case "Brand-wise Sales":
                    LoadBrandRows(await productReportService.GetBrandWiseSalesAsync(range, CurrentUserId));
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

    private Task LoadSalesRowsAsync(IReadOnlyList<ProductSalesRow> rows, string col1, string col2, bool showProfit = false)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.ProductName,
                Secondary = r.ProductCode,
                Column1Label = col1,
                Column1Value = r.QuantitySold.ToString("0.###"),
                Column2Label = col2,
                Column2Value = Fmt(r.Revenue),
                Column3Label = showProfit ? "Est. Profit" : (r.EstimatedProfit.HasValue ? "Est. Profit" : ""),
                Column3Value = r.EstimatedProfit is { } p ? Fmt(p) : "",
            });
        }

        _lastExport = new ReportExportData
        {
            Title = $"Products — {SelectedReportType}",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Product", "Code", col1, col2, "Est. Profit"],
            Rows = rows.Select(r => (IReadOnlyList<string>)
                [r.ProductName, r.ProductCode, r.QuantitySold.ToString("0.###"), Fmt(r.Revenue), r.EstimatedProfit is { } p ? Fmt(p) : "—"]).ToList(),
        };

        return Task.CompletedTask;
    }

    private void LoadDeadStockRows(IReadOnlyList<DeadStockRow> rows)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.ProductName,
                Secondary = r.ProductCode,
                Column1Label = "On Hand",
                Column1Value = r.QuantityOnHand.ToString("0.###"),
                Column2Label = "Stock Value",
                Column2Value = Fmt(r.StockValue),
                Column3Label = "Last Sold",
                Column3Value = r.LastSoldUtc?.ToLocalTime().ToString("dd-MMM-yyyy") ?? "Never",
            });
        }

        _lastExport = new ReportExportData
        {
            Title = "Products — Dead Stock",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Product", "Code", "On Hand", "Stock Value", "Last Sold"],
            Rows = rows.Select(r => (IReadOnlyList<string>)
                [r.ProductName, r.ProductCode, r.QuantityOnHand.ToString("0.###"), Fmt(r.StockValue), r.LastSoldUtc?.ToLocalTime().ToString("dd-MMM-yyyy") ?? "Never"]).ToList(),
        };
    }

    private void LoadCategoryRows(IReadOnlyList<CategorySalesRow> rows)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.CategoryName,
                Column1Label = "Qty Sold",
                Column1Value = r.QuantitySold.ToString("0.###"),
                Column2Label = "Revenue",
                Column2Value = Fmt(r.Revenue),
            });
        }

        _lastExport = new ReportExportData
        {
            Title = "Category-wise Sales",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Category", "Qty Sold", "Revenue"],
            Rows = rows.Select(r => (IReadOnlyList<string>)[r.CategoryName, r.QuantitySold.ToString("0.###"), Fmt(r.Revenue)]).ToList(),
        };
    }

    private void LoadBrandRows(IReadOnlyList<BrandSalesRow> rows)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.BrandName,
                Column1Label = "Qty Sold",
                Column1Value = r.QuantitySold.ToString("0.###"),
                Column2Label = "Revenue",
                Column2Value = Fmt(r.Revenue),
            });
        }

        _lastExport = new ReportExportData
        {
            Title = "Brand-wise Sales",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Brand", "Qty Sold", "Revenue"],
            Rows = rows.Select(r => (IReadOnlyList<string>)[r.BrandName, r.QuantitySold.ToString("0.###"), Fmt(r.Revenue)]).ToList(),
        };
    }

    public ReportExportData BuildExportData() => _lastExport ?? throw new InvalidOperationException("Load the report before exporting.");

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
