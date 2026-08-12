using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Barcodes;

public class BarcodeLookupServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BarcodeLookupService _sut;

    public BarcodeLookupServiceTests()
    {
        _sut = new BarcodeLookupService(_fixture.Context);
    }

    /// <summary>Seeds a product whose barcodes are given as (value, isActive) pairs; the first is primary.</summary>
    private async Task<Product> SeedProductAsync(
        bool productActive = true, params (string Value, bool IsActive)[] barcodes)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Scanned Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            IsActive = productActive,
        };

        for (var i = 0; i < barcodes.Length; i++)
        {
            product.Barcodes.Add(new ProductBarcode
            {
                Value = barcodes[i].Value,
                NormalizedValue = BarcodeNormalizer.Normalize(barcodes[i].Value),
                Symbology = BarcodeSymbology.Code128,
                IsPrimary = i == 0,
                IsActive = barcodes[i].IsActive,
            });
        }

        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    // ---- POS lookup ----

    [Fact]
    public async Task LookupAsync_FindsProduct_ByExactBarcode()
    {
        var product = await SeedProductAsync(true, ("8901030826501", true));

        var found = await _sut.LookupAsync("8901030826501");

        Assert.NotNull(found);
        Assert.Equal(product.Id, found!.Id);
    }

    /// <summary>The core Phase 13B promise: every one of a product's codes resolves to that same product.</summary>
    [Fact]
    public async Task LookupAsync_ResolvesEveryAlternateBarcode_ToTheSameProduct()
    {
        var product = await SeedProductAsync(
            true, ("8901030826501", true), ("5012345678900", true), ("INTERNAL-77", true));

        foreach (var code in new[] { "8901030826501", "5012345678900", "INTERNAL-77" })
        {
            var found = await _sut.LookupAsync(code);
            Assert.Equal(product.Id, found?.Id);
        }
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenNoMatch()
    {
        await SeedProductAsync(true, ("8901030826501", true));

        var found = await _sut.LookupAsync("0000000000000");

        Assert.Null(found);
    }

    [Fact]
    public async Task LookupAsync_TrimsWhitespace_FromScannerInput()
    {
        await SeedProductAsync(true, ("8901030826501", true));

        var found = await _sut.LookupAsync("  8901030826501  ");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task LookupAsync_IsCaseInsensitive()
    {
        await SeedProductAsync(true, ("abc-code-9", true));

        Assert.NotNull(await _sut.LookupAsync("ABC-CODE-9"));
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_ForEmptyInput()
    {
        var found = await _sut.LookupAsync("");

        Assert.Null(found);
    }

    /// <summary>Phase 13B decision 3: billing skips retired barcodes.</summary>
    [Fact]
    public async Task LookupAsync_SkipsRetiredBarcode()
    {
        await SeedProductAsync(true, ("STILL-GOOD", true), ("RETIRED-CODE", false));

        Assert.Null(await _sut.LookupAsync("RETIRED-CODE"));
    }

    [Fact]
    public async Task LookupAsync_StillFindsProduct_ViaItsRemainingActiveBarcode()
    {
        var product = await SeedProductAsync(true, ("STILL-GOOD", true), ("RETIRED-CODE", false));

        Assert.Equal(product.Id, (await _sut.LookupAsync("STILL-GOOD"))?.Id);
    }

    /// <summary>...and skips inactive products, even when the barcode itself is active.</summary>
    [Fact]
    public async Task LookupAsync_SkipsInactiveProduct()
    {
        await SeedProductAsync(productActive: false, ("DISCONTINUED-1", true));

        Assert.Null(await _sut.LookupAsync("DISCONTINUED-1"));
    }

    // ---- Diagnostic lookup (management / scan-test screens) ----

    [Fact]
    public async Task LookupDiagnosticAsync_ReportsPrimaryActiveBarcode_AsScannable()
    {
        await SeedProductAsync(true, ("PRIMARY-1", true));

        var diagnostic = await _sut.LookupDiagnosticAsync("PRIMARY-1");

        Assert.NotNull(diagnostic);
        Assert.True(diagnostic!.IsPrimary);
        Assert.True(diagnostic.WouldScanAtPos);
        Assert.Null(diagnostic.BlockedReason);
        Assert.Equal("Primary", diagnostic.RoleText);
    }

    [Fact]
    public async Task LookupDiagnosticAsync_ReportsAlternateRole()
    {
        await SeedProductAsync(true, ("PRIMARY-1", true), ("ALTERNATE-1", true));

        var diagnostic = await _sut.LookupDiagnosticAsync("ALTERNATE-1");

        Assert.False(diagnostic!.IsPrimary);
        Assert.Equal("Alternate", diagnostic.RoleText);
        Assert.True(diagnostic.WouldScanAtPos);
    }

    /// <summary>The payoff of decision 3 — a retired code reports what it is instead of "not found".</summary>
    [Fact]
    public async Task LookupDiagnosticAsync_FindsRetiredBarcode_AndExplainsWhyItWontScan()
    {
        await SeedProductAsync(true, ("STILL-GOOD", true), ("RETIRED-CODE", false));

        var diagnostic = await _sut.LookupDiagnosticAsync("RETIRED-CODE");

        Assert.NotNull(diagnostic);
        Assert.False(diagnostic!.WouldScanAtPos);
        Assert.Equal("Retired", diagnostic.StatusText);
        Assert.Contains("retired", diagnostic.BlockedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LookupDiagnosticAsync_FindsBarcodeOnInactiveProduct_AndBlamesTheProduct()
    {
        await SeedProductAsync(productActive: false, ("DISCONTINUED-1", true));

        var diagnostic = await _sut.LookupDiagnosticAsync("DISCONTINUED-1");

        Assert.NotNull(diagnostic);
        Assert.False(diagnostic!.WouldScanAtPos);
        Assert.False(diagnostic.IsProductActive);
        Assert.Contains("product", diagnostic.BlockedReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LookupDiagnosticAsync_ReturnsNull_WhenTheCodeIsGenuinelyUnknown()
    {
        await SeedProductAsync(true, ("KNOWN-1", true));

        Assert.Null(await _sut.LookupDiagnosticAsync("TRULY-UNKNOWN"));
    }

    public void Dispose() => _fixture.Dispose();
}
