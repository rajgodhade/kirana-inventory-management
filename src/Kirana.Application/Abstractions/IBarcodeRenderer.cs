using Kirana.Domain.Barcodes;

namespace Kirana.Application.Abstractions;

/// <summary>
/// Raw pixel data for a rendered barcode symbol: BGRA32, top-down, stride = <see cref="PixelWidth"/> * 4.
/// UI-framework-agnostic on purpose so Kirana.Application/Infrastructure never need a WinUI reference —
/// the App layer copies these bytes into a WriteableBitmap for display/printing.
/// </summary>
public sealed record BarcodeRenderResult(byte[] Bgra32Pixels, int PixelWidth, int PixelHeight);

/// <summary>
/// Renders a barcode value to a bitmap (PRD §16). Implemented in Kirana.Infrastructure using
/// ZXing.Net so barcode symbol generation stays out of WinUI code-behind.
/// </summary>
public interface IBarcodeRenderer
{
    BarcodeRenderResult Render(string value, BarcodeSymbology symbology, int pixelWidth, int pixelHeight);
}
