using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed record PurchaseOrderPrefillLine(int ProductId, decimal Quantity, decimal? EstimatedUnitCost);
public sealed record PurchaseOrderPrefill(
    int? SupplierId,
    IReadOnlyList<PurchaseOrderPrefillLine> Lines,
    IReadOnlyCollection<int>? PrioritizedProductIds = null);

public sealed class PurchaseOrderRowViewModel
{
    public required int Id { get; init; }
    public required string Number { get; init; }
    public required string Supplier { get; init; }
    public required string Date { get; init; }
    public required PurchaseOrderStatus Status { get; init; }
    public required decimal Total { get; init; }
    public required string CreatedBy { get; init; }
    public bool IsDraft => Status == PurchaseOrderStatus.Draft;
    public bool CanCancel => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted;
    public bool CanReceive => Status is PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived;
    public bool CanReconcile => Status is not PurchaseOrderStatus.Draft and not PurchaseOrderStatus.Cancelled;
}

public sealed partial class PurchaseOrdersViewModel(IPurchaseOrderService service, ManagementSession session) : ObservableObject
{
    public const string AllStatuses = "All statuses";
    public IReadOnlyList<string> StatusOptions { get; } = [AllStatuses, .. Enum.GetNames<PurchaseOrderStatus>()];
    public IReadOnlyList<string> SortOptions { get; } = ["Newest", "Oldest", "PO number", "Supplier", "Highest total"];
    public ObservableCollection<PurchaseOrderRowViewModel> Orders { get; } = [];
    public ObservableCollection<SearchSuggestionItem> SearchSuggestions { get; } = [];
    private readonly List<SearchSuggestionItem> _searchCatalog = [];
    public int? CurrentUserId => session.CurrentUser?.Id;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedStatus = AllStatuses;
    [ObservableProperty] private string _selectedSort = "Newest";
    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private decimal _expectedTotal;
    [ObservableProperty] private int _draftCount;
    [ObservableProperty] private int _submittedCount;

    public async Task SearchAsync()
    {
        IsBusy = true; ErrorMessage = null;
        try
        {
            var status = Enum.TryParse<PurchaseOrderStatus>(SelectedStatus, out var parsed) ? parsed : null as PurchaseOrderStatus?;
            var sort = SelectedSort switch { "Oldest" => PurchaseOrderSort.Oldest, "PO number" => PurchaseOrderSort.Number,
                "Supplier" => PurchaseOrderSort.Supplier, "Highest total" => PurchaseOrderSort.HighestTotal, _ => PurchaseOrderSort.Newest };
            var results = await service.SearchAsync(new PurchaseOrderSearchQuery
            {
                SearchText = SearchText, Status = status, Sort = sort, FromUtc = FromDate?.UtcDateTime,
                ToUtc = ToDate?.UtcDateTime.Date.AddDays(1).AddTicks(-1),
            }, CurrentUserId);
            Orders.Clear();
            foreach (var order in results) Orders.Add(new PurchaseOrderRowViewModel
            {
                Id = order.Id, Number = order.PurchaseOrderNumber, Supplier = order.SupplierNameSnapshot,
                Date = order.OrderDateUtc.ToLocalTime().ToString("dd MMM yyyy"), Status = order.Status,
                Total = order.GrandTotal, CreatedBy = order.CreatedByUser?.FullName ?? "System",
            });
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                _searchCatalog.Clear();
                foreach (var order in Orders)
                {
                    _searchCatalog.Add(new SearchSuggestionItem(order.Number, order.Number,
                        $"Purchase order · {order.Supplier}", $"{order.Number} {order.Supplier}"));
                    _searchCatalog.Add(new SearchSuggestionItem(order.Supplier, order.Supplier,
                        $"Purchase order · {order.Number}", $"{order.Supplier} {order.Number}"));
                }
            }
            UpdateSearchSuggestions(SearchText);
            HasResults = Orders.Count > 0; TotalCount = Orders.Count; ExpectedTotal = Orders.Sum(o => o.Total);
            DraftCount = Orders.Count(o => o.Status == PurchaseOrderStatus.Draft);
            SubmittedCount = Orders.Count(o => o.Status == PurchaseOrderStatus.Submitted);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void ClearFilters() { SearchText = string.Empty; SelectedStatus = AllStatuses; SelectedSort = "Newest"; FromDate = ToDate = null; }
    public void UpdateSearchSuggestions(string? text) =>
        SearchSuggestionCollection.Update(SearchSuggestions, _searchCatalog, text);
    public Task<PurchaseOrder?> GetAsync(int id) => service.GetByIdAsync(id, CurrentUserId);
    public Task SubmitAsync(int id) => service.SubmitAsync(id, CurrentUserId);
    public Task CancelAsync(int id, string reason) => service.CancelAsync(new CancelPurchaseOrderRequest { PurchaseOrderId = id, Reason = reason, PerformedByUserId = CurrentUserId });
}

/// <summary>
/// One row in the multi-select product picker. Selection lives on the item so the checkbox can
/// bind two-way, but <see cref="IsAlreadyAdded"/> products refuse to become selected — the picker
/// must never be able to queue a duplicate PO line.
/// </summary>
public sealed partial class ProductPickerItemViewModel : ObservableObject
{
    public required int ProductId { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required bool IsAlreadyAdded { get; init; }
    public string? GroupHeading { get; init; }
    public bool ShowGroupDivider { get; init; }
    public bool IsReplenishmentSuggestion { get; init; }

    /// <summary>Selectable state for the UI — an already-added product is shown but inert.</summary>
    public bool IsSelectable => !IsAlreadyAdded;

    /// <summary>
    /// Raised for every change to <see cref="IsSelected"/>, whoever caused it. A checkbox click is
    /// handled by the checkbox itself and never raises the list's ItemClick, so without this the
    /// owning view model would not see ticks made directly on the box.
    /// </summary>
    internal Action<ProductPickerItemViewModel>? SelectionChanged { get; set; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && IsAlreadyAdded)
        {
            IsSelected = false;
            return;
        }
        SelectionChanged?.Invoke(this);
    }

    /// <summary>Screen-reader text: state must not be conveyed by the checkbox colour alone.</summary>
    public string AccessibleName => IsAlreadyAdded
        ? $"{Title}, {Detail}, already added to this order"
        : $"{Title}, {Detail}";
}

public sealed partial class PurchaseOrderLineViewModel : ObservableObject
{
    public required int ProductId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductCode { get; init; }
    public required string Unit { get; init; }
    public required bool SupportsDecimalQuantity { get; init; }
    public required decimal GstRate { get; init; }
    public required PricingType PricingType { get; init; }
    [ObservableProperty] private string _quantityText = "1";
    [ObservableProperty] private string _unitCostText = "0";
    [ObservableProperty] private string _discountText = "0";
    [ObservableProperty] private decimal _lineTotal;
    public decimal Quantity => decimal.TryParse(QuantityText, out var value) ? value : 0;
    public decimal UnitCost => decimal.TryParse(UnitCostText, out var value) ? value : 0;
    public decimal Discount => decimal.TryParse(DiscountText, out var value) ? value : 0;
}

public sealed partial class PurchaseOrderEntryViewModel(
    IProductService productService, ISupplierService supplierService, IPurchaseOrderService service,
    IPurchaseGstCalculationService calculator, ManagementSession session) : ObservableObject
{
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<PurchaseOrderLineViewModel> Lines { get; } = [];
    public ObservableCollection<ProductPickerItemViewModel> ProductPickerItems { get; } = [];
    public ObservableCollection<SearchSuggestionItem> SupplierSuggestions { get; } = [];
    public int? CurrentUserId => session.CurrentUser?.Id;
    public int? EditingId { get; private set; }
    public bool IsEditing => EditingId is not null;
    public bool HasLines => Lines.Count > 0;

    // Two independent popups. A single shared "is open" flag was what let choosing a supplier and
    // opening the product picker interfere with one another.
    [ObservableProperty] private bool _isSupplierSuggestionsOpen;
    [ObservableProperty] private bool _isProductPickerOpen;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private DateTimeOffset? _orderDate = DateTimeOffset.Now;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _productSearchText = string.Empty;
    [ObservableProperty] private string _supplierSearchText = string.Empty;
    [ObservableProperty] private decimal _subTotal;
    [ObservableProperty] private decimal _discountTotal;
    [ObservableProperty] private decimal _taxTotal;
    [ObservableProperty] private decimal _roundOff;
    [ObservableProperty] private decimal _grandTotal;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    private int? _selectedProductSuggestionId;
    private readonly HashSet<int> _replenishmentProductIds = [];

    /// <summary>
    /// Ticked product ids, held outside <see cref="ProductPickerItems"/> on purpose: the item list is
    /// rebuilt on every keystroke, so selection has to survive re-searching (search "amul", tick two,
    /// search "tata", tick one, add all three).
    /// </summary>
    private readonly HashSet<int> _selectedProductIds = [];

    public int SelectedProductCount => _selectedProductIds.Count;
    public bool HasProductSelection => _selectedProductIds.Count > 0;
    public string AddSelectedLabel => $"Add Selected ({SelectedProductCount})";
    public PurchaseOrder? SavedOrder { get; private set; }

    public async Task InitializeAsync(int? id, PurchaseOrderPrefill? prefill = null)
    {
        Lines.Clear();
        ProductPickerItems.Clear();
        _selectedProductIds.Clear();
        IsProductPickerOpen = false;
        IsSupplierSuggestionsOpen = false;
        _replenishmentProductIds.Clear();
        Suppliers.Clear();
        foreach (var supplier in await supplierService.SearchAsync(new SupplierSearchQuery(), CurrentUserId)) Suppliers.Add(supplier);
        UpdateSupplierSuggestions(string.Empty);
        if (id is not { } orderId)
        {
            if (prefill is not null)
            {
                _replenishmentProductIds.UnionWith(prefill.PrioritizedProductIds ?? prefill.Lines.Select(x => x.ProductId));
                SelectedSupplier = prefill.SupplierId is { } supplierId
                    ? Suppliers.FirstOrDefault(s => s.Id == supplierId)
                    : null;
                SupplierSearchText = SelectedSupplier?.Name ?? string.Empty;
                foreach (var requested in prefill.Lines)
                {
                    var product = await productService.GetByIdAsync(requested.ProductId)
                        ?? throw new InvalidOperationException("A replenishment product no longer exists.");
                    Lines.Add(new PurchaseOrderLineViewModel
                    {
                        ProductId = product.Id, ProductName = product.Name, ProductCode = product.ProductCode,
                        Unit = product.Unit.ToString(), SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(),
                        GstRate = product.GstRatePercent ?? 0, PricingType = product.PricingType,
                        QuantityText = requested.Quantity.ToString("0.###"),
                        // Unknown remains explicit zero for user review; Product.PurchasePrice is
                        // not substituted because it is not authoritative completed purchase history.
                        UnitCostText = requested.EstimatedUnitCost?.ToString("0.##") ?? "0",
                    });
                }
                // A replenishment entry is already in the order. Keep the picker quiet until the
                // operator intentionally opens it to add another product.
                ProductSearchText = string.Empty;
                _selectedProductSuggestionId = null;
                OnPropertyChanged(nameof(HasLines));
                Recalculate();
            }
            return;
        }
        var order = await service.GetByIdAsync(orderId, CurrentUserId) ?? throw new InvalidOperationException("Purchase order not found.");
        if (order.Status != PurchaseOrderStatus.Draft) throw new InvalidOperationException("Only draft purchase orders can be edited.");
        EditingId = order.Id; SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == order.SupplierId);
        SupplierSearchText = SelectedSupplier?.Name ?? string.Empty;
        OrderDate = new DateTimeOffset(order.OrderDateUtc.ToLocalTime()); Notes = order.Notes ?? string.Empty;
        foreach (var item in order.Items) Lines.Add(new PurchaseOrderLineViewModel
        {
            ProductId = item.ProductId, ProductName = item.ProductNameSnapshot, ProductCode = item.ProductCodeSnapshot,
            Unit = item.UnitSnapshot, SupportsDecimalQuantity = item.UnitSnapshot is "Kilogram" or "Gram" or "Litre" or "Millilitre",
            GstRate = item.GstRatePercentSnapshot, PricingType = item.PricingTypeSnapshot,
            QuantityText = item.OrderedQuantity.ToString("0.###"), UnitCostText = item.UnitCost.ToString("0.##"),
            DiscountText = item.DiscountPercent.ToString("0.##"), LineTotal = item.LineTotal,
        });
        Recalculate();
    }

    public async Task AddProductAsync()
    {
        ErrorMessage = null;
        if (_selectedProductSuggestionId is { } selectedProductId)
        {
            await AddProductAsync(selectedProductId);
            return;
        }
        var product = (await productService.SearchAsync(new ProductSearchQuery { SearchText = ProductSearchText, MaxResults = 1 })).FirstOrDefault();
        if (product is null) { ErrorMessage = "No active product matched that name, SKU, code, or barcode."; return; }
        await AddProductAsync(product.Id);
    }

    public void SelectProductSuggestion(ProductPickerItemViewModel suggestion)
    {
        _selectedProductSuggestionId = suggestion.ProductId;
        ProductSearchText = suggestion.Title;
    }

    private async Task AddProductAsync(int productId)
    {
        ErrorMessage = null;
        var product = await productService.GetByIdAsync(productId);
        if (product is null || !product.IsActive)
        {
            ErrorMessage = "That product is no longer active or available.";
            return;
        }
        if (Lines.Any(l => l.ProductId == product.Id)) { ErrorMessage = "That product is already on this order."; return; }
        AddLine(product);
        _selectedProductSuggestionId = null;
        ProductSearchText = string.Empty; OnPropertyChanged(nameof(HasLines)); Recalculate();
    }

    /// <summary>The single place a PO line is built, so multi-add cannot drift from single-add defaults.</summary>
    private void AddLine(Product product) => Lines.Add(new PurchaseOrderLineViewModel
    {
        ProductId = product.Id, ProductName = product.Name, ProductCode = product.ProductCode, Unit = product.Unit.ToString(),
        SupportsDecimalQuantity = product.Unit.SupportsDecimalQuantity(), GstRate = product.GstRatePercent ?? 0,
        PricingType = product.PricingType, UnitCostText = product.PurchasePrice.ToString("0.##"),
    });

    public void ToggleProductSelection(ProductPickerItemViewModel item)
    {
        if (item.IsAlreadyAdded) return;
        item.IsSelected = !item.IsSelected;
    }

    public void SetProductSelected(ProductPickerItemViewModel item, bool isSelected)
    {
        if (item.IsAlreadyAdded) return;
        item.IsSelected = isSelected;
    }

    /// <summary>
    /// The one place the ticked set is maintained. Every route into selection — checkbox click, row
    /// click, Select All, Clear, keyboard — ends up here through the item's change notification, so
    /// the count can never drift from the checkboxes again.
    /// </summary>
    private void OnItemSelectionChanged(ProductPickerItemViewModel item)
    {
        if (item.IsSelected) _selectedProductIds.Add(item.ProductId);
        else _selectedProductIds.Remove(item.ProductId);
        NotifySelectionChanged();
    }

    /// <summary>Selects only what is currently visible and eligible — never an already-added product.</summary>
    public void SelectAllVisibleProducts()
    {
        foreach (var item in ProductPickerItems.Where(x => !x.IsAlreadyAdded)) item.IsSelected = true;
        NotifySelectionChanged();
    }

    public void ClearProductSelection()
    {
        foreach (var item in ProductPickerItems) item.IsSelected = false;
        _selectedProductIds.Clear();
        NotifySelectionChanged();
    }

    /// <summary>
    /// Adds every ticked product using the same defaults as single-product add, then resets the
    /// picker. Products already on the order are skipped rather than reported as errors — they can
    /// only be here if a line was added after the tick.
    /// </summary>
    public async Task<int> AddSelectedProductsAsync()
    {
        ErrorMessage = null;
        if (_selectedProductIds.Count == 0)
        {
            ErrorMessage = "Select at least one product.";
            return 0;
        }

        var added = 0;
        var unavailable = 0;
        foreach (var productId in _selectedProductIds.ToList())
        {
            if (Lines.Any(line => line.ProductId == productId)) continue;
            var product = await productService.GetByIdAsync(productId);
            if (product is null || !product.IsActive) { unavailable++; continue; }
            AddLine(product);
            added++;
        }

        _selectedProductIds.Clear();
        ProductSearchText = string.Empty;
        _selectedProductSuggestionId = null;
        ProductPickerItems.Clear();
        IsProductPickerOpen = false;
        NotifySelectionChanged();
        OnPropertyChanged(nameof(HasLines));
        Recalculate();

        if (unavailable > 0) ErrorMessage = $"{unavailable} selected product(s) are no longer active and were skipped.";
        return added;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedProductCount));
        OnPropertyChanged(nameof(HasProductSelection));
        OnPropertyChanged(nameof(AddSelectedLabel));
    }

    public void OpenProductPicker() => IsProductPickerOpen = true;

    /// <summary>Closing keeps the ticked set — reopening resumes the same selection (§13).</summary>
    public void CloseProductPicker() => IsProductPickerOpen = false;

    public async Task UpdateProductSuggestionsAsync(string? text)
    {
        var searchText = text ?? string.Empty;
        var products = await productService.SearchAsync(new ProductSearchQuery
        {
            SearchText = searchText,
            MaxResults = 2000,
        });
        if (!string.Equals(searchText, ProductSearchText, StringComparison.Ordinal)) return;
        ProductPickerItems.Clear();
        var activeProducts = products.Where(product => product.IsActive).ToList();
        var recommended = activeProducts.Where(product => _replenishmentProductIds.Contains(product.Id))
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var regular = activeProducts.Where(product => !_replenishmentProductIds.Contains(product.Id))
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase).ToList();
        // Every match is listed: this is a scrollable multi-select picker, not a typeahead dropdown,
        // so truncating to a handful would hide products the operator is trying to tick. The
        // ListView virtualizes, so a long catalogue costs nothing to render.
        var ordered = recommended.Concat(regular).ToList();
        var firstRecommendedId = recommended.FirstOrDefault()?.Id;
        var firstRegularId = regular.FirstOrDefault()?.Id;
        foreach (var product in ordered)
        {
            var isReplenishmentProduct = _replenishmentProductIds.Contains(product.Id);
            var isAlreadyAdded = Lines.Any(line => line.ProductId == product.Id);
            var detail = string.IsNullOrWhiteSpace(product.Sku)
                ? product.ProductCode
                : $"{product.ProductCode} · SKU {product.Sku}";
            var groupHeading = product.Id == firstRecommendedId ? "REPLENISHMENT RECOMMENDATIONS"
                : recommended.Count > 0 && product.Id == firstRegularId ? "ALL PRODUCTS" : null;
            var item = new ProductPickerItemViewModel
            {
                ProductId = product.Id,
                Title = product.Name,
                Detail = detail,
                IsAlreadyAdded = isAlreadyAdded,
                GroupHeading = groupHeading,
                ShowGroupDivider = recommended.Count > 0 && product.Id == firstRegularId,
                IsReplenishmentSuggestion = isReplenishmentProduct,
                // Re-tick anything chosen under a previous search term.
                IsSelected = !isAlreadyAdded && _selectedProductIds.Contains(product.Id),
            };
            item.SelectionChanged = OnItemSelectionChanged;
            ProductPickerItems.Add(item);
        }

        // A product selected earlier and since added by other means must not stay counted.
        foreach (var staleId in _selectedProductIds.Where(id => Lines.Any(line => line.ProductId == id)).ToList())
            _selectedProductIds.Remove(staleId);
        NotifySelectionChanged();
    }

    public void ClearSelectedProductForSearch() => _selectedProductSuggestionId = null;

    public void UpdateSupplierSuggestions(string? text)
    {
        SearchSuggestionCollection.Update(SupplierSuggestions,
            Suppliers.Where(s => s.IsActive).Select(s => new SearchSuggestionItem(s.Name, s.Name,
                string.IsNullOrWhiteSpace(s.Phone) ? "Supplier" : s.Phone, $"{s.Name} {s.SupplierCode} {s.Phone}", s.Id)), text);
    }

    /// <summary>
    /// The user is typing. Drop any committed supplier whose name no longer matches the text, refresh
    /// the list, and open only while there is something to search with — an empty box or a term with
    /// no matches leaves the popup shut (§2).
    /// </summary>
    public void ClearSelectedSupplierForSearch(string? text)
    {
        if (!string.Equals(text?.Trim(), SelectedSupplier?.Name, StringComparison.OrdinalIgnoreCase))
            SelectedSupplier = null;
        UpdateSupplierSuggestions(text);
        IsSupplierSuggestionsOpen = !string.IsNullOrWhiteSpace(text) && SupplierSuggestions.Count > 0;
    }

    /// <summary>
    /// Focus alone must not reopen the list once a supplier is committed, otherwise tabbing back
    /// through the form makes the popup reappear over the selected value.
    /// </summary>
    public void FocusSupplierSearch()
    {
        if (SelectedSupplier is not null) { IsSupplierSuggestionsOpen = false; return; }
        UpdateSupplierSuggestions(SupplierSearchText);
        IsSupplierSuggestionsOpen = !string.IsNullOrWhiteSpace(SupplierSearchText) && SupplierSuggestions.Count > 0;
    }

    /// <summary>
    /// Commits the supplier and closes the popup. The suggestion list is deliberately NOT refreshed
    /// here: repopulating the bound collection is what made the control re-open itself over the
    /// committed value, which was the original bug.
    /// </summary>
    public void SelectSupplierSuggestion(SearchSuggestionItem suggestion)
    {
        SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == suggestion.EntityId);
        SupplierSearchText = SelectedSupplier?.Name ?? string.Empty;
        IsSupplierSuggestionsOpen = false;
    }

    public void CloseSupplierSuggestions() => IsSupplierSuggestionsOpen = false;

    public void ClearSupplier()
    {
        SelectedSupplier = null;
        SupplierSearchText = string.Empty;
        UpdateSupplierSuggestions(string.Empty);
        IsSupplierSuggestionsOpen = false;
    }

    public void Remove(PurchaseOrderLineViewModel line) { Lines.Remove(line); OnPropertyChanged(nameof(HasLines)); Recalculate(); }
    public void Recalculate()
    {
        if (Lines.Count == 0) { SubTotal = DiscountTotal = TaxTotal = RoundOff = GrandTotal = 0; return; }
        if (Lines.Any(l => l.Quantity <= 0 || l.UnitCost < 0 || l.Discount is < 0 or > 100)) return;
        var totals = calculator.Calculate(Lines.Select(l => new PurchaseLine { ProductId = l.ProductId, Quantity = l.Quantity,
            UnitPrice = l.UnitCost, DiscountPercent = l.Discount, GstRatePercent = l.GstRate, PricingType = l.PricingType }).ToList());
        foreach (var result in totals.Lines) Lines.First(l => l.ProductId == result.Line.ProductId).LineTotal = result.LineTotal;
        SubTotal = totals.SubTotal; DiscountTotal = totals.DiscountTotal; TaxTotal = totals.TaxTotal; RoundOff = totals.RoundOffAmount; GrandTotal = totals.GrandTotal;
    }

    public async Task<bool> SaveAsync(bool submit)
    {
        ErrorMessage = null;
        if (SelectedSupplier is null) { ErrorMessage = "Select a supplier."; return false; }
        if (Lines.Count == 0) { ErrorMessage = "Add at least one product."; return false; }
        IsBusy = true;
        try
        {
            var request = new SavePurchaseOrderDraftRequest { SupplierId = SelectedSupplier.Id, OrderDateUtc = OrderDate?.UtcDateTime,
                Notes = Notes, PerformedByUserId = CurrentUserId, Lines = Lines.Select(l => new PurchaseOrderLineInput
                { ProductId = l.ProductId, OrderedQuantity = l.Quantity, UnitCost = l.UnitCost, DiscountPercent = l.Discount, PricingType = l.PricingType }).ToList() };
            SavedOrder = EditingId is { } id ? await service.UpdateDraftAsync(id, request) : await service.CreateDraftAsync(request);
            EditingId = SavedOrder.Id;
            if (submit) SavedOrder = await service.SubmitAsync(SavedOrder.Id, CurrentUserId);
            return true;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; return false; }
        finally { IsBusy = false; }
    }
}
