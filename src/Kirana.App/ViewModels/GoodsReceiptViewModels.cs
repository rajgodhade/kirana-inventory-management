using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed class GoodsReceiptRowViewModel
{
    public required int Id { get; init; }
    public required int PurchaseOrderId { get; init; }
    public required string Number { get; init; }
    public required string PurchaseOrderNumber { get; init; }
    public required string Supplier { get; init; }
    public required string ReceivedDate { get; init; }
    public required GoodsReceiptStatus Status { get; init; }
    public required int TotalItems { get; init; }
    public required string CreatedBy { get; init; }
    public bool IsDraft => Status == GoodsReceiptStatus.Draft;
    public bool CanCreatePurchase { get; init; }
}

public sealed partial class GoodsReceiptsViewModel(
    IGoodsReceiptService service, ISupplierService supplierService, ManagementSession session) : ObservableObject
{
    public const string AllStatuses = "All statuses";
    public ObservableCollection<GoodsReceiptRowViewModel> Receipts { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<SearchSuggestionItem> SearchSuggestions { get; } = [];
    private readonly List<SearchSuggestionItem> _searchCatalog = [];
    public IReadOnlyList<string> StatusOptions { get; } = [AllStatuses, .. Enum.GetNames<GoodsReceiptStatus>()];
    public IReadOnlyList<string> SortOptions { get; } = ["Newest", "Oldest"];
    public int? CurrentUserId => session.CurrentUser?.Id;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _selectedStatus = AllStatuses;
    [ObservableProperty] private string _selectedSort = "Newest";
    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _pendingPurchaseCount;

    public async Task InitializeAsync()
    {
        if (Suppliers.Count == 0)
            foreach (var supplier in await supplierService.SearchAsync(new SupplierSearchQuery(), CurrentUserId)) Suppliers.Add(supplier);
        await SearchAsync();
    }

    public async Task SearchAsync()
    {
        IsBusy = true; ErrorMessage = null;
        try
        {
            var status = Enum.TryParse<GoodsReceiptStatus>(SelectedStatus, out var parsed) ? parsed : null as GoodsReceiptStatus?;
            var results = await service.SearchAsync(new GoodsReceiptSearchQuery
            {
                SearchText = SearchText, SupplierId = SelectedSupplier?.Id, Status = status,
                FromUtc = FromDate?.UtcDateTime, ToUtc = ToDate?.UtcDateTime.Date.AddDays(1).AddTicks(-1),
                OldestFirst = SelectedSort == "Oldest",
            }, CurrentUserId);
            Receipts.Clear();
            foreach (var receipt in results) Receipts.Add(new GoodsReceiptRowViewModel
            {
                Id = receipt.Id, PurchaseOrderId = receipt.PurchaseOrderId, Number = receipt.GoodsReceiptNumber,
                PurchaseOrderNumber = receipt.PurchaseOrder.PurchaseOrderNumber,
                Supplier = receipt.SupplierNameSnapshot,
                ReceivedDate = receipt.ReceivedAtUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"),
                Status = receipt.Status, TotalItems = receipt.Items.Count,
                CreatedBy = receipt.CreatedByUser?.FullName ?? "System",
                CanCreatePurchase = receipt.Status == GoodsReceiptStatus.Completed && receipt.Purchase is null,
            });
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                _searchCatalog.Clear();
                foreach (var receipt in Receipts)
                {
                    _searchCatalog.Add(new SearchSuggestionItem(receipt.Number, receipt.Number,
                        $"Goods receipt · {receipt.PurchaseOrderNumber} · {receipt.Supplier}",
                        $"{receipt.Number} {receipt.PurchaseOrderNumber} {receipt.Supplier}"));
                    _searchCatalog.Add(new SearchSuggestionItem(receipt.PurchaseOrderNumber, receipt.PurchaseOrderNumber,
                        $"Purchase order · {receipt.Supplier}", $"{receipt.PurchaseOrderNumber} {receipt.Supplier}"));
                }
                foreach (var supplier in Suppliers)
                    _searchCatalog.Add(new SearchSuggestionItem(supplier.Name, supplier.Name, "Supplier", supplier.Name));
            }
            UpdateSearchSuggestions(SearchText);
            HasResults = Receipts.Count > 0; TotalCount = Receipts.Count;
            CompletedCount = Receipts.Count(r => r.Status == GoodsReceiptStatus.Completed);
            PendingPurchaseCount = Receipts.Count(r => r.CanCreatePurchase);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void ClearFilters() { SearchText = string.Empty; SelectedSupplier = null; SelectedStatus = AllStatuses; SelectedSort = "Newest"; FromDate = ToDate = null; }
    public void UpdateSearchSuggestions(string? text) =>
        SearchSuggestionCollection.Update(SearchSuggestions, _searchCatalog, text);
    public Task<GoodsReceipt?> GetAsync(int id) => service.GetByIdAsync(id, CurrentUserId);
    public Task CancelAsync(int id, string reason) => service.CancelAsync(new CancelGoodsReceiptRequest { GoodsReceiptId = id, Reason = reason, PerformedByUserId = CurrentUserId });
}

public sealed partial class GoodsReceiptLineViewModel : ObservableObject
{
    public required int PurchaseOrderItemId { get; init; }
    public required int ProductId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductCode { get; init; }
    public required UnitOfMeasure Unit { get; init; }
    public required decimal Ordered { get; init; }
    public required decimal PreviouslyReceived { get; init; }
    public required decimal RemainingBefore { get; init; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingAfter))]
    private string _receivedText = "0";
    public decimal Received => decimal.TryParse(ReceivedText, out var value) ? value : 0;
    public decimal RemainingAfter => Math.Max(0, RemainingBefore - Received);
}

public sealed partial class GoodsReceiptEntryViewModel(IGoodsReceiptService service, ManagementSession session) : ObservableObject
{
    public ObservableCollection<GoodsReceiptLineViewModel> Lines { get; } = [];
    public int? CurrentUserId => session.CurrentUser?.Id;
    public int PurchaseOrderId { get; private set; }
    public string PurchaseOrderNumber { get; private set; } = string.Empty;
    public string SupplierName { get; private set; } = string.Empty;
    public string OrderDate { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    [ObservableProperty] private DateTimeOffset? _receivedDate = DateTimeOffset.Now;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    public decimal TotalReceived => Lines.Sum(l => l.Received);
    public decimal TotalRemaining => Lines.Sum(l => l.RemainingAfter);

    public async Task InitializeAsync(int purchaseOrderId)
    {
        var preview = await service.GetReceiptPreviewAsync(purchaseOrderId, CurrentUserId);
        PurchaseOrderId = preview.PurchaseOrderId; PurchaseOrderNumber = preview.PurchaseOrderNumber;
        SupplierName = preview.SupplierName; OrderDate = preview.OrderDateUtc.ToLocalTime().ToString("dd MMM yyyy"); Status = preview.Status.ToString();
        Lines.Clear();
        foreach (var line in preview.Lines) Lines.Add(new GoodsReceiptLineViewModel
        {
            PurchaseOrderItemId = line.PurchaseOrderItemId, ProductId = line.ProductId,
            ProductName = line.ProductName, ProductCode = line.ProductCode, Unit = line.Unit,
            Ordered = line.OrderedQuantity, PreviouslyReceived = line.PreviouslyReceivedQuantity, RemainingBefore = line.RemainingQuantity,
        });
        OnPropertyChanged(nameof(PurchaseOrderNumber)); OnPropertyChanged(nameof(SupplierName));
        OnPropertyChanged(nameof(OrderDate)); OnPropertyChanged(nameof(Status));
    }

    public void RefreshTotals() { OnPropertyChanged(nameof(TotalReceived)); OnPropertyChanged(nameof(TotalRemaining)); }

    public string Validate()
    {
        if (Lines.All(l => l.Received <= 0)) return "Enter a positive received quantity for at least one item.";
        foreach (var line in Lines)
        {
            if (line.Received < 0) return $"Received quantity for {line.ProductName} cannot be negative.";
            if (line.Received > line.RemainingBefore) return $"Only {line.RemainingBefore:0.###} {line.Unit} remains for {line.ProductName}.";
            if (!line.Unit.SupportsDecimalQuantity() && line.Received != Math.Floor(line.Received)) return $"{line.ProductName} requires a whole quantity.";
        }
        return string.Empty;
    }

    public async Task<GoodsReceipt?> CompleteAsync()
    {
        ErrorMessage = Validate(); if (!string.IsNullOrEmpty(ErrorMessage)) return null;
        IsBusy = true;
        try
        {
            var draft = await service.CreateDraftAsync(new CreateGoodsReceiptDraftRequest
            {
                PurchaseOrderId = PurchaseOrderId, ReceivedAtUtc = ReceivedDate?.UtcDateTime,
                Notes = Notes, PerformedByUserId = CurrentUserId,
                Lines = Lines.Where(l => l.Received > 0).Select(l => new GoodsReceiptLineInput
                { PurchaseOrderItemId = l.PurchaseOrderItemId, ReceivedQuantity = l.Received }).ToList(),
            });
            return await service.CompleteAsync(draft.Id, CurrentUserId);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; return null; }
        finally { IsBusy = false; }
    }
}
