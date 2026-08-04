namespace Kirana.Application.Printing;

/// <summary>
/// Pure layout math for invoice/receipt printing (PRD §23): page-width selection per
/// <see cref="InvoiceFormat"/> and how many item lines fit in a given content height, so a long
/// cart can be split across multiple physical pages/roll-feeds. Kept UI-framework-free so it's
/// unit-testable; Kirana.App builds the actual print pages (WinUI <c>UIElement</c>s) from the
/// numbers this returns, mirroring <see cref="Kirana.Application.Barcodes.LabelLayoutCalculator"/>.
/// </summary>
public static class InvoiceLayoutCalculator
{
    private const double DipsPerInch = 96.0;
    private const double MillimetersPerInch = 25.4;

    public static double MillimetersToDips(double millimeters) => millimeters / MillimetersPerInch * DipsPerInch;

    /// <summary>Nominal content width in millimeters for a given format — thermal rolls have a
    /// fixed physical width; A4 uses the standard portrait width (with margins applied by the
    /// caller).</summary>
    public static double GetPageWidthMillimeters(InvoiceFormat format) => format switch
    {
        InvoiceFormat.Thermal58mm => 58,
        InvoiceFormat.Thermal80mm => 80,
        InvoiceFormat.A4 => 210,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown invoice format."),
    };

    public static InvoiceFormat ParseFormat(string? storeFormat) => storeFormat switch
    {
        "58mm" => InvoiceFormat.Thermal58mm,
        "80mm" => InvoiceFormat.Thermal80mm,
        "A4" => InvoiceFormat.A4,
        _ => InvoiceFormat.Thermal80mm,
    };

    public static string ToStoreFormatString(InvoiceFormat format) => format switch
    {
        InvoiceFormat.Thermal58mm => "58mm",
        InvoiceFormat.Thermal80mm => "80mm",
        InvoiceFormat.A4 => "A4",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown invoice format."),
    };

    /// <summary>Splits a flat list of invoice lines into pages of at most <paramref name="linesPerPage"/>
    /// each, in order — used when a cart is too long to fit one physical page/roll-feed.</summary>
    public static IReadOnlyList<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int linesPerPage)
    {
        if (linesPerPage <= 0)
        {
            throw new ArgumentException("Lines per page must be positive.", nameof(linesPerPage));
        }

        if (items.Count == 0)
        {
            return [items];
        }

        var pages = new List<IReadOnlyList<T>>();
        for (var i = 0; i < items.Count; i += linesPerPage)
        {
            pages.Add(items.Skip(i).Take(linesPerPage).ToList());
        }

        return pages;
    }
}
