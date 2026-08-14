using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed partial class ReplenishmentRowViewModel : ObservableObject
{
    public required ReplenishmentRecommendation Recommendation { get; init; }
    [ObservableProperty] private bool _isSelected;
    public int ProductId => Recommendation.ProductId;
    public string ProductName => Recommendation.ProductName;
    public string ProductCode => Recommendation.ProductCode;
    public string Unit => Recommendation.Unit.ToDisplayText();
    public string Supplier => Recommendation.PreferredSupplierName ?? "Not configured";
    public string Status => Recommendation.Status switch
    {
        ReplenishmentStatus.AtReorderLevel => "At reorder level",
        ReplenishmentStatus.BelowReorderLevel => "Below reorder level",
        ReplenishmentStatus.OutOfStock => "Out of stock",
        ReplenishmentStatus.NotConfigured => "Not configured",
        ReplenishmentStatus.InvalidConfiguration => "Invalid configuration",
        _ => "Healthy",
    };
    public string EstimatedCost => Recommendation.EstimatedUnitCost is { } cost ? $"₹{cost:N2}" : "Cost unavailable";
    public string EstimatedValue => Recommendation.EstimatedOrderValue is { } value ? $"₹{value:N2}" : "—";
}

public sealed partial class ReplenishmentViewModel(
    IReplenishmentService service, ISupplierService supplierService, ManagementSession session) : ObservableObject
{
    public ObservableCollection<ReplenishmentRowViewModel> Rows { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<SearchSuggestionItem> SearchSuggestions { get; } = [];
    private readonly List<SearchSuggestionItem> _searchCatalog = [];
    public IReadOnlyList<string> StatusOptions { get; } =
        ["Needs reorder", "Out of stock", "At reorder level", "Below reorder level", "Healthy", "Not configured", "All"];
    public IReadOnlyList<string> ConfigurationOptions { get; } = ["Configured only", "All products", "Not configured"];
    public int? CurrentUserId => session.CurrentUser?.Id;
    public bool HasRows => Rows.Count > 0;
    public bool HasSelection => Rows.Any(r => r.IsSelected);

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _selectedStatus = "Needs reorder";
    [ObservableProperty] private string _selectedConfiguration = "Configured only";
    [ObservableProperty] private int _productsNeedingReorder;
    [ObservableProperty] private decimal _totalSuggestedUnits;
    [ObservableProperty] private decimal _estimatedOrderValue;
    [ObservableProperty] private int _unconfiguredLowStockProducts;
    [ObservableProperty] private string _calculatedAtText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    public async Task InitializeAsync()
    {
        Suppliers.Clear();
        foreach (var supplier in await supplierService.SearchAsync(new SupplierSearchQuery(), CurrentUserId))
            if (supplier.IsActive) Suppliers.Add(supplier);
        await LoadSearchCatalogAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var (status, needsOnly) = SelectedStatus switch
            {
                "Out of stock" => (ReplenishmentStatus.OutOfStock as ReplenishmentStatus?, false),
                "At reorder level" => (ReplenishmentStatus.AtReorderLevel as ReplenishmentStatus?, false),
                "Below reorder level" => (ReplenishmentStatus.BelowReorderLevel as ReplenishmentStatus?, false),
                "Healthy" => (ReplenishmentStatus.Healthy as ReplenishmentStatus?, false),
                "Not configured" => (ReplenishmentStatus.NotConfigured as ReplenishmentStatus?, false),
                "All" => (null, false),
                _ => (null, true),
            };
            bool? enabled = SelectedConfiguration switch
            {
                "Configured only" => true,
                "Not configured" => false,
                _ => null,
            };
            var summary = await service.GetRecommendationsAsync(new ReplenishmentQuery
            {
                SearchText = SearchText, SupplierId = SelectedSupplier?.Id, Status = status,
                Enabled = enabled, NeedsReorderOnly = needsOnly,
            }, CurrentUserId);

            Rows.Clear();
            foreach (var item in summary.Items) Rows.Add(new ReplenishmentRowViewModel { Recommendation = item });
            UpdateSearchSuggestions(SearchText);
            ProductsNeedingReorder = summary.ProductsNeedingReorder;
            TotalSuggestedUnits = summary.TotalSuggestedUnits;
            EstimatedOrderValue = summary.EstimatedOrderValue;
            UnconfiguredLowStockProducts = summary.UnconfiguredLowStockProducts;
            CalculatedAtText = $"Calculated {summary.CalculatedAtUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}";
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasSelection));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void SelectionChanged() => OnPropertyChanged(nameof(HasSelection));

    public void UpdateSearchSuggestions(string? text) =>
        SearchSuggestionCollection.Update(SearchSuggestions, _searchCatalog, text);

    private async Task LoadSearchCatalogAsync()
    {
        var allProducts = await service.GetRecommendationsAsync(
            new ReplenishmentQuery { NeedsReorderOnly = false, Enabled = null }, CurrentUserId);
        _searchCatalog.Clear();
        foreach (var item in allProducts.Items)
        {
            _searchCatalog.Add(new SearchSuggestionItem(item.ProductCode, item.ProductCode,
                $"Product · {item.ProductName}", $"{item.ProductCode} {item.ProductName}"));
            _searchCatalog.Add(new SearchSuggestionItem(item.ProductName, item.ProductName,
                $"Product · {item.ProductCode}", $"{item.ProductName} {item.ProductCode}"));
        }
    }

    public async Task<PurchaseOrderPrefill?> BuildPrefillAsync(
        IReadOnlyCollection<int> productIds,
        IReadOnlyCollection<int>? prioritizedProductIds = null)
    {
        if (productIds.Count == 0) return null;
        // Re-read authoritative stock and commitments immediately before navigation. The PO page
        // remains the review/confirmation boundary and the calculation itself writes nothing.
        var fresh = await service.GetRecommendationsAsync(
            new ReplenishmentQuery { NeedsReorderOnly = true, Enabled = true }, CurrentUserId);
        var chosen = fresh.Items.Where(i => productIds.Contains(i.ProductId) && i.SuggestedQuantity > 0).ToList();
        if (chosen.Count == 0)
        {
            ErrorMessage = "Stock or open orders changed. Refresh and review the recommendations again.";
            return null;
        }

        int? supplierId = chosen.All(i => i.PreferredSupplierId is not null)
            && chosen.Select(i => i.PreferredSupplierId).Distinct().Count() == 1
                ? chosen[0].PreferredSupplierId
                : null;
        return new PurchaseOrderPrefill(supplierId,
            chosen.Select(i => new PurchaseOrderPrefillLine(i.ProductId, i.SuggestedQuantity, i.EstimatedUnitCost)).ToList(),
            prioritizedProductIds ?? chosen.Select(i => i.ProductId).ToArray());
    }

    public void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedSupplier = null;
        SelectedStatus = "Needs reorder";
        SelectedConfiguration = "Configured only";
    }
}
