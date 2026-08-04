using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Application.Products;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Products management page (PRD §12-13, §24, §26).</summary>
public sealed partial class ProductsViewModel(
    IProductService productService,
    ICategoryService categoryService,
    IBrandService brandService,
    IInventoryService inventoryService,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private Brand? _selectedBrand;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanEditProducts => session.HasPermission(PermissionKeys.ProductsEdit);
    public bool CanViewPurchasePrice => session.HasPermission(PermissionKeys.PricingViewPurchasePrice);
    public bool CanManageInventory => session.HasPermission(PermissionKeys.InventoryManage);

    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public ObservableCollection<ProductRowViewModel> Products { get; } = [];

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
                CategoryId = SelectedCategory?.Id,
                BrandId = SelectedBrand?.Id,
                IncludeInactive = ShowInactive,
            });

            Products.Clear();
            foreach (var product in results)
            {
                Products.Add(await ToRowAsync(product));
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

    public Task<Product> CreateProductAsync(CreateProductRequest request) =>
        productService.CreateAsync(request);

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

    public async Task AdjustStockAsync(int productId, decimal quantityChange, StockMovementType movementType, string? reason) =>
        await inventoryService.AdjustStockAsync(productId, quantityChange, movementType, reason, CurrentUserId);

    public Task<IReadOnlyList<StockMovement>> GetMovementHistoryAsync(int productId) =>
        inventoryService.GetMovementHistoryAsync(productId, take: 20);

    public Task<IReadOnlyList<ProductBatch>> GetBatchesAsync(int productId) =>
        inventoryService.GetBatchesAsync(productId);

    public async Task AddBatchAsync(int productId, string batchNumber, DateOnly? mfgDate, DateOnly? expiryDate, decimal quantity, decimal? purchasePrice, decimal? sellingPrice) =>
        await inventoryService.AddBatchAsync(productId, batchNumber, mfgDate, expiryDate, quantity, purchasePrice, sellingPrice, CurrentUserId);

    private async Task<ProductRowViewModel> ToRowAsync(Product product)
    {
        var stock = product.Inventory?.QuantityOnHand ?? await inventoryService.GetStockAsync(product.Id);

        var status = stock <= 0
            ? "OUT OF STOCK"
            : product.MinimumStock > 0 && stock <= product.MinimumStock
                ? "LOW STOCK"
                : "";

        return new ProductRowViewModel
        {
            Id = product.Id,
            ProductCode = product.ProductCode,
            Name = product.Name,
            Sku = product.Sku,
            Barcode = product.Barcode,
            CategoryName = product.Category?.Name ?? "",
            BrandName = product.Brand?.Name ?? "",
            Unit = product.Unit.ToString(),
            SellingPrice = product.SellingPrice,
            Mrp = product.Mrp,
            PurchasePrice = CanViewPurchasePrice ? product.PurchasePrice : null,
            ShowPurchasePrice = CanViewPurchasePrice,
            Stock = stock,
            IsActive = product.IsActive,
            TracksBatches = product.TracksBatches,
            StockStatus = status,
        };
    }
}
