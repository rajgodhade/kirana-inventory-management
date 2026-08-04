using Kirana.Application.Printing;

namespace Kirana.Tests.Printing;

public class InvoiceLayoutCalculatorTests
{
    [Theory]
    [InlineData(InvoiceFormat.Thermal58mm, 58)]
    [InlineData(InvoiceFormat.Thermal80mm, 80)]
    [InlineData(InvoiceFormat.A4, 210)]
    public void GetPageWidthMillimeters_ReturnsExpectedWidth(InvoiceFormat format, double expectedMm)
    {
        Assert.Equal(expectedMm, InvoiceLayoutCalculator.GetPageWidthMillimeters(format));
    }

    [Theory]
    [InlineData("58mm", InvoiceFormat.Thermal58mm)]
    [InlineData("80mm", InvoiceFormat.Thermal80mm)]
    [InlineData("A4", InvoiceFormat.A4)]
    [InlineData(null, InvoiceFormat.Thermal80mm)]
    [InlineData("unknown", InvoiceFormat.Thermal80mm)]
    public void ParseFormat_MapsStoreStringToFormat_DefaultingTo80mm(string? storeFormat, InvoiceFormat expected)
    {
        Assert.Equal(expected, InvoiceLayoutCalculator.ParseFormat(storeFormat));
    }

    [Theory]
    [InlineData(InvoiceFormat.Thermal58mm, "58mm")]
    [InlineData(InvoiceFormat.Thermal80mm, "80mm")]
    [InlineData(InvoiceFormat.A4, "A4")]
    public void ToStoreFormatString_RoundTripsWithParseFormat(InvoiceFormat format, string expected)
    {
        var stored = InvoiceLayoutCalculator.ToStoreFormatString(format);

        Assert.Equal(expected, stored);
        Assert.Equal(format, InvoiceLayoutCalculator.ParseFormat(stored));
    }

    [Fact]
    public void MillimetersToDips_ConvertsUsing96DipsPerInch()
    {
        var dips = InvoiceLayoutCalculator.MillimetersToDips(25.4);

        Assert.Equal(96, dips, precision: 5);
    }

    [Fact]
    public void Chunk_SplitsIntoPagesOfRequestedSize()
    {
        var items = Enumerable.Range(1, 7).ToList();

        var pages = InvoiceLayoutCalculator.Chunk(items, 3);

        Assert.Equal(3, pages.Count);
        Assert.Equal([1, 2, 3], pages[0]);
        Assert.Equal([4, 5, 6], pages[1]);
        Assert.Equal([7], pages[2]);
    }

    [Fact]
    public void Chunk_ReturnsSingleEmptyPage_WhenNoItems()
    {
        var pages = InvoiceLayoutCalculator.Chunk(new List<int>(), 5);

        var page = Assert.Single(pages);
        Assert.Empty(page);
    }

    [Fact]
    public void Chunk_Throws_ForNonPositiveLinesPerPage()
    {
        Assert.Throws<ArgumentException>(() => InvoiceLayoutCalculator.Chunk([1, 2, 3], 0));
    }
}
