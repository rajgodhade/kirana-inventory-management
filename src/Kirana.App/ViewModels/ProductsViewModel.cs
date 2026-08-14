using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Products management page (PRD §12-13, §24, §26).</summary>
public enum ProductListNavigationFilter
{
    All,
    LowStock,
    OutOfStock,
}

public sealed partial class ProductsViewModel(
    IProductService productService,
    ICategoryService categoryService,
    IBrandService brandService,
    ISupplierService supplierService,
    IInventoryService inventoryService,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Narrows the list to discontinued products only, matching how the sibling
    /// "Out of stock" and "Expired" checkboxes behave. Named <c>...Only</c> for the same reason —
    /// the previous <c>ShowInactive</c> read as "include these as well", which is not what it does.</summary>
    [ObservableProperty]
    private bool _inactiveOnly;

    [ObservableProperty]
    private bool _outOfStockOnly;

    [ObservableProperty]
    private bool _lowStockOnly;

    [ObservableProperty]
    private bool _expiredOnly;

    [ObservableProperty]
    private string _selectedSortOption = "Name (A-Z)";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _totalProductsText = "0 products";

    public bool CanEditProducts => session.HasPermission(PermissionKeys.ProductsEdit);
    public bool CanViewPurchasePrice => session.HasPermission(PermissionKeys.PricingViewPurchasePrice);
    public bool CanManageInventory => session.HasPermission(PermissionKeys.InventoryManage);
    public bool CanConfigureReplenishment => session.HasPermission(PermissionKeys.PurchasesManage);

    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<ProductRowViewModel> Products { get; } = [];
    public IReadOnlyList<string> SortOptions { get; } =
    [
        "Name (A-Z)",
        "Name (Z-A)",
        "Stock (Low to High)",
        "Stock (High to Low)",
        "Price (Low to High)",
        "Price (High to Low)",
        "Recently Added",
        "Expiry Date",
    ];

    public int? CurrentUserId => session.CurrentUser?.Id;

    public async Task InitializeAsync()
    {
        await ReloadFilterOptionsAsync();
        await SearchAsync();
    }

    public async Task ReloadFilterOptionsAsync()
    {
        var categories = await categoryService.GetAllAsync();
        var brands = await brandService.GetAllAsync();
        var suppliers = CanConfigureReplenishment
            ? await supplierService.SearchAsync(new SupplierSearchQuery(), CurrentUserId)
            : [];

        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(category);
        }

        Brands.Clear();
        foreach (var brand in brands)
        {
            Brands.Add(brand);
        }

        Suppliers.Clear();
        foreach (var supplier in suppliers.Where(s => s.IsActive))
        {
            Suppliers.Add(supplier);
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await productService.SearchAsync(new ProductSearchQuery
            {
                SearchText = SearchText,
                IncludeInactive = InactiveOnly,
            });

            var rows = new List<ProductRowViewModel>();
            foreach (var product in results)
            {
                var row = await ToRowAsync(product);

                // Every checkbox on this toolbar narrows the list to just those products, rather
                // than adding them to the active ones — "Show inactive" behaves like its neighbours
                // "Out of stock" and "Expired". The service call above widens the query to include
                // inactive rows; this narrows it back down to only them.
                if ((!InactiveOnly || !row.IsActive)
                    && (!LowStockOnly || row.StockStatus == "LOW STOCK")
                    && (!OutOfStockOnly || row.Stock <= 0)
                    && (!ExpiredOnly || row.ExpiryStatus == "EXPIRED"))
                {
                    rows.Add(row);
                }
            }

            IEnumerable<ProductRowViewModel> sorted = SelectedSortOption switch
            {
                "Name (Z-A)" => rows.OrderByDescending(r => r.Name),
                "Stock (Low to High)" => rows.OrderBy(r => r.Stock).ThenBy(r => r.Name),
                "Stock (High to Low)" => rows.OrderByDescending(r => r.Stock).ThenBy(r => r.Name),
                "Price (Low to High)" => rows.OrderBy(r => r.SellingPrice).ThenBy(r => r.Name),
                "Price (High to Low)" => rows.OrderByDescending(r => r.SellingPrice).ThenBy(r => r.Name),
                "Recently Added" => rows.OrderByDescending(r => r.CreatedAtUtc),
                // Products without an expiry (no batches, or batches with no expiry set) sort to
                // the end rather than the front — the soonest real expiry is what needs attention.
                "Expiry Date" => rows.OrderBy(r => r.NearestExpiryDate ?? DateOnly.MaxValue).ThenBy(r => r.Name),
                _ => rows.OrderBy(r => r.Name),
            };

            Products.Clear();
            foreach (var row in sorted)
            {
                Products.Add(row);
            }

            TotalProductsText = Products.Count == 1 ? "1 product" : $"{Products.Count} products";
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

    public Task<Product> CreateProductAsync(CreateProductRequest request) =>
        productService.CreateAsync(request);

    public void ApplyNavigationFilter(ProductListNavigationFilter filter)
    {
        SearchText = string.Empty;
        InactiveOnly = false;
        ExpiredOnly = false;
        LowStockOnly = filter == ProductListNavigationFilter.LowStock;
        OutOfStockOnly = filter == ProductListNavigationFilter.OutOfStock;
    }

    public Task<Product> UpdateProductAsync(int productId, UpdateProductRequest request) =>
        productService.UpdateAsync(productId, request);

    public async Task SetActiveAsync(int productId, bool isActive) =>
        await productService.SetActiveAsync(productId, isActive, CurrentUserId);

    public async Task<Product?> GetProductAsync(int productId) => await productService.GetByIdAsync(productId);

    public async Task<Category> CreateCategoryAsync(string name) =>
        await categoryService.CreateAsync(name, CurrentUserId);

    public async Task SetCategoryActiveAsync(int categoryId, bool isActive) =>
        await categoryService.SetActiveAsync(categoryId, isActive, CurrentUserId);

    public async Task<Brand> CreateBrandAsync(string name) =>
        await brandService.CreateAsync(name, CurrentUserId);

    public async Task SetBrandActiveAsync(int brandId, bool isActive) =>
        await brandService.SetActiveAsync(brandId, isActive, CurrentUserId);

    // No AdjustStockAsync passthrough here any more (Phase 13D): manual stock changes go through
    // IInventoryAdjustmentService, which records a reason, writes an adjustment record and refuses
    // to drive stock negative. Leaving a shortcut on this ViewModel would reintroduce the weaker
    // path the 13D page replaced.

    public Task<IReadOnlyList<StockMovement>> GetMovementHistoryAsync(int productId) =>
        inventoryService.GetMovementHistoryAsync(productId, take: 20);

    public Task<IReadOnlyList<ProductBatch>> GetBatchesAsync(int productId) =>
        inventoryService.GetBatchesAsync(productId);

    public Task<decimal> GetStockAsync(int productId) => inventoryService.GetStockAsync(productId);

    public async Task AddBatchAsync(int productId, string batchNumber, DateOnly? mfgDate, DateOnly? expiryDate, decimal quantity, decimal? purchasePrice, decimal? sellingPrice) =>
        await inventoryService.AddBatchAsync(productId, batchNumber, mfgDate, expiryDate, quantity, purchasePrice, sellingPrice, CurrentUserId);

    public Task UpdateBatchExpiryAsync(int batchId, DateOnly? expiryDate) =>
        inventoryService.UpdateBatchExpiryAsync(batchId, expiryDate, CurrentUserId);

    private async Task<ProductRowViewModel> ToRowAsync(Product product)
    {
        var stock = product.Inventory?.QuantityOnHand ?? await inventoryService.GetStockAsync(product.Id);

        var status = stock <= 0
            ? "OUT OF STOCK"
            : product.MinimumStock > 0 && stock <= product.MinimumStock
                ? "LOW STOCK"
                : "";

        // Only batch-tracked products can have an expiry at all, so the extra query is skipped
        // entirely for the common (non-batch) case rather than run on every row.
        DateOnly? nearestExpiry = null;
        if (product.TracksBatches)
        {
            var batches = await inventoryService.GetBatchesAsync(product.Id);
            nearestExpiry = batches
                .Where(b => b.Quantity > 0 && b.ExpiryDate is not null)
                .OrderBy(b => b.ExpiryDate)
                .Select(b => b.ExpiryDate)
                .FirstOrDefault();
        }

        return new ProductRowViewModel
        {
            Id = product.Id,
            ProductCode = product.ProductCode,
            Name = product.Name,
            Sku = product.Sku,
            Barcodes = product.Barcodes
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id)
                .Select(ProductBarcodeOption.From)
                .ToList(),
            CategoryName = product.Category?.Name ?? "",
            BrandName = product.Brand?.Name ?? "",
            Unit = product.UnitDisplayText ?? product.Unit.ToDisplayText(),
            SellingPrice = product.SellingPrice,
            Mrp = product.Mrp,
            PurchasePrice = CanViewPurchasePrice ? product.PurchasePrice : null,
            ShowPurchasePrice = CanViewPurchasePrice,
            PricingType = product.PricingType,
            Stock = stock,
            IsActive = product.IsActive,
            TracksBatches = product.TracksBatches,
            StockStatus = status,
            CreatedAtUtc = product.CreatedAtUtc,
            NearestExpiryDate = nearestExpiry,
        };
    }
}
