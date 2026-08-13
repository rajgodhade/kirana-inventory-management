using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Inventories;
using Kirana.Application.Products;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>One row of the adjustment history list.</summary>
public sealed class InventoryAdjustmentRowViewModel(InventoryAdjustment adjustment)
{
    public string AdjustmentNumber { get; } = adjustment.AdjustmentNumber;
    public string ProductName { get; } = adjustment.ProductNameSnapshot;
    public string ProductCode { get; } = adjustment.ProductCodeSnapshot;
    public string DateText { get; } = adjustment.AdjustedAtUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt");
    public string ReasonText { get; } = adjustment.Reason.ToDisplayText();
    public string UserText { get; } = adjustment.AdjustedByUser?.FullName ?? "—";
    public string UnitText { get; } = adjustment.UnitSnapshot.ToDisplayText();

    /// <summary>Always carries an explicit sign so direction survives without colour.</summary>
    public string QuantityText { get; } =
        $"{adjustment.Direction.ToSignPrefix()}{adjustment.AdjustmentQuantity:0.###}";

    public string TransitionText { get; } =
        $"{adjustment.PreviousQuantity:0.###} → {adjustment.NewQuantity:0.###}";

    public string DirectionState { get; } =
        adjustment.Direction == InventoryAdjustmentDirection.Increase ? "Increase" : "Decrease";

    public string NotesText { get; } = adjustment.Notes ?? "";
    public bool HasNotes { get; } = !string.IsNullOrWhiteSpace(adjustment.Notes);
}

/// <summary>A product-search hit while choosing what to adjust.</summary>
public sealed class AdjustmentProductRowViewModel(Product product)
{
    public int Id { get; } = product.Id;
    public string Name { get; } = product.Name;

    public string DetailText { get; } = string.IsNullOrWhiteSpace(product.Sku)
        ? product.ProductCode
        : $"{product.ProductCode} · {product.Sku}";

    public string StockText { get; } =
        $"{product.Inventory?.QuantityOnHand ?? 0m:0.###} {product.Unit.ToDisplayText()}";
}

/// <summary>
/// Backs the Inventory Adjustments page: history with filters, and the create → review → confirm
/// workflow. Moves between those as view states rather than separate pages.
///
/// <para>Nothing here mutates inventory. The only write is
/// <see cref="IInventoryAdjustmentService.CreateAsync"/>, reached solely from the explicit confirm
/// step — editing fields, previewing and cancelling all leave stock untouched.</para>
/// </summary>
public sealed partial class InventoryAdjustmentViewModel(
    IInventoryAdjustmentService adjustments,
    IProductService products,
    IBarcodeLookupService barcodeLookup,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<InventoryAdjustmentRowViewModel> History { get; } = [];
    public ObservableCollection<AdjustmentProductRowViewModel> SearchResults { get; } = [];

    public IScannerInputBuffer ScannerBuffer { get; } = new ScannerInputBuffer();

    public IReadOnlyList<string> DirectionOptions { get; } = ["Decrease", "Increase"];

    public IReadOnlyList<string> ReasonOptions { get; } =
        Enum.GetValues<InventoryAdjustmentReason>().Select(r => r.ToDisplayText()).ToList();

    /// <summary>"All reasons" plus each reason, for the history filter.</summary>
    public IReadOnlyList<string> ReasonFilterOptions { get; } =
        new[] { AllReasons }.Concat(Enum.GetValues<InventoryAdjustmentReason>().Select(r => r.ToDisplayText())).ToList();

    public IReadOnlyList<string> DirectionFilterOptions { get; } = [AllDirections, "Increase", "Decrease"];

    private const string AllReasons = "All reasons";
    private const string AllDirections = "All directions";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHistoryView))]
    [NotifyPropertyChangedFor(nameof(IsCreateView))]
    [NotifyPropertyChangedFor(nameof(IsReviewView))]
    [NotifyPropertyChangedFor(nameof(IsCompletedView))]
    private string _viewState = "History";

    // ---- History filters ----
    [ObservableProperty] private string _historySearchText = "";
    [ObservableProperty] private string _selectedReasonFilter = AllReasons;
    [ObservableProperty] private string _selectedDirectionFilter = AllDirections;
    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private string _historyCountText = "";

    // ---- Create form ----
    [ObservableProperty] private string _productSearchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProduct))]
    [NotifyPropertyChangedFor(nameof(SelectedProductName))]
    [NotifyPropertyChangedFor(nameof(SelectedProductDetail))]
    private Product? _selectedProduct;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStockText))]
    [NotifyPropertyChangedFor(nameof(NewQuantityText))]
    [NotifyPropertyChangedFor(nameof(PreviewTransitionText))]
    private decimal _currentStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewQuantityText))]
    [NotifyPropertyChangedFor(nameof(PreviewTransitionText))]
    [NotifyPropertyChangedFor(nameof(SignedQuantityText))]
    [NotifyPropertyChangedFor(nameof(WouldGoNegative))]
    private string _quantityInput = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewQuantityText))]
    [NotifyPropertyChangedFor(nameof(PreviewTransitionText))]
    [NotifyPropertyChangedFor(nameof(SignedQuantityText))]
    [NotifyPropertyChangedFor(nameof(WouldGoNegative))]
    [NotifyPropertyChangedFor(nameof(DirectionState))]
    private string _selectedDirection = "Decrease";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotesAreRequired))]
    private string _selectedReason = InventoryAdjustmentReason.Damaged.ToDisplayText();

    [ObservableProperty] private string _notes = "";

    // ---- Completion ----
    [ObservableProperty] private string _completionNumber = "";
    [ObservableProperty] private string _completionSummary = "";

    public bool IsHistoryView => ViewState == "History";
    public bool IsCreateView => ViewState == "Create";
    public bool IsReviewView => ViewState == "Review";
    public bool IsCompletedView => ViewState == "Completed";

    public bool CanManage => session.HasPermission(PermissionKeys.InventoryManage);
    private int? UserId => session.CurrentUser?.Id;

    public bool HasSelectedProduct => SelectedProduct is not null;
    public string SelectedProductName => SelectedProduct?.Name ?? "";
    public string SelectedProductDetail => SelectedProduct is null
        ? ""
        : string.IsNullOrWhiteSpace(SelectedProduct.Sku)
            ? SelectedProduct.ProductCode
            : $"{SelectedProduct.ProductCode} · {SelectedProduct.Sku}";

    public string UnitText => SelectedProduct?.Unit.ToDisplayText() ?? "";
    public string CurrentStockText => $"{CurrentStock:0.###} {UnitText}".Trim();

    private InventoryAdjustmentDirection Direction =>
        SelectedDirection == "Increase"
            ? InventoryAdjustmentDirection.Increase
            : InventoryAdjustmentDirection.Decrease;

    public string DirectionState => SelectedDirection;

    private InventoryAdjustmentReason Reason =>
        Enum.GetValues<InventoryAdjustmentReason>()
            .FirstOrDefault(r => r.ToDisplayText() == SelectedReason);

    public bool NotesAreRequired => Reason.RequiresNotes();

    private decimal? ParsedQuantity =>
        decimal.TryParse(QuantityInput.Trim(), out var value) && value > 0m ? value : null;

    /// <summary>Live preview only — the service recomputes everything against fresh stock inside its
    /// own transaction, so a stale preview can never influence the result.</summary>
    public decimal? PreviewNewQuantity => ParsedQuantity is { } quantity
        ? CurrentStock + Direction.ToSignedQuantity(quantity)
        : null;

    public string SignedQuantityText => ParsedQuantity is { } quantity
        ? $"{Direction.ToSignPrefix()}{quantity:0.###} {UnitText}".Trim()
        : "—";

    public string NewQuantityText => PreviewNewQuantity is { } newQuantity
        ? $"{newQuantity:0.###} {UnitText}".Trim()
        : "—";

    public string PreviewTransitionText => PreviewNewQuantity is { } newQuantity
        ? $"{CurrentStock:0.###} → {newQuantity:0.###}"
        : $"{CurrentStock:0.###} → —";

    public bool WouldGoNegative => PreviewNewQuantity is { } newQuantity && newQuantity < 0m;

    // ---- History ----

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var query = new InventoryAdjustmentQuery
            {
                SearchText = string.IsNullOrWhiteSpace(HistorySearchText) ? null : HistorySearchText,
                Reason = SelectedReasonFilter == AllReasons
                    ? null
                    : Enum.GetValues<InventoryAdjustmentReason>().First(r => r.ToDisplayText() == SelectedReasonFilter),
                Direction = SelectedDirectionFilter switch
                {
                    "Increase" => InventoryAdjustmentDirection.Increase,
                    "Decrease" => InventoryAdjustmentDirection.Decrease,
                    _ => null,
                },
                FromUtc = FromDate?.UtcDateTime.Date,
                ToUtc = ToDate?.UtcDateTime.Date.AddDays(1),
            };

            var results = await adjustments.SearchAsync(query);
            History.Clear();
            foreach (var adjustment in results)
            {
                History.Add(new InventoryAdjustmentRowViewModel(adjustment));
            }

            HistoryCountText = History.Count == 1 ? "1 adjustment" : $"{History.Count} adjustments";
        });
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        HistorySearchText = "";
        SelectedReasonFilter = AllReasons;
        SelectedDirectionFilter = AllDirections;
        FromDate = null;
        ToDate = null;
        await LoadAsync();
    }

    // ---- Create workflow ----

    [RelayCommand]
    private void StartNew()
    {
        SelectedProduct = null;
        ProductSearchText = "";
        SearchResults.Clear();
        QuantityInput = "";
        Notes = "";
        SelectedDirection = "Decrease";
        SelectedReason = InventoryAdjustmentReason.Damaged.ToDisplayText();
        CurrentStock = 0m;
        ErrorMessage = null;
        StatusMessage = null;
        ViewState = "Create";
    }

    [RelayCommand]
    private async Task BackToHistoryAsync()
    {
        ViewState = "History";
        await LoadAsync();
    }

    [RelayCommand]
    private void BackToCreate() => ViewState = "Create";

    public async Task SearchProductsAsync()
    {
        if (string.IsNullOrWhiteSpace(ProductSearchText))
        {
            SearchResults.Clear();
            return;
        }

        var matches = await products.SearchAsync(new ProductSearchQuery
        {
            SearchText = ProductSearchText,
            MaxResults = 12,
        });

        SearchResults.Clear();
        foreach (var product in matches)
        {
            SearchResults.Add(new AdjustmentProductRowViewModel(product));
        }
    }

    /// <summary>Resolves a scan through the shared Phase 13B lookup, so any active barcode of a
    /// product selects that product. A scan identifies the product only — it never implies a
    /// quantity.</summary>
    public async Task ScanAsync(string barcode)
    {
        await RunAsync(async () =>
        {
            var product = await barcodeLookup.LookupAsync(barcode)
                ?? throw new InvalidOperationException($"No active product found for barcode '{barcode.Trim()}'.");

            await SelectProductAsync(product.Id);
            StatusMessage = $"{product.Name} selected.";
        });
    }

    /// <summary>Deselects the product so the search box returns, keeping the rest of the form.</summary>
    public void ClearSelectedProduct()
    {
        SelectedProduct = null;
        CurrentStock = 0m;
        ProductSearchText = "";
        SearchResults.Clear();
        OnPropertyChanged(nameof(UnitText));
        OnPropertyChanged(nameof(CurrentStockText));
    }

    public async Task SelectProductAsync(int productId)
    {
        await RunAsync(async () =>
        {
            SelectedProduct = await products.GetByIdAsync(productId)
                ?? throw new InvalidOperationException("Product not found.");

            // Read through the service so the figure comes from the same untracked path the write
            // will use, rather than from whatever the search result happened to carry.
            CurrentStock = await adjustments.GetCurrentStockAsync(productId);

            ProductSearchText = "";
            SearchResults.Clear();
            OnPropertyChanged(nameof(UnitText));
            OnPropertyChanged(nameof(CurrentStockText));
            OnPropertyChanged(nameof(NewQuantityText));
            OnPropertyChanged(nameof(SignedQuantityText));
            OnPropertyChanged(nameof(PreviewTransitionText));
        });
    }

    /// <summary>Validates the form and moves to review. Writes nothing — the confirm step is the
    /// only thing that touches inventory.</summary>
    [RelayCommand]
    private async Task ReviewAsync()
    {
        ErrorMessage = null;

        if (SelectedProduct is null)
        {
            ErrorMessage = "Choose a product to adjust.";
            return;
        }

        if (ParsedQuantity is null)
        {
            ErrorMessage = "Enter a quantity greater than zero.";
            return;
        }

        if (NotesAreRequired && string.IsNullOrWhiteSpace(Notes))
        {
            ErrorMessage = $"Notes are required when the reason is '{SelectedReason}'.";
            return;
        }

        await RunAsync(async () =>
        {
            // Re-read current stock: the form may have been open a while, and the review screen
            // should show what the adjustment will actually apply against.
            CurrentStock = await adjustments.GetCurrentStockAsync(SelectedProduct.Id);

            if (WouldGoNegative)
            {
                ErrorMessage =
                    $"Cannot decrease below zero: only {CurrentStock:0.###} {UnitText} in stock.";
                return;
            }

            ViewState = "Review";
        });
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (SelectedProduct is null || ParsedQuantity is not { } quantity)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var adjustment = await adjustments.CreateAsync(new CreateInventoryAdjustmentRequest
            {
                ProductId = SelectedProduct.Id,
                Direction = Direction,
                Quantity = quantity,
                Reason = Reason,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
                PerformedByUserId = UserId,
            });

            CompletionNumber = adjustment.AdjustmentNumber;
            CompletionSummary =
                $"Product: {adjustment.ProductNameSnapshot}\n" +
                $"Adjustment: {adjustment.Direction.ToSignPrefix()}{adjustment.AdjustmentQuantity:0.###} " +
                $"{adjustment.UnitSnapshot.ToDisplayText()}\n" +
                $"Reason: {adjustment.Reason.ToDisplayText()}\n" +
                $"Stock: {adjustment.PreviousQuantity:0.###} → {adjustment.NewQuantity:0.###}" +
                (string.IsNullOrWhiteSpace(adjustment.Notes) ? "" : $"\nNotes: {adjustment.Notes}");

            ViewState = "Completed";
            await LoadAsync();
        });
    }

    /// <summary>Single place for busy state and error surfacing, so no command can leave the page
    /// spinning after a failure.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
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
}
