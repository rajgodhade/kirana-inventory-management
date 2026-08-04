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

    [ObservableProperty]
    private string? _barcode;

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private WriteableBitmap? _barcodeImage;

    [ObservableProperty]
    private bool _isGenerating;
}
