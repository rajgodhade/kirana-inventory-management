using Kirana.Application.Barcodes;

namespace Kirana.Tests.Barcodes;

public class LabelLayoutCalculatorTests
{
    [Fact]
    public void CalculateGrid_ReturnsExpectedColumnsAndRows()
    {
        // A4-ish page 816x1056 DIP, 3x1in labels (288x96 DIP) -> 2 columns, 11 rows.
        var (columns, rows) = LabelLayoutCalculator.CalculateGrid(816, 1056, 288, 96);

        Assert.Equal(2, columns);
        Assert.Equal(11, rows);
    }

    [Fact]
    public void CalculateGrid_NeverReturnsLessThanOne_WhenLabelLargerThanPage()
    {
        var (columns, rows) = LabelLayoutCalculator.CalculateGrid(100, 100, 500, 500);

        Assert.Equal(1, columns);
        Assert.Equal(1, rows);
    }

    [Fact]
    public void CalculateGrid_Throws_ForNonPositiveLabelDimensions()
    {
        Assert.Throws<ArgumentException>(() => LabelLayoutCalculator.CalculateGrid(800, 1000, 0, 100));
        Assert.Throws<ArgumentException>(() => LabelLayoutCalculator.CalculateGrid(800, 1000, 100, -1));
    }

    [Fact]
    public void Chunk_SplitsIntoFixedSizePages_PreservingOrder()
    {
        var items = new List<int> { 1, 2, 3, 4, 5, 6, 7 };

        var pages = LabelLayoutCalculator.Chunk(items, 3);

        Assert.Equal(3, pages.Count);
        Assert.Equal([1, 2, 3], pages[0]);
        Assert.Equal([4, 5, 6], pages[1]);
        Assert.Equal([7], pages[2]);
    }

    [Fact]
    public void Chunk_Throws_ForNonPositiveChunkSize()
    {
        Assert.Throws<ArgumentException>(() => LabelLayoutCalculator.Chunk(new List<int> { 1 }, 0));
    }

    [Fact]
    public void ExpandByQuantity_RepeatsEachItemInOrder()
    {
        var expanded = LabelLayoutCalculator.ExpandByQuantity(new[]
        {
            ("A", 2),
            ("B", 1),
            ("C", 3),
        });

        Assert.Equal(["A", "A", "B", "C", "C", "C"], expanded);
    }

    [Fact]
    public void MillimetersToDips_ConvertsUsing96DpiStandard()
    {
        // 25.4mm == 1 inch == 96 DIPs.
        var dips = LabelLayoutCalculator.MillimetersToDips(25.4);

        Assert.Equal(96.0, dips, precision: 6);
    }
}
