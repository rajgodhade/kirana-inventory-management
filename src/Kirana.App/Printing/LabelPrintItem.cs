using Microsoft.UI.Xaml.Media.Imaging;

namespace Kirana.App.Printing;

/// <summary>Everything needed to draw one physical label — product name, barcode value,
/// Product ID/SKU, MRP, and selling price (PRD §16) — plus its pre-rendered barcode bitmap so
/// the same <see cref="WriteableBitmap"/> can back both the on-screen preview and every printed
/// copy without re-rendering.</summary>
public sealed class LabelPrintItem
{
    public required string ProductName { get; init; }
    public required string CodeOrSku { get; init; }
    public required string BarcodeValue { get; init; }
    public required decimal Mrp { get; init; }
    public required decimal SellingPrice { get; init; }
    public required WriteableBitmap BarcodeImage { get; init; }
}
