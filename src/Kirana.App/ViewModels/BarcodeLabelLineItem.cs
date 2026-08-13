using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kirana.App.ViewModels;

/// <summary>One row in the barcode label dialog — a single product plus how many copies to
/// print (PRD §16). The same list backs both the single-product and bulk entry points; single
/// just starts with one row.</summary>
public sealed partial class BarcodeLabelLineItem : ObservableObject
{
    public required int ProductId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductCode { get; init; }
    public string? Sku { get; init; }
    public decimal Mrp { get; init; }
    public decimal SellingPrice { get; init; }

    public string CodeOrSku => string.IsNullOrWhiteSpace(Sku) ? ProductCode : $"{ProductCode} / {Sku}";

    /// <summary>Every active code on this product (Phase 13B) — the picker's options. Products with
    /// a single barcode hide the picker entirely (see <see cref="HasBarcodeChoice"/>).</summary>
    public System.Collections.ObjectModel.ObservableCollection<ProductBarcodeOption> AvailableBarcodes { get; } = [];

    public bool HasBarcodeChoice => AvailableBarcodes.Count > 1;

    /// <summary>Which code this label carries. Defaults to the product's primary; changing it
    /// re-renders the preview so what's on screen always matches what will print.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Barcode))]
    private ProductBarcodeOption? _selectedBarcode;

    /// <summary>The value actually printed. Kept as a passthrough so the print pipeline
    /// (<c>LabelPrintItem.BarcodeValue</c>) needed no change for Phase 13B.</summary>
    public string? Barcode => SelectedBarcode?.Value;

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private WriteableBitmap? _barcodeImage;

    [ObservableProperty]
    private bool _isGenerating;
}
