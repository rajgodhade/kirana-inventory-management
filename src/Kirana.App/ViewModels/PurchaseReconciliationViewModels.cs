using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed class PurchaseReconciliationRowViewModel
{
    public required PurchaseReconciliationRecord Record { get; init; }
    public int PurchaseOrderId => Record.PurchaseOrderId;
    public string PurchaseOrderNumber => Record.PurchaseOrderNumber;
    public string Supplier => Record.SupplierName;
    public string Date => Record.OrderDateUtc.ToLocalTime().ToString("dd MMM yyyy");
    public string Ordered => FormatQuantities(Record.Lines, x => x.OrderedQuantity);
    public string Received => FormatQuantities(Record.Lines, x => x.ReceivedQuantity);
    public string Purchased => FormatQuantities(Record.Lines, x => x.PurchasedQuantity);
    public string PendingReceipt => FormatQuantities(Record.Lines, x => x.PendingReceiptQuantity);
    public string PendingInvoice => FormatQuantities(Record.Lines, x => x.PendingInvoiceQuantity);
    public decimal Expected => Record.ExpectedValue;
    public decimal Actual => Record.ActualValue;
    public decimal Variance => Record.TotalVariance;
    public string Status => StatusText(Record);
    public bool HasException => Record.Has(PurchaseReconciliationFlags.Exception);

    internal static string StatusText(PurchaseReconciliationRecord record)
    {
        var labels = new List<string>();
        if (record.Has(PurchaseReconciliationFlags.OverReceived)) labels.Add("Over-received");
        if (record.Has(PurchaseReconciliationFlags.OverInvoiced)) labels.Add("Over-invoiced");
        if (record.Has(PurchaseReconciliationFlags.AwaitingReceipt)) labels.Add("Awaiting receipt");
        else if (record.Has(PurchaseReconciliationFlags.PartiallyReceived)) labels.Add("Partial receipt");
        if (record.Has(PurchaseReconciliationFlags.AwaitingPurchase)) labels.Add("Awaiting purchase");
        else if (record.Has(PurchaseReconciliationFlags.PendingPurchase)) labels.Add("Pending invoice");
        if (record.Has(PurchaseReconciliationFlags.PriceMismatch)) labels.Add("Price mismatch");
        if (record.Has(PurchaseReconciliationFlags.TaxMismatch)) labels.Add("Tax mismatch");
        if (record.Has(PurchaseReconciliationFlags.FullyReconciled)) labels.Add("Fully reconciled");
        return labels.Count == 0 ? "Review" : string.Join(" \u00b7 ", labels);
    }

    internal static string FormatQuantities(
        IEnumerable<PurchaseReconciliationLine> lines,
        Func<PurchaseReconciliationLine, decimal> selector) =>
        string.Join(" \u00b7 ", lines.GroupBy(x => x.Unit).Select(group =>
            $"{group.Sum(selector):0.###} {group.Key}"));
}

public sealed partial class PurchaseReconciliationsViewModel(
    IPurchaseReconciliationService service,
    ISupplierService supplierService,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<PurchaseReconciliationRowViewModel> Records { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<SearchSuggestionItem> SearchSuggestions { get; } = [];
    private readonly List<SearchSuggestionItem> _searchCatalog = [];
    public IReadOnlyList<string> FilterOptions { get; } =
        ["All", "Fully reconciled", "Pending receipt", "Pending purchase", "Quantity mismatch", "Price mismatch", "Tax mismatch", "Exceptions"];
    public IReadOnlyList<string> SortOptions { get; } = ["Newest", "Oldest", "Supplier", "Highest variance"];
    public int? CurrentUserId => session.CurrentUser?.Id;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private string _selectedSort = "Newest";
    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    [ObservableProperty] private string _calculatedAt = string.Empty;
    [ObservableProperty] private int _totalPurchaseOrders;
    [ObservableProperty] private int _fullyReconciled;
    [ObservableProperty] private int _pendingReceipt;
    [ObservableProperty] private int _pendingPurchase;
    [ObservableProperty] private int _exceptions;
    [ObservableProperty] private int _quantityExceptions;
    [ObservableProperty] private int _priceExceptions;
    [ObservableProperty] private int _taxExceptions;
    [ObservableProperty] private decimal _expectedValue;
    [ObservableProperty] private decimal _actualValue;
    [ObservableProperty] private decimal _variance;

    public async Task InitializeAsync()
    {
        if (Suppliers.Count == 0)
            foreach (var supplier in await supplierService.SearchAsync(new SupplierSearchQuery(), CurrentUserId))
                Suppliers.Add(supplier);
        await SearchAsync();
    }

    public async Task SearchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await service.SearchAsync(new PurchaseReconciliationQuery
            {
                SearchText = SearchText,
                SupplierId = SelectedSupplier?.Id,
                FromUtc = FromDate?.UtcDateTime,
                ToUtc = ToDate?.UtcDateTime.Date.AddDays(1).AddTicks(-1),
                Filter = ParseFilter(SelectedFilter),
                Sort = ParseSort(SelectedSort),
            }, CurrentUserId);
            Records.Clear();
            foreach (var record in result.Records)
                Records.Add(new PurchaseReconciliationRowViewModel { Record = record });
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                _searchCatalog.Clear();
                foreach (var row in Records)
                {
                    var record = row.Record;
                    _searchCatalog.Add(new SearchSuggestionItem(row.PurchaseOrderNumber, row.PurchaseOrderNumber,
                        $"Purchase order · {row.Supplier}", $"{row.PurchaseOrderNumber} {row.Supplier}"));
                    foreach (var receipt in record.GoodsReceipts)
                        _searchCatalog.Add(new SearchSuggestionItem(receipt.Number, receipt.Number,
                            $"Goods receipt · {row.PurchaseOrderNumber}", $"{receipt.Number} {row.PurchaseOrderNumber} {row.Supplier}"));
                    foreach (var purchase in record.Purchases)
                        _searchCatalog.Add(new SearchSuggestionItem(purchase.Number, purchase.Number,
                            $"Purchase · {row.Supplier}", $"{purchase.Number} {row.PurchaseOrderNumber} {row.Supplier}"));
                }
                foreach (var supplier in Suppliers)
                    _searchCatalog.Add(new SearchSuggestionItem(supplier.Name, supplier.Name, "Supplier", supplier.Name));
            }
            UpdateSearchSuggestions(SearchText);
            HasResults = Records.Count > 0;
            CalculatedAt = $"Calculated {result.CalculatedAtUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}";
            ApplyMetrics(result.Metrics);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedSupplier = null;
        SelectedFilter = "All";
        SelectedSort = "Newest";
        FromDate = ToDate = null;
    }

    public void UpdateSearchSuggestions(string? text) =>
        SearchSuggestionCollection.Update(SearchSuggestions, _searchCatalog, text);

    public ReportExportData BuildExportData() => new()
    {
        Title = "Purchase Reconciliation Report",
        Subtitle = CalculatedAt,
        Columns = ["PO", "Supplier", "Ordered", "Received", "Purchased", "Pending Receipt", "Pending Invoice", "Expected Value", "Actual Value", "Variance", "Status"],
        Rows = Records.Select(row => (IReadOnlyList<string>)
        [
            row.PurchaseOrderNumber, row.Supplier, row.Ordered, row.Received, row.Purchased,
            PurchaseReconciliationRowViewModel.FormatQuantities(row.Record.Lines, x => x.PendingReceiptQuantity),
            PurchaseReconciliationRowViewModel.FormatQuantities(row.Record.Lines, x => x.PendingInvoiceQuantity),
            row.Expected.ToString("0.00"), row.Actual.ToString("0.00"), row.Variance.ToString("+0.00;-0.00;0.00"), row.Status,
        ]).ToList(),
    };

    private void ApplyMetrics(PurchaseReconciliationMetrics metrics)
    {
        TotalPurchaseOrders = metrics.TotalPurchaseOrders;
        FullyReconciled = metrics.FullyReconciled;
        PendingReceipt = metrics.PendingReceipt;
        PendingPurchase = metrics.PendingPurchase;
        Exceptions = metrics.Exceptions;
        QuantityExceptions = metrics.QuantityExceptions;
        PriceExceptions = metrics.PriceExceptions;
        TaxExceptions = metrics.TaxExceptions;
        ExpectedValue = metrics.ExpectedPurchaseValue;
        ActualValue = metrics.ActualPurchaseValue;
        Variance = metrics.TotalVariance;
    }

    private static PurchaseReconciliationFilter ParseFilter(string value) => value switch
    {
        "Fully reconciled" => PurchaseReconciliationFilter.FullyReconciled,
        "Pending receipt" => PurchaseReconciliationFilter.PendingReceipt,
        "Pending purchase" => PurchaseReconciliationFilter.PendingPurchase,
        "Quantity mismatch" => PurchaseReconciliationFilter.QuantityMismatch,
        "Price mismatch" => PurchaseReconciliationFilter.PriceMismatch,
        "Tax mismatch" => PurchaseReconciliationFilter.TaxMismatch,
        "Exceptions" => PurchaseReconciliationFilter.Exceptions,
        _ => PurchaseReconciliationFilter.All,
    };

    private static PurchaseReconciliationSort ParseSort(string value) => value switch
    {
        "Oldest" => PurchaseReconciliationSort.Oldest,
        "Supplier" => PurchaseReconciliationSort.Supplier,
        "Highest variance" => PurchaseReconciliationSort.HighestVariance,
        _ => PurchaseReconciliationSort.Newest,
    };
}

public sealed class PurchaseReconciliationLineViewModel
{
    public required PurchaseReconciliationLine Line { get; init; }
    public string Product => Line.ProductName;
    public string ProductCode => Line.ProductCode;
    public string Unit => Line.Unit;
    public decimal Ordered => Line.OrderedQuantity;
    public decimal Received => Line.ReceivedQuantity;
    public decimal Purchased => Line.PurchasedQuantity;
    public decimal PendingReceipt => Line.PendingReceiptQuantity;
    public decimal PendingInvoice => Line.PendingInvoiceQuantity;
    public decimal ExpectedCost => Line.ExpectedUnitCost;
    public string ActualCost => Line.ActualUnitCost is { } value ? $"\u20b9{value:0.00}" : "\u2014";
    public string CostVariance => Line.UnitCostVariance is { } value ? $"\u20b9{value:+0.00;-0.00;0.00}" : "\u2014";
    public decimal ExpectedTotal => Line.ExpectedTotal;
    public decimal ActualTotal => Line.ActualTotal;
    public decimal TotalVariance => Line.TotalVariance;
    public decimal ExpectedTax => Line.ExpectedTax;
    public decimal ActualTax => Line.ActualTax;
    public decimal TaxVariance => Line.TaxVariance;
    public string Status
    {
        get
        {
            var labels = new List<string>();
            if (Line.OverReceivedQuantity > 0) labels.Add($"Over-received {Line.OverReceivedQuantity:0.###}");
            if (Line.OverInvoicedQuantity > 0) labels.Add($"Over-invoiced {Line.OverInvoicedQuantity:0.###}");
            if (Line.PendingReceiptQuantity > 0) labels.Add($"Receipt pending {Line.PendingReceiptQuantity:0.###}");
            if (Line.PendingInvoiceQuantity > 0) labels.Add($"Invoice pending {Line.PendingInvoiceQuantity:0.###}");
            if ((Line.Flags & PurchaseReconciliationFlags.PriceMismatch) != 0) labels.Add("Price mismatch");
            if ((Line.Flags & PurchaseReconciliationFlags.TaxMismatch) != 0) labels.Add("Tax mismatch");
            return labels.Count == 0 ? "Matched" : string.Join(" \u00b7 ", labels);
        }
    }
}

public sealed class PurchaseReconciliationDocumentViewModel
{
    public required string Type { get; init; }
    public required PurchaseReconciliationDocument Document { get; init; }
    public int Id => Document.Id;
    public string Number => Document.Number;
    public string Date => Document.DateUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");
    public string Status => Document.Status;
    public string Quantity => Document.Quantity.ToString("0.###");
}

public sealed partial class PurchaseReconciliationDetailsViewModel(
    IPurchaseReconciliationService service,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<PurchaseReconciliationLineViewModel> Lines { get; } = [];
    public ObservableCollection<PurchaseReconciliationDocumentViewModel> Documents { get; } = [];
    public ObservableCollection<string> Issues { get; } = [];
    public int? CurrentUserId => session.CurrentUser?.Id;
    public PurchaseReconciliationRecord? Record { get; private set; }
    [ObservableProperty] private string _purchaseOrderNumber = string.Empty;
    [ObservableProperty] private string _supplier = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _calculatedAt = string.Empty;
    [ObservableProperty] private string _ordered = string.Empty;
    [ObservableProperty] private string _received = string.Empty;
    [ObservableProperty] private string _purchased = string.Empty;
    [ObservableProperty] private string _pendingReceiptText = string.Empty;
    [ObservableProperty] private string _pendingInvoiceText = string.Empty;
    [ObservableProperty] private decimal _expectedValue;
    [ObservableProperty] private decimal _actualValue;
    [ObservableProperty] private decimal _totalVariance;
    [ObservableProperty] private decimal _expectedTax;
    [ObservableProperty] private decimal _actualTax;
    [ObservableProperty] private decimal _taxVariance;
    [ObservableProperty] private decimal _expectedDiscount;
    [ObservableProperty] private decimal _actualDiscount;
    [ObservableProperty] private decimal _discountVariance;
    [ObservableProperty] private bool _hasIssues;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task LoadAsync(int purchaseOrderId)
    {
        IsBusy = true; ErrorMessage = null;
        try
        {
            Record = await service.GetByPurchaseOrderIdAsync(purchaseOrderId, CurrentUserId)
                ?? throw new InvalidOperationException("Purchase reconciliation is not available for this purchase order.");
            PurchaseOrderNumber = Record.PurchaseOrderNumber;
            Supplier = $"{Record.SupplierName} ({Record.SupplierCode})";
            Status = PurchaseReconciliationRowViewModel.StatusText(Record);
            CalculatedAt = $"Calculated {Record.CalculatedAtUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}";
            Ordered = PurchaseReconciliationRowViewModel.FormatQuantities(Record.Lines, x => x.OrderedQuantity);
            Received = PurchaseReconciliationRowViewModel.FormatQuantities(Record.Lines, x => x.ReceivedQuantity);
            Purchased = PurchaseReconciliationRowViewModel.FormatQuantities(Record.Lines, x => x.PurchasedQuantity);
            PendingReceiptText = PurchaseReconciliationRowViewModel.FormatQuantities(Record.Lines, x => x.PendingReceiptQuantity);
            PendingInvoiceText = PurchaseReconciliationRowViewModel.FormatQuantities(Record.Lines, x => x.PendingInvoiceQuantity);
            ExpectedValue = Record.ExpectedValue; ActualValue = Record.ActualValue; TotalVariance = Record.TotalVariance;
            ExpectedTax = Record.ExpectedTax; ActualTax = Record.ActualTax; TaxVariance = Record.TaxVariance;
            ExpectedDiscount = Record.ExpectedDiscount; ActualDiscount = Record.ActualDiscount; DiscountVariance = Record.DiscountVariance;
            Lines.Clear(); foreach (var line in Record.Lines) Lines.Add(new PurchaseReconciliationLineViewModel { Line = line });
            Documents.Clear();
            foreach (var receipt in Record.GoodsReceipts) Documents.Add(new PurchaseReconciliationDocumentViewModel { Type = "GRN", Document = receipt });
            foreach (var purchase in Record.Purchases) Documents.Add(new PurchaseReconciliationDocumentViewModel { Type = "Purchase", Document = purchase });
            BuildIssues(Record);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void BuildIssues(PurchaseReconciliationRecord record)
    {
        Issues.Clear();
        foreach (var line in record.Lines)
        {
            if (line.PendingReceiptQuantity > 0) Issues.Add($"{line.ProductName}: {line.PendingReceiptQuantity:0.###} {line.Unit} not received.");
            if (line.PendingInvoiceQuantity > 0) Issues.Add($"{line.ProductName}: {line.PendingInvoiceQuantity:0.###} {line.Unit} received but not invoiced.");
            if (line.OverReceivedQuantity > 0) Issues.Add($"{line.ProductName}: received quantity exceeds the order by {line.OverReceivedQuantity:0.###} {line.Unit}.");
            if (line.OverInvoicedQuantity > 0) Issues.Add($"{line.ProductName}: invoiced quantity exceeds receipt by {line.OverInvoicedQuantity:0.###} {line.Unit}.");
            if ((line.Flags & PurchaseReconciliationFlags.PriceMismatch) != 0) Issues.Add($"{line.ProductName}: actual unit cost differs from expected by \u20b9{line.UnitCostVariance:+0.00;-0.00;0.00}.");
            if ((line.Flags & PurchaseReconciliationFlags.TaxMismatch) != 0) Issues.Add($"{line.ProductName}: stored GST differs from the PO expectation.");
        }
        HasIssues = Issues.Count > 0;
    }
}
