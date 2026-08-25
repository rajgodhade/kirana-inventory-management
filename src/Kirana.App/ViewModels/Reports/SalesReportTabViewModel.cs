using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels.Reports;

public sealed partial class SalesReportTabViewModel(ISalesReportService salesReportService, ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>The "no narrowing" entry in the price-level filter. Named rather than left blank so
    /// the combo never shows an empty row that reads as "unset by accident".</summary>
    private const string AllPriceLevels = "All price levels";

    public ReportDateFilterViewModel DateFilter { get; } = new();

    /// <summary>Price-level filter (Phase 15B-5). Narrows to the level RECORDED on each bill, so a
    /// later price change cannot move a sale between these buckets.</summary>
    public IReadOnlyList<string> PriceLevelFilterNames { get; } =
        [AllPriceLevels, PriceLevel.Retail.ToDisplayText(), PriceLevel.Wholesale.ToDisplayText()];

    [ObservableProperty] private string _selectedPriceLevelFilterName = AllPriceLevels;

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

    [ObservableProperty] private string _retailSalesText = "₹0.00";
    [ObservableProperty] private string _wholesaleSalesText = "₹0.00";
    [ObservableProperty] private string _retailBillCountText = "0 bills";
    [ObservableProperty] private string _wholesaleBillCountText = "0 bills";

    [ObservableProperty] private string _salesTaxableText = "₹0.00";
    [ObservableProperty] private string _salesGstText = "₹0.00";
    [ObservableProperty] private string _purchaseTaxableText = "₹0.00";
    [ObservableProperty] private string _purchaseGstText = "₹0.00";

    // Phase 18A-5: stored GST split by historical party classification (18A-4). Captions only —
    // jurisdiction components remain in the per-rate rows above.
    [ObservableProperty] private string _salesClassificationText = "";
    [ObservableProperty] private string _purchaseClassificationText = "";

    public ObservableCollection<PaymentMethodAmount> PaymentMethods { get; } = [];
    public ObservableCollection<GstRateBreakdown> SalesGstByRate { get; } = [];
    public ObservableCollection<GstRateBreakdown> PurchaseGstByRate { get; } = [];

    private int? CurrentUserId => session.CurrentUser?.Id;

    // Matched back through ToDisplayText rather than Enum.Parse, so the combo's labels stay the
    // single definition of how a level is spelled for the operator.
    private PriceLevel? SelectedPriceLevel => SelectedPriceLevelFilterName == AllPriceLevels
        ? null
        : Enum.GetValues<PriceLevel>()
            .Cast<PriceLevel?>()
            .FirstOrDefault(l => l!.Value.ToDisplayText() == SelectedPriceLevelFilterName);
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

            // Only the sales summary is narrowed by price level. The GST report deliberately is not:
            // GST is owed on everything sold in the period regardless of which level it was billed
            // at, so a filtered GST figure would be a number no one should be filing.
            var filter = SelectedPriceLevel is { } level ? new ReportFilter { PriceLevel = level } : null;

            var summary = await salesReportService.GetSummaryAsync(range, filter, CurrentUserId);
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

        RetailSalesText = Fmt(s.RetailSales);
        WholesaleSalesText = Fmt(s.WholesaleSales);
        RetailBillCountText = BillCountLabel(s.RetailBillCount);
        WholesaleBillCountText = BillCountLabel(s.WholesaleBillCount);

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
        SalesClassificationText = $"B2B {Fmt(g.SalesB2bGst)} · B2C {Fmt(g.SalesB2cGst)} · Unresolved identity {Fmt(g.SalesUnresolvedIdentityGst)}";
        PurchaseClassificationText = $"Registered supplier {Fmt(g.PurchaseRegisteredSupplierGst)} · Unregistered supplier {Fmt(g.PurchaseUnregisteredSupplierGst)} · Unresolved {Fmt(g.PurchaseUnresolvedSupplierGst)}";

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
            // The level filter belongs in the subtitle: without it an exported "Gross Sales" of a
            // filtered report is indistinguishable from an unfiltered one once it leaves the app.
            Subtitle = SelectedPriceLevel is null
                ? DateFilter.Resolve().Label
                : $"{DateFilter.Resolve().Label} — {SelectedPriceLevelFilterName} only",
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
                ["Retail Sales", Fmt(s.RetailSales)],
                ["Wholesale Sales", Fmt(s.WholesaleSales)],
                ["Retail Bills", s.RetailBillCount.ToString()],
                ["Wholesale Bills", s.WholesaleBillCount.ToString()],
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
            rows.Add(["Sales", r.GstTreatment, $"{r.RatePercent}%", Fmt(r.TaxableAmount), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.UnresolvedGst), Fmt(r.TaxAmount), r.InvoiceCount.ToString()]);
        }

        foreach (var r in g.PurchasesByRate)
        {
            rows.Add(["Purchases", r.GstTreatment, $"{r.RatePercent}%", Fmt(r.TaxableAmount), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.UnresolvedGst), Fmt(r.TaxAmount), r.InvoiceCount.ToString()]);
        }

        return new ReportExportData
        {
            Title = "GST Report",
            Subtitle = $"{DateFilter.Resolve().Label} — jurisdiction uses immutable transaction StateCode snapshots",
            Columns = ["Type", "GST Treatment", "Rate", "Taxable", "CGST", "SGST", "IGST", "Unresolved GST", "Total Tax", "Invoice Count"],
            Rows = rows,
        };
    }

    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", Inr);

    private static string BillCountLabel(int count) => count == 1 ? "1 bill" : $"{count} bills";
}
