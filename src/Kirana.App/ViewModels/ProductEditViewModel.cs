using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Abstractions;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Add/Edit Product dialog (PRD §11-15, §16-17). The barcode field renders a
/// live preview here purely for user feedback — actual assignment/generation just fills the text
/// box; the value is persisted through the normal Save flow like every other field.</summary>
public sealed partial class ProductEditViewModel : ObservableObject
{
    private readonly ProductsViewModel _owner;
    private readonly IBarcodeService _barcodeService;
    private readonly IBarcodeRenderer _barcodeRenderer;
    private readonly int? _editingProductId;

    public bool IsEditMode { get; }
    public bool ShowPurchasePrice { get; }
    public IReadOnlyList<UnitOfMeasure> Units { get; } = Enum.GetValues<UnitOfMeasure>();
    public IReadOnlyList<PricingType> PricingTypes { get; } = Enum.GetValues<PricingType>();

    public System.Collections.ObjectModel.ObservableCollection<Category> Categories => _owner.Categories;
    public System.Collections.ObjectModel.ObservableCollection<Brand> Brands => _owner.Brands;
    public System.Collections.ObjectModel.ObservableCollection<Supplier> Suppliers => _owner.Suppliers;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _sku;

    /// <summary>The "add a barcode" input, not a stored value — each code is committed to
    /// <see cref="Barcodes"/> by the Add command (Phase 13B).</summary>
    [ObservableProperty]
    private string? _newBarcodeInput;

    /// <summary>Every code on this product, primary first. In edit mode each change persists
    /// immediately through <c>IBarcodeService</c>; in create mode the list is staged here and
    /// shipped whole in the create request on Save.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ProductBarcodeRowViewModel> Barcodes { get; } = [];

    public bool HasBarcodes => Barcodes.Count > 0;

    /// <summary>Shown in edit mode only: barcode actions here save straight away, unlike every
    /// other field in this dialog, so the dialog says so rather than surprising the operator.</summary>
    public bool ShowImmediateSaveHint => IsEditMode;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private Brand? _selectedBrand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinimumStockHeader))]
    [NotifyPropertyChangedFor(nameof(ReorderQuantityHeader))]
    [NotifyPropertyChangedFor(nameof(OpeningStockHeader))]
    [NotifyPropertyChangedFor(nameof(PackSizeHeader))]
    [NotifyPropertyChangedFor(nameof(CurrentStockText))]
    [NotifyPropertyChangedFor(nameof(SuggestedReorderText))]
    private UnitOfMeasure _selectedUnit = UnitOfMeasure.Piece;

    /// <summary>Inventory quantity fields show the selected unit right in the header (e.g.
    /// "Minimum stock (Kilogram)") so it is obvious what "5" in the box actually means — the same
    /// unit the product's own stock ledger and POS billing already use.</summary>
    public string MinimumStockHeader => $"Minimum stock ({SelectedUnit})";
    public string ReorderQuantityHeader => $"Target stock ({SelectedUnit})";
    public string OpeningStockHeader => $"Opening stock ({SelectedUnit})";

    /// <summary>Optional purchase pack (Phase 13A) — most products never need this, so it stays
    /// collapsed behind a checkbox instead of always showing two extra fields. Selling/stock/
    /// billing always stay in <see cref="SelectedUnit"/>; this only affects Purchase Entry.</summary>
    [ObservableProperty]
    private bool _hasPurchasePack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackSizeHeader))]
    private UnitOfMeasure _selectedPackUnit = UnitOfMeasure.Box;

    [ObservableProperty]
    private string _packSizeText = string.Empty;

    [ObservableProperty]
    private string? _unitDisplayTextInput;

    public string PackSizeHeader => $"1 {SelectedPackUnit} = how many {SelectedUnit}?";

    [ObservableProperty]
    private string _purchasePriceText = "0";

    [ObservableProperty]
    private string _mrpText = "0";

    [ObservableProperty]
    private string _sellingPriceText = "0";

    [ObservableProperty]
    private string? _wholesalePriceText;

    [ObservableProperty]
    private string? _defaultDiscountPercentText;

    [ObservableProperty]
    private string? _gstRatePercentText;

    [ObservableProperty]
    private string? _hsnCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PricingTypeHelpText))]
    private PricingType _selectedPricingType = PricingType.Inclusive;

    public string PricingTypeHelpText => SelectedPricingType == PricingType.Inclusive
        ? "The selling price already includes GST."
        : "GST will be added during billing.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedReorderText))]
    private string _minimumStockText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedReorderText))]
    private string _reorderQuantityText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedReorderText))]
    private bool _replenishmentEnabled;

    [ObservableProperty]
    private Supplier? _selectedPreferredSupplier;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStockText))]
    [NotifyPropertyChangedFor(nameof(SuggestedReorderText))]
    private decimal _currentStock;

    public string CurrentStockText => $"{CurrentStock:0.###} {SelectedUnit}";
    public string SuggestedReorderText
    {
        get
        {
            if (!ReplenishmentEnabled) return "Not configured";
            if (!decimal.TryParse(MinimumStockText, out var reorder)
                || !decimal.TryParse(ReorderQuantityText, out var target)
                || reorder < 0 || target < reorder) return "Fix configuration to calculate";
            var suggested = CurrentStock <= reorder ? Math.Max(target - CurrentStock, 0) : 0;
            return $"{suggested:0.###} {SelectedUnit}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpiryField))]
    private bool _tracksBatches;

    [ObservableProperty]
    private DateTimeOffset? _expiryDate;

    [ObservableProperty]
    private bool _canEditExpiryInline = true;

    [ObservableProperty]
    private string _expiryHelpText = "Set the expiry for this product's current batch.";

    public bool ShowExpiryField => TracksBatches;

    private int? _editingBatchId;

    [ObservableProperty]
    private string _openingStockText = "0";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private WriteableBitmap? _barcodePreviewImage;

    [ObservableProperty]
    private string _barcodeStatusText = "";

    [ObservableProperty]
    private bool _isGeneratingBarcode;

    public event EventHandler? Saved;

    /// <summary>Create mode.</summary>
    public ProductEditViewModel(ProductsViewModel owner, IBarcodeService barcodeService, IBarcodeRenderer barcodeRenderer)
    {
        _owner = owner;
        _barcodeService = barcodeService;
        _barcodeRenderer = barcodeRenderer;
        IsEditMode = false;
        ShowPurchasePrice = owner.CanViewPurchasePrice;
        CurrentStock = 0;
    }

    /// <summary>Edit mode, pre-filled from an existing product.</summary>
    public ProductEditViewModel(ProductsViewModel owner, IBarcodeService barcodeService, IBarcodeRenderer barcodeRenderer, Product existing)
    {
        _owner = owner;
        _barcodeService = barcodeService;
        _barcodeRenderer = barcodeRenderer;
        _editingProductId = existing.Id;
        IsEditMode = true;
        ShowPurchasePrice = owner.CanViewPurchasePrice;

        Name = existing.Name;
        Sku = existing.Sku;
        Description = existing.Description;

        foreach (var barcode in existing.Barcodes
                     .OrderByDescending(b => b.IsActive)
                     .ThenByDescending(b => b.IsPrimary)
                     .ThenBy(b => b.Id))
        {
            Barcodes.Add(ProductBarcodeRowViewModel.From(barcode));
        }
        SelectedCategory = owner.Categories.FirstOrDefault(c => c.Id == existing.CategoryId);
        SelectedBrand = owner.Brands.FirstOrDefault(b => b.Id == existing.BrandId);
        SelectedUnit = existing.Unit;
        HasPurchasePack = existing.PurchasePackUnit is not null;
        SelectedPackUnit = existing.PurchasePackUnit ?? UnitOfMeasure.Box;
        PackSizeText = existing.PurchasePackSize?.ToString("0.###") ?? string.Empty;
        UnitDisplayTextInput = existing.UnitDisplayText;
        PurchasePriceText = existing.PurchasePrice.ToString("0.##");
        MrpText = existing.Mrp.ToString("0.##");
        SellingPriceText = existing.SellingPrice.ToString("0.##");
        WholesalePriceText = existing.WholesalePrice?.ToString("0.##");
        DefaultDiscountPercentText = existing.DefaultDiscountPercent?.ToString("0.##");
        GstRatePercentText = existing.GstRatePercent?.ToString("0.##");
        HsnCode = existing.HsnCode;
        SelectedPricingType = existing.PricingType;
        MinimumStockText = existing.MinimumStock.ToString("0.###");
        ReorderQuantityText = existing.ReorderQuantity.ToString("0.###");
        ReplenishmentEnabled = existing.ReplenishmentEnabled;
        SelectedPreferredSupplier = owner.Suppliers.FirstOrDefault(s => s.Id == existing.PreferredSupplierId);
        CurrentStock = existing.Inventory?.QuantityOnHand ?? 0;
        TracksBatches = existing.TracksBatches;

        UpdateBarcodePreview();
    }

    public async Task LoadBatchExpiryAsync()
    {
        if (!IsEditMode || !TracksBatches)
        {
            return;
        }

        var batches = await _owner.GetBatchesAsync(_editingProductId!.Value);
        if (batches.Count == 1)
        {
            _editingBatchId = batches[0].Id;
            ExpiryDate = batches[0].ExpiryDate is { } date
                ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue))
                : null;
            ExpiryHelpText = "Expiry is stored on this product's current batch.";
        }
        else if (batches.Count > 1)
        {
            CanEditExpiryInline = false;
            ExpiryHelpText = "This product has multiple batches. Use the Batches action on the Products page to set each expiry date separately.";
        }
    }

    partial void OnNewBarcodeInputChanged(string? value) => UpdateBarcodePreview();

    private void UpdateBarcodePreview()
    {
        if (string.IsNullOrWhiteSpace(NewBarcodeInput))
        {
            BarcodePreviewImage = null;
            BarcodeStatusText = "";
            return;
        }

        try
        {
            _barcodeService.ValidateFormat(NewBarcodeInput);
        }
        catch (ArgumentException ex)
        {
            BarcodePreviewImage = null;
            BarcodeStatusText = ex.Message;
            return;
        }

        var symbology = _barcodeService.DetermineSymbology(NewBarcodeInput);
        BarcodeStatusText = symbology == BarcodeSymbology.Ean13 ? "Valid EAN-13" : "CODE128 (not a standard EAN-13)";

        var rendered = _barcodeRenderer.Render(NewBarcodeInput, symbology, 240, 80);
        BarcodePreviewImage = ToWriteableBitmap(rendered);
    }

    [RelayCommand]
    private async Task GenerateBarcodeAsync()
    {
        ErrorMessage = null;
        IsGeneratingBarcode = true;
        try
        {
            NewBarcodeInput = await _barcodeService.GenerateInternalBarcodeAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsGeneratingBarcode = false;
        }
    }

    // ------------------------------------------------------------- barcode list (Phase 13B)
    // Each command branches on IsEditMode: with a real product id the change persists immediately
    // through IBarcodeService (which owns the one-primary and uniqueness invariants); without one
    // it only mutates this in-memory list, which Save then ships whole in the create request.

    [RelayCommand]
    private async Task AddBarcodeAsync()
    {
        ErrorMessage = null;

        var value = NewBarcodeInput?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorMessage = "Enter or generate a barcode first.";
            return;
        }

        try
        {
            _barcodeService.ValidateFormat(value);

            if (Barcodes.Any(b => string.Equals(b.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorMessage = $"'{value}' is already on this product.";
                return;
            }

            if (IsEditMode)
            {
                var added = await _barcodeService.AddBarcodeAsync(
                    _editingProductId!.Value, value, makePrimary: false, _owner.CurrentUserId);
                await ReloadBarcodesAsync();
                NewBarcodeInput = null;
                _ = added;
                return;
            }

            // Create mode: uniqueness still has to be checked against the rest of the catalogue,
            // even though nothing is written until Save.
            await _barcodeService.EnsureAvailableAsync(value, excludingProductId: null);

            Barcodes.Add(new ProductBarcodeRowViewModel
            {
                Value = value,
                Symbology = _barcodeService.DetermineSymbology(value),
                IsPrimary = Barcodes.Count == 0,
                IsActive = true,
            });

            NewBarcodeInput = null;
            OnPropertyChanged(nameof(HasBarcodes));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SetPrimaryBarcodeAsync(ProductBarcodeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            if (IsEditMode)
            {
                await _barcodeService.SetPrimaryAsync(row.Id, _owner.CurrentUserId);
                await ReloadBarcodesAsync();
                return;
            }

            foreach (var other in Barcodes)
            {
                other.IsPrimary = ReferenceEquals(other, row);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleBarcodeActiveAsync(ProductBarcodeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            if (IsEditMode)
            {
                await _barcodeService.SetBarcodeActiveAsync(row.Id, !row.IsActive, _owner.CurrentUserId);
                await ReloadBarcodesAsync();
                return;
            }

            row.IsActive = !row.IsActive;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveBarcodeAsync(ProductBarcodeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            if (IsEditMode)
            {
                await _barcodeService.RemoveBarcodeAsync(row.Id, _owner.CurrentUserId);
                await ReloadBarcodesAsync();
                return;
            }

            Barcodes.Remove(row);
            if (Barcodes.Count > 0 && !Barcodes.Any(b => b.IsPrimary))
            {
                Barcodes[0].IsPrimary = true;
            }

            OnPropertyChanged(nameof(HasBarcodes));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task ReloadBarcodesAsync()
    {
        var fresh = await _barcodeService.GetForProductAsync(_editingProductId!.Value);
        Barcodes.Clear();
        foreach (var barcode in fresh)
        {
            Barcodes.Add(ProductBarcodeRowViewModel.From(barcode));
        }

        OnPropertyChanged(nameof(HasBarcodes));
    }

    private static WriteableBitmap ToWriteableBitmap(BarcodeRenderResult result)
    {
        var bitmap = new WriteableBitmap(result.PixelWidth, result.PixelHeight);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(result.Bgra32Pixels, 0, result.Bgra32Pixels.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Product name is required.";
            return;
        }

        if (!TryParseDecimal(PurchasePriceText, out var purchasePrice) ||
            !TryParseDecimal(MrpText, out var mrp) ||
            !TryParseDecimal(SellingPriceText, out var sellingPrice))
        {
            ErrorMessage = "Purchase price, MRP, and selling price must be valid numbers.";
            return;
        }

        if (!TryParseOptionalDecimal(WholesalePriceText, out var wholesalePrice) ||
            !TryParseOptionalDecimal(DefaultDiscountPercentText, out var discountPercent) ||
            !TryParseOptionalDecimal(GstRatePercentText, out var gstRatePercent))
        {
            ErrorMessage = "Wholesale price, discount, and GST rate must be valid numbers.";
            return;
        }

        if (!TryParseDecimal(MinimumStockText, out var minimumStock) || !TryParseDecimal(ReorderQuantityText, out var reorderQuantity))
        {
            ErrorMessage = "Minimum stock and reorder quantity must be valid numbers.";
            return;
        }

        if (ReplenishmentEnabled && reorderQuantity < minimumStock)
        {
            ErrorMessage = "Target stock must be greater than or equal to reorder level.";
            return;
        }

        // Mirrors the same whole-unit rule SaleService enforces at billing time — a product sold
        // in whole Pieces/Boxes/etc. shouldn't be able to have a fractional minimum/reorder/opening
        // stock either, since no sale could ever land the stock level on that exact fraction.
        if (!SelectedUnit.SupportsDecimalQuantity())
        {
            if (minimumStock != Math.Floor(minimumStock) || reorderQuantity != Math.Floor(reorderQuantity))
            {
                ErrorMessage = $"'{SelectedUnit}' is a whole-unit measure — minimum stock and reorder quantity must be whole numbers.";
                return;
            }

            if (!IsEditMode && decimal.TryParse(OpeningStockText, out var wholeCheck) && wholeCheck != Math.Floor(wholeCheck))
            {
                ErrorMessage = $"'{SelectedUnit}' is a whole-unit measure — opening stock must be a whole number.";
                return;
            }
        }

        UnitOfMeasure? purchasePackUnit = null;
        decimal? purchasePackSize = null;
        if (HasPurchasePack)
        {
            if (!TryParseDecimal(PackSizeText, out var packSize) || packSize <= 0)
            {
                ErrorMessage = "Pack size must be a valid number greater than zero.";
                return;
            }

            if (SelectedPackUnit == SelectedUnit)
            {
                ErrorMessage = "Purchase pack unit must be different from the product's unit.";
                return;
            }

            purchasePackUnit = SelectedPackUnit;
            purchasePackSize = packSize;
        }

        IsSaving = true;
        try
        {
            Product savedProduct;
            if (IsEditMode)
            {
                savedProduct = await _owner.UpdateProductAsync(_editingProductId!.Value, new UpdateProductRequest
                {
                    Name = Name,
                    Sku = Sku,
                    // No barcodes here: in edit mode each barcode action already persisted itself
                    // through IBarcodeService when the operator clicked it.
                    Description = Description,
                    CategoryId = SelectedCategory?.Id,
                    BrandId = SelectedBrand?.Id,
                    Unit = SelectedUnit,
                    PurchasePackUnit = purchasePackUnit,
                    PurchasePackSize = purchasePackSize,
                    UnitDisplayText = UnitDisplayTextInput,
                    PurchasePrice = purchasePrice,
                    Mrp = mrp,
                    SellingPrice = sellingPrice,
                    WholesalePrice = wholesalePrice,
                    DefaultDiscountPercent = discountPercent,
                    GstRatePercent = gstRatePercent,
                    HsnCode = HsnCode,
                    PricingType = SelectedPricingType,
                    TracksBatches = TracksBatches,
                    MinimumStock = minimumStock,
                    ReorderQuantity = reorderQuantity,
                    UpdateReplenishmentConfiguration = true,
                    ReplenishmentEnabled = ReplenishmentEnabled,
                    PreferredSupplierId = SelectedPreferredSupplier?.Id,
                    PerformedByUserId = _owner.CurrentUserId,
                });

                if (TracksBatches && CanEditExpiryInline)
                {
                    DateOnly? expiry = ExpiryDate is { } selectedExpiry
                        ? DateOnly.FromDateTime(selectedExpiry.Date)
                        : null;

                    if (_editingBatchId is { } batchId)
                    {
                        await _owner.UpdateBatchExpiryAsync(batchId, expiry);
                    }
                    else if (expiry is not null)
                    {
                        var quantity = await _owner.GetStockAsync(savedProduct.Id);
                        await _owner.AddBatchAsync(savedProduct.Id, $"DEFAULT-{savedProduct.ProductCode}", null, expiry,
                            quantity, savedProduct.PurchasePrice, savedProduct.SellingPrice);
                    }
                }
            }
            else
            {
                if (!TryParseDecimal(OpeningStockText, out var openingStock))
                {
                    ErrorMessage = "Opening stock must be a valid number.";
                    return;
                }

                var primaryIndex = Barcodes.IndexOf(Barcodes.FirstOrDefault(b => b.IsPrimary)!);

                savedProduct = await _owner.CreateProductAsync(new CreateProductRequest
                {
                    Name = Name,
                    Sku = Sku,
                    Barcodes = Barcodes.Select(b => b.Value).ToList(),
                    PrimaryBarcodeIndex = primaryIndex < 0 ? 0 : primaryIndex,
                    Description = Description,
                    CategoryId = SelectedCategory?.Id,
                    BrandId = SelectedBrand?.Id,
                    Unit = SelectedUnit,
                    PurchasePackUnit = purchasePackUnit,
                    PurchasePackSize = purchasePackSize,
                    UnitDisplayText = UnitDisplayTextInput,
                    PurchasePrice = purchasePrice,
                    Mrp = mrp,
                    SellingPrice = sellingPrice,
                    WholesalePrice = wholesalePrice,
                    DefaultDiscountPercent = discountPercent,
                    GstRatePercent = gstRatePercent,
                    HsnCode = HsnCode,
                    PricingType = SelectedPricingType,
                    TracksBatches = TracksBatches,
                    MinimumStock = minimumStock,
                    ReorderQuantity = reorderQuantity,
                    ReplenishmentEnabled = ReplenishmentEnabled,
                    PreferredSupplierId = SelectedPreferredSupplier?.Id,
                    OpeningStock = openingStock,
                    PerformedByUserId = _owner.CurrentUserId,
                });

                if (TracksBatches && ExpiryDate is { } selectedExpiry)
                {
                    await _owner.AddBatchAsync(savedProduct.Id, $"OPENING-{savedProduct.ProductCode}", null,
                        DateOnly.FromDateTime(selectedExpiry.Date), openingStock,
                        savedProduct.PurchasePrice, savedProduct.SellingPrice);
                }
            }

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static bool TryParseDecimal(string? text, out decimal value) =>
        decimal.TryParse(text, out value);

    private static bool TryParseOptionalDecimal(string? text, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (decimal.TryParse(text, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
