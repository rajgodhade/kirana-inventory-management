using System.Globalization;
using Kirana.Domain.Barcodes;

namespace Kirana.Tests.Barcodes;

public class BarcodeNormalizerTests
{
    [Fact]
    public void Normalize_TrimsSurroundingWhitespace()
    {
        Assert.Equal("8901030811127", BarcodeNormalizer.Normalize("  8901030811127 "));
    }

    [Fact]
    public void Normalize_UppercasesLetters()
    {
        Assert.Equal("ABC123", BarcodeNormalizer.Normalize("abc123"));
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var once = BarcodeNormalizer.Normalize(" abc123 ");
        Assert.Equal(once, BarcodeNormalizer.Normalize(once));
    }

    [Fact]
    public void Normalize_LeavesInternalSpacingIntact()
    {
        // ValidateFormat permits any printable ASCII, so collapsing characters here would make two
        // visibly different codes collide.
        Assert.Equal("AB C", BarcodeNormalizer.Normalize("ab c"));
    }

    [Fact]
    public void Normalize_IsCultureIndependent()
    {
        // A culture-sensitive ToUpper() maps 'i' to 'İ' (U+0130) under tr-TR, which would make the
        // stored uniqueness key depend on the machine's regional settings.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("I", BarcodeNormalizer.Normalize("i"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
