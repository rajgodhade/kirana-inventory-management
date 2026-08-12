using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// One selectable barcode (Phase 13B) — used by the products grid summary and by the label
/// dialog's "which code should this label carry?" picker. A flattened snapshot rather than the
/// entity itself, so the UI never holds a tracked EF instance.
/// </summary>
public sealed class ProductBarcodeOption
{
    public required int Id { get; init; }
    public required string Value { get; init; }
    public required BarcodeSymbology Symbology { get; init; }
    public required bool IsPrimary { get; init; }
    public required bool IsActive { get; init; }

    public string SymbologyLabel => Symbology == BarcodeSymbology.Ean13 ? "EAN-13" : "CODE128";
    public string RoleLabel => IsPrimary ? "Primary" : "Alternate";

    /// <summary>What the picker shows: "8901030811127 · EAN-13 · Primary".</summary>
    public string DisplayText => $"{Value} · {SymbologyLabel} · {RoleLabel}";

    public static ProductBarcodeOption From(ProductBarcode barcode) => new()
    {
        Id = barcode.Id,
        Value = barcode.Value,
        Symbology = barcode.Symbology,
        IsPrimary = barcode.IsPrimary,
        IsActive = barcode.IsActive,
    };
}
