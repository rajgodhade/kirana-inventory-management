using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.Promotions;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed partial class PromotionsViewModel(IPromotionService promotionService, ManagementSession session) : ObservableObject
{
    public ObservableCollection<PromotionRowViewModel> Promotions { get; } = [];
    public IReadOnlyList<string> StatusFilters { get; } = ["All statuses", .. Enum.GetNames<PromotionStatus>()];
    public IReadOnlyList<string> ScopeFilters { get; } = ["All scopes", .. Enum.GetNames<PromotionScopeType>()];
    public IReadOnlyList<string> TypeFilters { get; } = ["All types", .. Enum.GetNames<PromotionType>()];
    public bool CanManage => session.HasPermission(PermissionKeys.PromotionsManage);
    public int? CurrentUserId => session.CurrentUser?.Id;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedStatus = "All statuses";
    [ObservableProperty] private string _selectedScope = "All scopes";
    [ObservableProperty] private string _selectedType = "All types";
    [ObservableProperty] private bool _runningOnly;
    [ObservableProperty] private string _totalText = "0";
    [ObservableProperty] private string _runningText = "0";
    [ObservableProperty] private string _upcomingText = "0";
    [ObservableProperty] private string _expiredText = "0";
    [ObservableProperty] private string _disabledText = "0";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var query = new PromotionSearchQuery
            {
                SearchText = SearchText,
                Status = Enum.TryParse<PromotionStatus>(SelectedStatus, out var status) ? status : null,
                ScopeType = Enum.TryParse<PromotionScopeType>(SelectedScope, out var scope) ? scope : null,
                PromotionType = Enum.TryParse<PromotionType>(SelectedType, out var type) ? type : null,
                RunningOnly = RunningOnly,
            };
            var promotions = await promotionService.SearchAsync(query, CurrentUserId);
            Promotions.Clear();
            foreach (var promotion in promotions) Promotions.Add(new PromotionRowViewModel(promotion));
            var summary = await promotionService.GetSummaryAsync(CurrentUserId);
            TotalText = summary.Total.ToString("N0"); RunningText = summary.Running.ToString("N0");
            UpcomingText = summary.Upcoming.ToString("N0"); ExpiredText = summary.Expired.ToString("N0");
            DisabledText = summary.Disabled.ToString("N0");
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public Task SetActiveAsync(PromotionRowViewModel row, bool active) =>
        promotionService.SetActiveAsync(row.Id, active, CurrentUserId);
    public Task DeleteAsync(PromotionRowViewModel row) => promotionService.DeleteAsync(row.Id, CurrentUserId);
}

public sealed class PromotionRowViewModel
{
    public PromotionRowViewModel(Promotion promotion)
    {
        Id = promotion.Id; Name = promotion.PromotionName; Code = promotion.PromotionCode;
        Type = promotion.PromotionType.ToString(); Scope = promotion.Scope?.ScopeType.ToString() ?? "—";
        Discount = promotion.PromotionType switch
        {
            PromotionType.Percentage => $"{promotion.Percentage:0.##}%",
            PromotionType.FlatAmount => $"₹{promotion.FlatAmount:0.00}",
            _ => $"₹{promotion.FixedPrice:0.00} fixed price",
        };
        Start = promotion.Schedule?.StartAtUtc.ToLocalTime().ToString("dd MMM yy, hh:mm tt") ?? "—";
        End = promotion.Schedule?.EndAtUtc.ToLocalTime().ToString("dd MMM yy, hh:mm tt") ?? "—";
        Status = promotion.Status.ToString(); Priority = promotion.Priority.ToString(); IsActive = promotion.IsActive;
        ActionText = promotion.IsActive ? "Disable" : "Activate";
    }
    public int Id { get; }
    public string Name { get; }
    public string Code { get; }
    public string Type { get; }
    public string Scope { get; }
    public string Discount { get; }
    public string Start { get; }
    public string End { get; }
    public string Status { get; }
    public string Priority { get; }
    public bool IsActive { get; }
    public string ActionText { get; }
}

public sealed partial class PromotionEditViewModel(
    IPromotionService promotionService, ICategoryService categoryService, IBrandService brandService,
    IProductService productService, ManagementSession session, int? promotionId = null) : ObservableObject
{
    private readonly List<Brand> _allBrands = [];
    private readonly List<PromotionProductTargetViewModel> _allProducts = [];
    public bool IsEdit => promotionId is not null;
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public ObservableCollection<PromotionProductTargetViewModel> Products { get; } = [];
    public IReadOnlyList<PromotionScopeType> ScopeTypes { get; } = Enum.GetValues<PromotionScopeType>();
    public IReadOnlyList<PromotionType> PromotionTypes { get; } = Enum.GetValues<PromotionType>();
    public IReadOnlyList<DiscountCalculationMode> CalculationModes { get; } = Enum.GetValues<DiscountCalculationMode>();
    public IReadOnlyList<PromotionPriorityMode> PriorityModes { get; } = Enum.GetValues<PromotionPriorityMode>();
    public IReadOnlyList<int> ExistingTargetIds { get; private set; } = [];

    [ObservableProperty] private string _promotionName = string.Empty;
    [ObservableProperty] private string _promotionCode = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _priorityText = "0";
    [ObservableProperty] private DateTimeOffset? _startDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? _endDate = DateTimeOffset.Now.AddDays(7);
    [ObservableProperty] private TimeSpan _startTime = DateTime.Now.TimeOfDay;
    [ObservableProperty] private TimeSpan _endTime = DateTime.Now.TimeOfDay;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCategoryScope))] [NotifyPropertyChangedFor(nameof(IsBrandScope))] [NotifyPropertyChangedFor(nameof(IsProductScope))] private PromotionScopeType _selectedScopeType;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPercentage))] [NotifyPropertyChangedFor(nameof(IsFlatAmount))] [NotifyPropertyChangedFor(nameof(IsFixedPrice))] private PromotionType _selectedPromotionType;
    [ObservableProperty] private DiscountCalculationMode _selectedCalculationMode;
    [ObservableProperty] private PromotionPriorityMode _selectedPriorityMode;
    [ObservableProperty] private string _percentageText = "10";
    [ObservableProperty] private string _flatAmountText = string.Empty;
    [ObservableProperty] private string _fixedPriceText = string.Empty;
    [ObservableProperty] private string _minimumQuantityText = string.Empty;
    [ObservableProperty] private string _minimumBillText = string.Empty;
    [ObservableProperty] private string _maximumDiscountText = string.Empty;
    [ObservableProperty] private string _maximumUsageText = string.Empty;
    [ObservableProperty] private bool _allowStacking;
    [ObservableProperty] private bool _activateImmediately = true;
    [ObservableProperty] private string _previewPriceText = "220";
    [ObservableProperty] private string _previewCurrentText = "₹220.00";
    [ObservableProperty] private string _previewFinalText = "₹198.00";
    [ObservableProperty] private string _previewSavingsText = "Save ₹22.00";
    [ObservableProperty] private string? _errorMessage;

    public bool IsCategoryScope => SelectedScopeType == PromotionScopeType.Category;
    public bool IsBrandScope => SelectedScopeType == PromotionScopeType.Brand;
    public bool IsProductScope => SelectedScopeType == PromotionScopeType.Product;
    public bool IsPercentage => SelectedPromotionType == PromotionType.Percentage;
    public bool IsFlatAmount => SelectedPromotionType == PromotionType.FlatAmount;
    public bool IsFixedPrice => SelectedPromotionType == PromotionType.FixedSellingPrice;

    public async Task LoadAsync()
    {
        foreach (var item in await categoryService.GetAllAsync()) Categories.Add(item);
        foreach (var item in await brandService.GetAllAsync()) { _allBrands.Add(item); Brands.Add(item); }
        foreach (var item in await productService.SearchAsync(new ProductSearchQuery { MaxResults = 1000 }))
        {
            var row = new PromotionProductTargetViewModel(item);
            _allProducts.Add(row);
            Products.Add(row);
        }
        if (promotionId is not { } id) { UpdatePreview(); return; }
        var promotion = await promotionService.GetByIdAsync(id, session.CurrentUser?.Id) ?? throw new InvalidOperationException("Promotion not found.");
        PromotionName = promotion.PromotionName; PromotionCode = promotion.PromotionCode; Description = promotion.Description ?? string.Empty;
        PriorityText = promotion.Priority.ToString(); SelectedScopeType = promotion.Scope!.ScopeType; SelectedPromotionType = promotion.PromotionType;
        SelectedCalculationMode = promotion.CalculationMode; SelectedPriorityMode = promotion.PriorityMode;
        PercentageText = promotion.Percentage?.ToString("0.##") ?? string.Empty; FlatAmountText = promotion.FlatAmount?.ToString("0.##") ?? string.Empty;
        FixedPriceText = promotion.FixedPrice?.ToString("0.##") ?? string.Empty; MinimumQuantityText = promotion.MinimumQuantity?.ToString("0.###") ?? string.Empty;
        MinimumBillText = promotion.MinimumBillAmount?.ToString("0.##") ?? string.Empty; MaximumDiscountText = promotion.MaximumDiscount?.ToString("0.##") ?? string.Empty;
        MaximumUsageText = promotion.MaximumUsage?.ToString() ?? string.Empty; AllowStacking = promotion.AllowStacking; ActivateImmediately = promotion.IsActive;
        StartDate = promotion.Schedule!.StartAtUtc.ToLocalTime(); EndDate = promotion.Schedule.EndAtUtc.ToLocalTime();
        StartTime = StartDate.Value.TimeOfDay; EndTime = EndDate.Value.TimeOfDay;
        ExistingTargetIds = promotion.Scope.Targets.Select(x => x.CategoryId ?? x.BrandId ?? x.ProductId ?? 0).Where(x => x > 0).ToList();
        UpdatePreview();
    }

    public void FilterProducts(string? text)
    {
        var query = text?.Trim();
        Products.Clear();
        foreach (var product in _allProducts.Where(x => string.IsNullOrEmpty(query)
            || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.ProductCode.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (x.Sku?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
            Products.Add(product);
    }

    public void FilterBrands(string? text)
    {
        var query = text?.Trim();
        Brands.Clear();
        foreach (var brand in _allBrands.Where(x => string.IsNullOrEmpty(query)
            || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            Brands.Add(brand);
        }
    }

    public async Task SaveAsync(IReadOnlyList<int> targetIds)
    {
        ErrorMessage = null;
        try
        {
            var request = BuildRequest(targetIds);
            if (promotionId is { } id) await promotionService.UpdateAsync(id, request);
            else await promotionService.CreateAsync(request);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public void UpdatePreview()
    {
        try
        {
            var request = BuildRequest([]);
            var current = ParseDecimal(PreviewPriceText) ?? 0;
            var preview = promotionService.Preview(request, current);
            PreviewCurrentText = $"₹{preview.CurrentPrice:N2}"; PreviewFinalText = $"₹{preview.FinalPrice:N2}"; PreviewSavingsText = $"Save ₹{preview.Savings:N2}";
            UpdateProductPreviews(request);
        }
        catch
        {
            PreviewFinalText = "—";
            PreviewSavingsText = "Enter valid discount values";
            foreach (var product in _allProducts) product.SetInvalidPreview();
        }
    }

    private void UpdateProductPreviews(SavePromotionRequest request)
    {
        foreach (var product in _allProducts)
        {
            try
            {
                var preview = promotionService.Preview(request, product.SellingPrice);
                product.SetPromotionPrice(preview.FinalPrice, preview.Savings);
            }
            catch
            {
                product.SetInvalidPreview();
            }
        }
    }

    private SavePromotionRequest BuildRequest(IReadOnlyList<int> targetIds)
    {
        var startLocal = (StartDate ?? DateTimeOffset.Now).Date + StartTime;
        var endLocal = (EndDate ?? DateTimeOffset.Now).Date + EndTime;
        return new SavePromotionRequest
        {
            PromotionName = PromotionName, PromotionCode = PromotionCode, Description = Description,
            PromotionType = SelectedPromotionType, Percentage = ParseDecimal(PercentageText), FlatAmount = ParseDecimal(FlatAmountText), FixedPrice = ParseDecimal(FixedPriceText),
            Priority = int.TryParse(PriorityText, out var priority) ? priority : 0, PriorityMode = SelectedPriorityMode,
            CalculationMode = SelectedCalculationMode, AllowStacking = AllowStacking, MaximumDiscount = ParseDecimal(MaximumDiscountText),
            MinimumBillAmount = ParseDecimal(MinimumBillText), MinimumQuantity = ParseDecimal(MinimumQuantityText),
            MaximumUsage = int.TryParse(MaximumUsageText, out var usage) ? usage : null,
            StartAtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local),
            EndAtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local),
            TimeZoneId = TimeZoneInfo.Local.Id, ScopeType = SelectedScopeType, TargetIds = targetIds,
            ActivateImmediately = ActivateImmediately, PerformedByUserId = session.CurrentUser?.Id,
        };
    }

    private static decimal? ParseDecimal(string text) => decimal.TryParse(text, out var value) ? value : null;
}

public sealed partial class PromotionProductTargetViewModel(Product product) : ObservableObject
{
    public int Id => product.Id;
    public string Name => product.Name;
    public string ProductCode => product.ProductCode;
    public string? Sku => product.Sku;
    public decimal Mrp => product.Mrp;
    public decimal SellingPrice => product.SellingPrice;
    public string MrpText => $"MRP ₹{Mrp:N2}";
    public string SellingPriceText => $"Selling ₹{SellingPrice:N2}";

    [ObservableProperty] private string _promotionPriceText = $"Promo ₹{product.SellingPrice:N2}";
    [ObservableProperty] private string _savingText = string.Empty;

    public void SetPromotionPrice(decimal finalPrice, decimal saving)
    {
        PromotionPriceText = $"Promo ₹{finalPrice:N2}";
        SavingText = saving > 0 ? $"Save ₹{saving:N2}" : "No saving";
    }

    public void SetInvalidPreview()
    {
        PromotionPriceText = "Promo —";
        SavingText = "Check discount";
    }
}
