using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels.Reports;

public sealed partial class InventoryReportTabViewModel(IInventoryReportService inventoryReportService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    public IReadOnlyList<string> ReportTypes { get; } = session.HasPermission(PermissionKeys.PricingViewPurchasePrice)
        ?
        [
            "Current Inventory", "Inventory Valuation", "Low Stock", "Out of Stock", "Overstock",
            "Stock Movement History", "Damaged Stock", "Expired Batches", "Expiring Soon", "Batch Summary",
        ]
        :
        [
            "Current Inventory", "Low Stock", "Out of Stock", "Overstock",
            "Stock Movement History", "Damaged Stock", "Expired Batches", "Expiring Soon", "Batch Summary",
        ];

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValuationSelected))]
    [NotifyPropertyChangedFor(nameof(NeedsDateRange))]
    private string _selectedReportType = "Current Inventory";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _valuationTotalText = "₹0.00";
    [ObservableProperty] private string _valuationUnitsText = "0";
    [ObservableProperty] private string _valuationProductCountText = "0";

    public bool IsValuationSelected => SelectedReportType == "Inventory Valuation";
    public bool NeedsDateRange => SelectedReportType is "Stock Movement History" or "Damaged Stock";

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
                case "Current Inventory":
                    LoadInventoryRows(await inventoryReportService.GetCurrentInventoryAsync(null, CurrentUserId), "Current Inventory");
                    break;
                case "Inventory Valuation":
                    await LoadValuationAsync();
                    break;
                case "Low Stock":
                    LoadInventoryRows(await inventoryReportService.GetLowStockAsync(CurrentUserId), "Low Stock");
                    break;
                case "Out of Stock":
                    LoadInventoryRows(await inventoryReportService.GetOutOfStockAsync(CurrentUserId), "Out of Stock");
                    break;
                case "Overstock":
                    LoadInventoryRows(await inventoryReportService.GetOverstockAsync(CurrentUserId), "Overstock");
                    break;
                case "Stock Movement History":
                    LoadMovementRows(await inventoryReportService.GetStockMovementHistoryAsync(range, null, CurrentUserId));
                    break;
                case "Damaged Stock":
                    LoadMovementRows(await inventoryReportService.GetDamagedStockAsync(range, CurrentUserId));
                    break;
                case "Expired Batches":
                    LoadBatchRows(await inventoryReportService.GetExpiredBatchesAsync(CurrentUserId), "Expired Batches");
                    break;
                case "Expiring Soon":
                    LoadBatchRows(await inventoryReportService.GetExpiringSoonAsync(30, CurrentUserId), "Expiring Within 30 Days");
                    break;
                case "Batch Summary":
                    LoadBatchRows(await inventoryReportService.GetBatchSummaryAsync(CurrentUserId), "Batch Summary");
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

    private async Task LoadValuationAsync()
    {
        var v = await inventoryReportService.GetValuationAsync(CurrentUserId);
        ValuationTotalText = Fmt(v.TotalStockValue);
        ValuationUnitsText = v.TotalUnitsOnHand.ToString("0.###");
        ValuationProductCountText = v.ProductCount.ToString();

        _lastExport = new ReportExportData
        {
            Title = "Inventory Valuation",
            Subtitle = "As of now",
            Columns = ["Metric", "Value"],
            Rows =
            [
                ["Total Stock Value", Fmt(v.TotalStockValue)],
                ["Products with Stock", v.ProductCount.ToString()],
                ["Total Units on Hand", v.TotalUnitsOnHand.ToString("0.###")],
            ],
        };
    }

    private void LoadInventoryRows(IReadOnlyList<InventoryRow> rows, string title)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.ProductName,
                Secondary = r.ProductCode,
                Column1Label = "Category",
                Column1Value = r.CategoryName ?? "—",
                Column2Label = "On Hand",
                Column2Value = $"{r.QuantityOnHand:0.###} {r.Unit}",
                Column3Label = r.StockValue.HasValue ? "Stock Value" : "",
                Column3Value = r.StockValue is { } v ? Fmt(v) : "",
            });
        }

        _lastExport = new ReportExportData
        {
            Title = $"Inventory — {title}",
            Subtitle = "As of now",
            Columns = ["Product", "Code", "Category", "On Hand", "Stock Value"],
            Rows = rows.Select(r => (IReadOnlyList<string>)
                [r.ProductName, r.ProductCode, r.CategoryName ?? "—", $"{r.QuantityOnHand:0.###} {r.Unit}", r.StockValue is { } v ? Fmt(v) : "—"]).ToList(),
        };
    }

    private void LoadMovementRows(IReadOnlyList<StockMovementRow> rows)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.ProductName,
                Secondary = r.ProductCode,
                Column1Label = "Type",
                Column1Value = r.MovementType,
                Column2Label = "Change",
                Column2Value = r.QuantityChange.ToString("0.###"),
                Column3Label = "When",
                Column3Value = r.TimestampUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"),
            });
        }

        _lastExport = new ReportExportData
        {
            Title = "Stock Movement History",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Product", "Code", "Type", "Change", "New Qty", "When", "Reference"],
            Rows = rows.Select(r => (IReadOnlyList<string>)
                [r.ProductName, r.ProductCode, r.MovementType, r.QuantityChange.ToString("0.###"), r.NewQuantity.ToString("0.###"),
                 r.TimestampUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"), r.ReferenceId ?? ""]).ToList(),
        };
    }

    private void LoadBatchRows(IReadOnlyList<BatchSummaryRow> rows, string title)
    {
        foreach (var r in rows)
        {
            Rows.Add(new GenericReportRowViewModel
            {
                Primary = r.ProductName,
                Secondary = $"{r.ProductCode} · Batch {r.BatchNumber}",
                Column1Label = "Qty",
                Column1Value = r.Quantity.ToString("0.###"),
                Column2Label = "Expiry",
                Column2Value = r.ExpiryDate?.ToString("dd-MMM-yyyy") ?? "—",
                Column3Label = r.IsExpired ? "Status" : "",
                Column3Value = r.IsExpired ? "EXPIRED" : "",
            });
        }

        _lastExport = new ReportExportData
        {
            Title = title,
            Subtitle = "As of now",
            Columns = ["Product", "Code", "Batch", "Qty", "Mfg Date", "Expiry", "Expired"],
            Rows = rows.Select(r => (IReadOnlyList<string>)
                [r.ProductName, r.ProductCode, r.BatchNumber, r.Quantity.ToString("0.###"),
                 r.ManufacturingDate?.ToString("dd-MMM-yyyy") ?? "", r.ExpiryDate?.ToString("dd-MMM-yyyy") ?? "", r.IsExpired ? "Yes" : "No"]).ToList(),
        };
    }

    public ReportExportData BuildExportData() => _lastExport ?? throw new InvalidOperationException("Load the report before exporting.");

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
