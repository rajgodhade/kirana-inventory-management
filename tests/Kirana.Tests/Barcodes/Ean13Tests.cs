using Kirana.Domain.Barcodes;

namespace Kirana.Tests.Barcodes;

public class Ean13Tests
{
    [Theory]
    [InlineData("400638133393", '1')] // well-known Nivea example
    [InlineData("123456789012", '8')]
    public void ComputeCheckDigit_MatchesKnownExample(string first12, char expected)
    {
        Assert.Equal(expected, Ean13.ComputeCheckDigit(first12));
    }

    [Fact]
    public void ComputeCheckDigit_Throws_WhenNotTwelveDigits()
    {
        Assert.Throws<ArgumentException>(() => Ean13.ComputeCheckDigit("123"));
    }

    [Fact]
    public void ComputeCheckDigit_Throws_WhenNonDigitPresent()
    {
        Assert.Throws<ArgumentException>(() => Ean13.ComputeCheckDigit("40063813339A"));
    }

    [Fact]
    public void BuildWithCheckDigit_AppendsCorrectDigit()
    {
        Assert.Equal("4006381333931", Ean13.BuildWithCheckDigit("400638133393"));
    }

    [Fact]
    public void IsValid_ReturnsTrue_ForCorrectCheckDigit()
    {
        Assert.True(Ean13.IsValid("4006381333931"));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForWrongCheckDigit()
    {
        Assert.False(Ean13.IsValid("4006381333930"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("40063813339")] // 11 digits
    [InlineData("40063813339312")] // 14 digits
    [InlineData("400638133393A")] // non-digit
    public void IsValid_ReturnsFalse_ForMalformedInput(string? candidate)
    {
        Assert.False(Ean13.IsValid(candidate));
    }
}
