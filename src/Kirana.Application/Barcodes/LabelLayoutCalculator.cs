namespace Kirana.Application.Barcodes;

/// <summary>
/// Pure layout math for barcode label sheets (PRD §16): how many labels fit on a page, how to
/// split a flat list of labels into pages, and mm↔DIP conversion for the label-size fields.
/// Kept UI-framework-free so it's unit-testable; Kirana.App builds the actual print pages
/// (WinUI <c>UIElement</c>s) from the grid dimensions this returns.
/// </summary>
public static class LabelLayoutCalculator
{
    private const double DipsPerInch = 96.0;
    private const double MillimetersPerInch = 25.4;

    public static double MillimetersToDips(double millimeters) => millimeters / MillimetersPerInch * DipsPerInch;

    /// <summary>How many whole labels fit across/down a page of the given size.</summary>
    public static (int Columns, int Rows) CalculateGrid(double pageWidth, double pageHeight, double labelWidth, double labelHeight)
    {
        if (labelWidth <= 0 || labelHeight <= 0)
        {
            throw new ArgumentException("Label dimensions must be positive.");
        }

        var columns = Math.Max(1, (int)Math.Floor(pageWidth / labelWidth));
        var rows = Math.Max(1, (int)Math.Floor(pageHeight / labelHeight));
        return (columns, rows);
    }

    /// <summary>Splits a flat list into fixed-size pages, in order.</summary>
    public static IReadOnlyList<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int chunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentException("Chunk size must be positive.", nameof(chunkSize));
        }

        var pages = new List<IReadOnlyList<T>>();
        for (var i = 0; i < items.Count; i += chunkSize)
        {
            pages.Add(items.Skip(i).Take(chunkSize).ToList());
        }

        return pages;
    }

    /// <summary>Repeats each item by its requested quantity, preserving order — e.g. turning
    /// "Product A ×3, Product B ×1" into the 4 individual label instances to print.</summary>
    public static IReadOnlyList<T> ExpandByQuantity<T>(IEnumerable<(T Item, int Quantity)> itemsWithQuantity)
    {
        var expanded = new List<T>();
        foreach (var (item, quantity) in itemsWithQuantity)
        {
            for (var i = 0; i < quantity; i++)
            {
                expanded.Add(item);
            }
        }

        return expanded;
    }
}
