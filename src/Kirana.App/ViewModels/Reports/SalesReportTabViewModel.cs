using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

public sealed partial class SalesReportTabViewModel(ISalesReportService salesReportService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    public ReportDateFilterViewModel DateFilter { get; } = new();

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _grossSalesText = "₹0.00";
    [ObservableProperty] private string _netSalesText = "₹0.00";
    [ObservableProperty] private string _returnsText = "₹0.00";
    [ObservableProperty] private string _itemDiscountsText = "₹0.00";
    [ObservableProperty] private string _billDiscountsText = "₹0.00";
    [ObservableProperty] private string _totalDiscountsText = "₹0.00";
    [ObservableProperty] private string _gstCollectedText = "₹0.00";
    [ObservableProperty] private string _billCountText = "0";
    [ObservableProperty] private string _averageBillValueText = "₹0.00";
    [ObservableProperty] private string _itemsSoldText = "0";

    [ObservableProperty] private string _salesTaxableText = "₹0.00";
    [ObservableProperty] private string _salesGstText = "₹0.00";
    [ObservableProperty] private string _purchaseTaxableText = "₹0.00";
    [ObservableProperty] private string _purchaseGstText = "₹0.00";

    public ObservableCollection<PaymentMethodAmount> PaymentMethods { get; } = [];
    public ObservableCollection<GstRateBreakdown> SalesGstByRate { get; } = [];
    public ObservableCollection<GstRateBreakdown> PurchaseGstByRate { get; } = [];

    private int? CurrentUserId => session.CurrentUser?.Id;
    private SalesReportSummary? _lastSummary;
    private GstReport? _lastGst;

    public async Task EnsureLoadedAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var range = DateFilter.Resolve();

            var summary = await salesReportService.GetSummaryAsync(range, filter: null, CurrentUserId);
            _lastSummary = summary;
            ApplySummary(summary);

            var gst = await salesReportService.GetGstReportAsync(range, CurrentUserId);
            _lastGst = gst;
            ApplyGst(gst);
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

    private void ApplySummary(SalesReportSummary s)
    {
        GrossSalesText = Fmt(s.GrossSales);
        NetSalesText = Fmt(s.NetSales);
        ReturnsText = Fmt(s.Returns);
        ItemDiscountsText = Fmt(s.ItemDiscounts);
        BillDiscountsText = Fmt(s.BillDiscounts);
        TotalDiscountsText = Fmt(s.TotalDiscounts);
        GstCollectedText = Fmt(s.GstCollected);
        BillCountText = s.BillCount.ToString();
        AverageBillValueText = Fmt(s.AverageBillValue);
        ItemsSoldText = s.ItemsSold.ToString("0.###");

        PaymentMethods.Clear();
        foreach (var p in s.PaymentMethodBreakdown)
        {
            PaymentMethods.Add(p);
        }
    }

    private void ApplyGst(GstReport g)
    {
        SalesTaxableText = Fmt(g.SalesTaxableAmount);
        SalesGstText = Fmt(g.SalesGstCollected);
        PurchaseTaxableText = Fmt(g.PurchaseTaxableAmount);
        PurchaseGstText = Fmt(g.PurchaseGstPaid);

        SalesGstByRate.Clear();
        foreach (var r in g.SalesByRate)
        {
            SalesGstByRate.Add(r);
        }

        PurchaseGstByRate.Clear();
        foreach (var r in g.PurchasesByRate)
        {
            PurchaseGstByRate.Add(r);
        }
    }

    public ReportExportData BuildSalesExportData()
    {
        var s = _lastSummary ?? throw new InvalidOperationException("Load the report before exporting.");
        return new ReportExportData
        {
            Title = "Sales Report",
            Subtitle = DateFilter.Resolve().Label,
            Columns = ["Metric", "Amount"],
            Rows =
            [
                ["Gross Sales", Fmt(s.GrossSales)],
                ["Returns", Fmt(s.Returns)],
                ["Net Sales", Fmt(s.NetSales)],
                ["Item Discounts", Fmt(s.ItemDiscounts)],
                ["Bill Discounts", Fmt(s.BillDiscounts)],
                ["Total Discounts", Fmt(s.TotalDiscounts)],
                ["GST Collected", Fmt(s.GstCollected)],
                ["Bills", s.BillCount.ToString()],
                ["Average Bill Value", Fmt(s.AverageBillValue)],
                ["Items Sold", s.ItemsSold.ToString("0.###")],
                .. s.PaymentMethodBreakdown.Select(p => new[] { $"Payment: {p.Method}", Fmt(p.Amount) }),
            ],
        };
    }

    public ReportExportData BuildGstExportData()
    {
        var g = _lastGst ?? throw new InvalidOperationException("Load the report before exporting.");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var r in g.SalesByRate)
        {
            rows.Add(["Sales", r.GstTreatment, $"{r.RatePercent}%", Fmt(r.TaxableAmount), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.TaxAmount), r.InvoiceCount.ToString()]);
        }

        foreach (var r in g.PurchasesByRate)
        {
            rows.Add(["Purchases", r.GstTreatment, $"{r.RatePercent}%", Fmt(r.TaxableAmount), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.TaxAmount), r.InvoiceCount.ToString()]);
        }

        return new ReportExportData
        {
            Title = "GST Report",
            Subtitle = $"{DateFilter.Resolve().Label} — CGST/SGST assume intra-state; see report notes",
            Columns = ["Type", "GST Treatment", "Rate", "Taxable", "CGST", "SGST", "IGST", "Total Tax", "Invoice Count"],
            Rows = rows,
        };
    }

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);
}
