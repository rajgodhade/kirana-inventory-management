namespace Kirana.Domain.Barcodes;

/// <summary>Barcode symbologies supported for label rendering/printing (PRD §16-17).</summary>
public enum BarcodeSymbology
{
    /// <summary>13-digit numeric barcode with a valid GS1 check digit.</summary>
    Ean13,

    /// <summary>General-purpose alphanumeric barcode used for anything that isn't a valid EAN-13.</summary>
    Code128,
}
