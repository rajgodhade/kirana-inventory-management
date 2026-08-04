using Kirana.Application.Abstractions;
using Kirana.Domain.Barcodes;
using ZXing;
using ZXing.Common;

namespace Kirana.Infrastructure.Barcodes;

/// <summary>
/// Renders CODE128/EAN-13 symbols via ZXing.Net's platform-agnostic pixel writer (PRD §16-17).
/// <see cref="ZXing.Rendering.PixelData.Pixels"/> is already BGRA32, top-down — the same layout
/// a WinUI <c>WriteableBitmap</c> expects, so the App layer can copy it straight into one.
/// </summary>
public sealed class ZXingBarcodeRenderer : IBarcodeRenderer
{
    public BarcodeRenderResult Render(string value, BarcodeSymbology symbology, int pixelWidth, int pixelHeight)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = symbology == BarcodeSymbology.Ean13 ? BarcodeFormat.EAN_13 : BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width = pixelWidth,
                Height = pixelHeight,
                Margin = 2,
                PureBarcode = true,
            },
        };

        var pixelData = writer.Write(value);
        return new BarcodeRenderResult(pixelData.Pixels, pixelData.Width, pixelData.Height);
    }
}
