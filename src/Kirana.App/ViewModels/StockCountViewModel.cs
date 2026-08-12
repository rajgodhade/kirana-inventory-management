using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Application.StockCounts;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the Stock Counting page: the history list, the active count, variance review and the
/// completion summary. The page moves between those as view states rather than separate pages, so a
/// counter never loses their place mid-count.
/// </summary>
public sealed partial class StockCountViewModel(
    IStockCountService stockCounts,
    IProductService products,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<StockCountRowViewModel> History { get; } = [];
    public ObservableCollection<StockCountItemRowViewModel> Items { get; } = [];
    public ObservableCollection<StockCountVarianceRowViewModel> VarianceLines { get; } = [];

    /// <summary>Deliberately its own minimal row type rather than the products page's
    /// ProductRowViewModel: this picker needs three fields, and that type carries pricing/stock/
    /// expiry state plus `required` members that only complicate the binding here.</summary>
    public ObservableCollection<StockCountSearchRowViewModel> SearchResults { get; } = [];

    public IScannerInputBuffer ScannerBuffer { get; } = new ScannerInputBuffer();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveCount))]
    [NotifyPropertyChangedFor(nameof(ActiveCountNumber))]
    [NotifyPropertyChangedFor(nameof(ActiveStartedText))]
    private StockCount? _activeCount;

    [ObservableProperty] private string _searchText = "";

    /// <summary>Which of the page's states is showing: List, Counting, Review or Completed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(IsCountingView))]
    [NotifyPropertyChangedFor(nameof(IsReviewView))]
    [NotifyPropertyChangedFor(nameof(IsCompletedView))]
    private string _viewState = "List";

    // ---- Review / completion summary ----
    [ObservableProperty] private string _reviewSummaryText = "";
    [ObservableProperty] private string _reviewIncreaseText = "";
    [ObservableProperty] private string _reviewDecreaseText = "";
    [ObservableProperty] private string _reviewAffectedText = "";
    [ObservableProperty] private string _rebaseWarning = "";
    [ObservableProperty] private string _completionSummary = "";
    [ObservableProperty] private string _completionCountNumber = "";

    public bool IsListView => ViewState == "List";
    public bool IsCountingView => ViewState == "Counting";
    public bool IsReviewView => ViewState == "Review";
    public bool IsCompletedView => ViewState == "Completed";

    public bool HasActiveCount => ActiveCount is not null;
    public string ActiveCountNumber => ActiveCount?.CountNumber ?? "";
    public string ActiveStartedText => ActiveCount is null
        ? ""
        : ActiveCount.StartedAtUtc.ToLocalTime().ToString("dd MMM yyyy, h:mm tt");

    public string ProgressText => $"{Items.Count(i => i.IsCounted)} / {Items.Count} products counted";
    public bool CanManage => session.HasPermission(PermissionKeys.InventoryManage);

    private int? UserId => session.CurrentUser?.Id;

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            ActiveCount = await stockCounts.GetActiveAsync();

            History.Clear();
            foreach (var summary in await stockCounts.GetSummariesAsync())
            {
                History.Add(new StockCountRowViewModel(summary));
            }

            if (ActiveCount is not null)
            {
                LoadItems(ActiveCount);
            }

            ViewState = "List";
        });
    }

    private void LoadItems(StockCount count)
    {
        Items.Clear();
        foreach (var item in count.Items)
        {
            Items.Add(new StockCountItemRowViewModel(item));
        }

        OnPropertyChanged(nameof(ProgressText));
    }

    [RelayCommand]
    private async Task StartCountAsync()
    {
        await RunAsync(async () =>
        {
            var count = await stockCounts.StartAsync(null, UserId);
            ActiveCount = await stockCounts.GetByIdAsync(count.Id);
            LoadItems(ActiveCount!);
            ViewState = "Counting";
            StatusMessage = $"{count.CountNumber} started. Scan or search products to count them.";
        });
    }

    [RelayCommand]
    private async Task ContinueCountAsync()
    {
        await RunAsync(async () =>
        {
            ActiveCount = await stockCounts.GetActiveAsync();
            if (ActiveCount is null)
            {
                ErrorMessage = "That count is no longer open.";
                return;
            }

            LoadItems(ActiveCount);
            ViewState = "Counting";
        });
    }

    [RelayCommand]
    private void BackToList() => ViewState = "List";

    [RelayCommand]
    private void BackToCounting() => ViewState = "Counting";

    // ---- Adding products ----

    public async Task SearchProductsAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchResults.Clear();
            return;
        }

        var matches = await products.SearchAsync(new ProductSearchQuery { SearchText = SearchText, MaxResults = 12 });
        SearchResults.Clear();
        foreach (var product in matches)
        {
            SearchResults.Add(new StockCountSearchRowViewModel(product));
        }
    }

    public async Task AddProductAsync(int productId)
    {
        if (ActiveCount is null) return;

        await RunAsync(async () =>
        {
            var item = await stockCounts.AddItemAsync(ActiveCount.Id, productId, null, UserId);
            AddOrFocusItem(item);
            SearchText = "";
            SearchResults.Clear();
        });
    }

    /// <summary>Handles a scanned barcode. Any active code of a product resolves to that product's
    /// single item; a second scan of an already-listed product is a no-op rather than a duplicate.</summary>
    public async Task ScanAsync(string barcode)
    {
        if (ActiveCount is null) return;

        await RunAsync(async () =>
        {
            var item = await stockCounts.AddItemByBarcodeAsync(ActiveCount.Id, barcode, UserId);
            var row = AddOrFocusItem(item);
            StatusMessage = row.IsCounted
                ? $"{row.ProductName} is already counted at {row.PhysicalText} {row.UnitText}."
                : $"{row.ProductName} added — enter the physical quantity.";
        });
    }

    private StockCountItemRowViewModel AddOrFocusItem(StockCountItem item)
    {
        var existing = Items.FirstOrDefault(i => i.Id == item.Id);
        if (existing is not null)
        {
            return existing;
        }

        var row = new StockCountItemRowViewModel(item);
        Items.Insert(0, row);
        OnPropertyChanged(nameof(ProgressText));
        return row;
    }

    /// <summary>Commits a typed physical quantity. Parsing failures are reported rather than
    /// silently ignored — a count that quietly drops an entry is worse than one that complains.</summary>
    public async Task SetQuantityAsync(StockCountItemRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.QuantityInput))
        {
            return;
        }

        if (!decimal.TryParse(row.QuantityInput.Trim(), out var quantity))
        {
            ErrorMessage = $"'{row.QuantityInput}' is not a valid quantity.";
            row.QuantityInput = row.CountedQuantity?.ToString("0.###") ?? "";
            return;
        }

        await RunAsync(async () =>
        {
            var updated = await stockCounts.SetCountedQuantityAsync(row.Id, quantity, null, UserId);
            row.CountedQuantity = updated.CountedQuantity;
            row.QuantityInput = updated.CountedQuantity?.ToString("0.###") ?? "";
            OnPropertyChanged(nameof(ProgressText));
        },
        onError: () =>
        {
            // Put the field back to the last accepted value so the grid never shows a number the
            // service refused.
            row.QuantityInput = row.CountedQuantity?.ToString("0.###") ?? "";
        });
    }

    public async Task RemoveItemAsync(StockCountItemRowViewModel row)
    {
        await RunAsync(async () =>
        {
            await stockCounts.RemoveItemAsync(row.Id, UserId);
            Items.Remove(row);
            OnPropertyChanged(nameof(ProgressText));
        });
    }

    // ---- Review and finalize ----

    [RelayCommand]
    private async Task ReviewVariancesAsync()
    {
        if (ActiveCount is null) return;

        await RunAsync(async () =>
        {
            var preview = await stockCounts.GetVariancePreviewAsync(ActiveCount.Id);

            VarianceLines.Clear();
            // Differences first — that is what the operator is here to check.
            foreach (var line in preview.Lines
                .Where(l => l.CountedQuantity is not null)
                .OrderBy(l => l.ObservedVariance == 0m)
                .ThenBy(l => l.ProductName))
            {
                VarianceLines.Add(new StockCountVarianceRowViewModel(line));
            }

            ReviewSummaryText = preview.AdjustmentCount == 0
                ? "No differences found — every counted product matches the system."
                : $"{preview.AdjustmentCount} product(s) have differences";
            ReviewIncreaseText = $"+{preview.TotalIncreaseQuantity:0.###}";
            ReviewDecreaseText = $"-{preview.TotalDecreaseQuantity:0.###}";
            ReviewAffectedText = preview.AdjustmentCount.ToString();
            RebaseWarning = preview.HasRebases
                ? $"{preview.RebasedLines.Count} product(s) had stock movement during this count. " +
                  "Their adjustments will be recalculated against current stock so the result matches what you counted."
                : "";

            if (preview.UncountedItems > 0)
            {
                StatusMessage = $"{preview.UncountedItems} product(s) were added but never counted — they will be left unchanged.";
            }

            ViewState = "Review";
        });
    }

    [RelayCommand]
    private async Task FinalizeAsync()
    {
        if (ActiveCount is null) return;

        await RunAsync(async () =>
        {
            var result = await stockCounts.FinalizeAsync(ActiveCount.Id, UserId);

            CompletionCountNumber = result.CountNumber;
            CompletionSummary =
                $"Products counted: {result.ProductsCounted}\n" +
                $"Increased: {result.IncreasedCount}\n" +
                $"Decreased: {result.DecreasedCount}\n" +
                $"Unchanged: {result.UnchangedCount}\n" +
                $"Inventory adjustments: {result.AdjustmentCount}" +
                (result.RebasedCount > 0 ? $"\nRecalculated against current stock: {result.RebasedCount}" : "");

            ActiveCount = null;
            Items.Clear();
            VarianceLines.Clear();
            ViewState = "Completed";

            History.Clear();
            foreach (var summary in await stockCounts.GetSummariesAsync())
            {
                History.Add(new StockCountRowViewModel(summary));
            }
        });
    }

    [RelayCommand]
    private async Task CancelCountAsync()
    {
        if (ActiveCount is null) return;

        await RunAsync(async () =>
        {
            await stockCounts.CancelAsync(ActiveCount.Id, null, UserId);
            StatusMessage = $"{ActiveCount.CountNumber} was cancelled. No stock was changed.";
            ActiveCount = null;
            Items.Clear();
            await LoadAsync();
        });
    }

    /// <summary>Single place where busy state, error surfacing and the status reset live, so no
    /// command can leave the page spinning after a failure.</summary>
    private async Task RunAsync(Func<Task> action, Action? onError = null)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            onError?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
