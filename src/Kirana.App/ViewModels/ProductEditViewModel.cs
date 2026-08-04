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

    public System.Collections.ObjectModel.ObservableCollection<Category> Categories => _owner.Categories;
    public System.Collections.ObjectModel.ObservableCollection<Brand> Brands => _owner.Brands;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _sku;

    [ObservableProperty]
    private string? _barcode;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private Brand? _selectedBrand;

    [ObservableProperty]
    private UnitOfMeasure _selectedUnit = UnitOfMeasure.Piece;

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
    private bool _isTaxInclusive;

    [ObservableProperty]
    private string _minimumStockText = "0";

    [ObservableProperty]
    private string _reorderQuantityText = "0";

    [ObservableProperty]
    private bool _tracksBatches;

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
        Barcode = existing.Barcode;
        Description = existing.Description;
        SelectedCategory = owner.Categories.FirstOrDefault(c => c.Id == existing.CategoryId);
        SelectedBrand = owner.Brands.FirstOrDefault(b => b.Id == existing.BrandId);
        SelectedUnit = existing.Unit;
        PurchasePriceText = existing.PurchasePrice.ToString("0.##");
        MrpText = existing.Mrp.ToString("0.##");
        SellingPriceText = existing.SellingPrice.ToString("0.##");
        WholesalePriceText = existing.WholesalePrice?.ToString("0.##");
        DefaultDiscountPercentText = existing.DefaultDiscountPercent?.ToString("0.##");
        GstRatePercentText = existing.GstRatePercent?.ToString("0.##");
        HsnCode = existing.HsnCode;
        IsTaxInclusive = existing.IsTaxInclusive;
        MinimumStockText = existing.MinimumStock.ToString("0.###");
        ReorderQuantityText = existing.ReorderQuantity.ToString("0.###");
        TracksBatches = existing.TracksBatches;

        UpdateBarcodePreview();
    }

    partial void OnBarcodeChanged(string? value) => UpdateBarcodePreview();

    private void UpdateBarcodePreview()
    {
        if (string.IsNullOrWhiteSpace(Barcode))
        {
            BarcodePreviewImage = null;
            BarcodeStatusText = "";
            return;
        }

        try
        {
            _barcodeService.ValidateFormat(Barcode);
        }
        catch (ArgumentException ex)
        {
            BarcodePreviewImage = null;
            BarcodeStatusText = ex.Message;
            return;
        }

        var symbology = _barcodeService.DetermineSymbology(Barcode);
        BarcodeStatusText = symbology == BarcodeSymbology.Ean13 ? "Valid EAN-13" : "CODE128 (not a standard EAN-13)";

        var rendered = _barcodeRenderer.Render(Barcode, symbology, 240, 80);
        BarcodePreviewImage = ToWriteableBitmap(rendered);
    }

    [RelayCommand]
    private async Task GenerateBarcodeAsync()
    {
        ErrorMessage = null;
        IsGeneratingBarcode = true;
        try
        {
            Barcode = await _barcodeService.GenerateInternalBarcodeAsync();
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

        IsSaving = true;
        try
        {
            if (IsEditMode)
            {
                await _owner.UpdateProductAsync(_editingProductId!.Value, new UpdateProductRequest
                {
                    Name = Name,
                    Sku = Sku,
                    Barcode = Barcode,
                    Description = Description,
                    CategoryId = SelectedCategory?.Id,
                    BrandId = SelectedBrand?.Id,
                    Unit = SelectedUnit,
                    PurchasePrice = purchasePrice,
                    Mrp = mrp,
                    SellingPrice = sellingPrice,
                    WholesalePrice = wholesalePrice,
                    DefaultDiscountPercent = discountPercent,
                    GstRatePercent = gstRatePercent,
                    HsnCode = HsnCode,
                    IsTaxInclusive = IsTaxInclusive,
                    TracksBatches = TracksBatches,
                    MinimumStock = minimumStock,
                    ReorderQuantity = reorderQuantity,
                    PerformedByUserId = _owner.CurrentUserId,
                });
            }
            else
            {
                if (!TryParseDecimal(OpeningStockText, out var openingStock))
                {
                    ErrorMessage = "Opening stock must be a valid number.";
                    return;
                }

                await _owner.CreateProductAsync(new CreateProductRequest
                {
                    Name = Name,
                    Sku = Sku,
                    Barcode = Barcode,
                    Description = Description,
                    CategoryId = SelectedCategory?.Id,
                    BrandId = SelectedBrand?.Id,
                    Unit = SelectedUnit,
                    PurchasePrice = purchasePrice,
                    Mrp = mrp,
                    SellingPrice = sellingPrice,
                    WholesalePrice = wholesalePrice,
                    DefaultDiscountPercent = discountPercent,
                    GstRatePercent = gstRatePercent,
                    HsnCode = HsnCode,
                    IsTaxInclusive = IsTaxInclusive,
                    TracksBatches = TracksBatches,
                    MinimumStock = minimumStock,
                    ReorderQuantity = reorderQuantity,
                    OpeningStock = openingStock,
                    PerformedByUserId = _owner.CurrentUserId,
                });
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
