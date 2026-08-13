using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Barcodes;

/// <summary>
/// One scan must add exactly ONE unit — the invariant Phase 13B is most likely to break, because a
/// product now has several codes that all resolve to it and a naive "match any barcode" join can
/// return one row per barcode.
/// <para>Also pins the scanner double-add fix that <c>PosShellPage.xaml.cs</c> and
/// <c>PurchaseEntryPage.xaml.cs</c> depend on: <see cref="IScannerInputBuffer.OnEnterPressed"/>
/// reports whether the burst was a scan, and callers must not ALSO run a manual search on that same
/// Enter. Those two files are deliberately untouched by this phase; this test is what keeps them
/// correct as the barcode model changes underneath them.</para>
/// </summary>
public class PosScanQuantityRegressionTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BarcodeLookupService _lookup;

    public PosScanQuantityRegressionTests()
    {
        _lookup = new BarcodeLookupService(_fixture.Context);
    }

    private async Task<Product> SeedProductAsync(params string[] barcodes)
    {
        var product = new Product
        {
            ProductCode = "PRD-SCAN01",
            Name = "Multi-Coded Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            IsActive = true,
        };

        for (var i = 0; i < barcodes.Length; i++)
        {
            product.Barcodes.Add(new ProductBarcode
            {
                Value = barcodes[i],
                NormalizedValue = BarcodeNormalizer.Normalize(barcodes[i]),
                Symbology = BarcodeSymbology.Code128,
                IsPrimary = i == 0,
                IsActive = true,
            });
        }

        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100 });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    /// <summary>Feeds a barcode through the buffer exactly as a USB scanner does: a fast character
    /// burst terminated by Enter. Returns the products the cart would have received.</summary>
    private async Task<List<Product>> SimulateScanAsync(IScannerInputBuffer buffer, string barcode)
    {
        var added = new List<Product>();
        var scanned = new List<string>();

        void OnScan(string value) => scanned.Add(value);
        buffer.BarcodeScanned += OnScan;

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < barcode.Length; i++)
        {
            // ~5ms apart: far faster than a human, which is how the buffer recognizes a scanner.
            buffer.OnCharacter(barcode[i], now.AddMilliseconds(i * 5));
        }

        var handledAsScan = buffer.OnEnterPressed(now.AddMilliseconds(barcode.Length * 5));
        buffer.BarcodeScanned -= OnScan;

        foreach (var value in scanned)
        {
            var product = await _lookup.LookupAsync(value);
            if (product is not null) added.Add(product);
        }

        // This mirrors the guard in PosShellPage/PurchaseEntryPage: when the buffer reports a scan,
        // the caller must NOT also treat the Enter as a manual search submission.
        if (!handledAsScan)
        {
            var product = await _lookup.LookupAsync(barcode);
            if (product is not null) added.Add(product);
        }

        return added;
    }

    [Fact]
    public async Task ScanningTheProductsOnlyBarcode_AddsExactlyOneUnit()
    {
        var product = await SeedProductAsync("8901030826501");

        var added = await SimulateScanAsync(new ScannerInputBuffer(), "8901030826501");

        Assert.Equal(product.Id, Assert.Single(added).Id);
    }

    /// <summary>The Phase 13B risk case: three codes on one product must still add one unit each.</summary>
    [Theory]
    [InlineData("8901030826501")]
    [InlineData("5012345678900")]
    [InlineData("INTERNAL-77")]
    public async Task ScanningAnyAlternateBarcode_AddsExactlyOneUnitOfTheSameProduct(string barcode)
    {
        var product = await SeedProductAsync("8901030826501", "5012345678900", "INTERNAL-77");

        var added = await SimulateScanAsync(new ScannerInputBuffer(), barcode);

        Assert.Equal(product.Id, Assert.Single(added).Id);
    }

    [Fact]
    public async Task ScanningEveryBarcodeInTurn_AddsOneUnitPerScan_NotOnePerBarcodeOnTheProduct()
    {
        var product = await SeedProductAsync("CODE-A", "CODE-B", "CODE-C");
        var buffer = new ScannerInputBuffer();
        var total = new List<Product>();

        foreach (var code in new[] { "CODE-A", "CODE-B", "CODE-C" })
        {
            total.AddRange(await SimulateScanAsync(buffer, code));
        }

        // Three scans → three units, all of the same product. A join that fanned out across the
        // product's barcodes would produce nine.
        Assert.Equal(3, total.Count);
        Assert.All(total, p => Assert.Equal(product.Id, p.Id));
    }

    [Fact]
    public async Task ScanningARetiredBarcode_AddsNothing()
    {
        var product = await SeedProductAsync("ACTIVE-CODE", "RETIRED-CODE");
        var retired = product.Barcodes.Single(b => b.Value == "RETIRED-CODE");
        retired.IsActive = false;
        await _fixture.Context.SaveChangesAsync();

        var added = await SimulateScanAsync(new ScannerInputBuffer(), "RETIRED-CODE");

        Assert.Empty(added);
    }

    /// <summary>Typing slowly is NOT a scan: the buffer must report false so the caller runs its
    /// manual search instead — the other half of the double-add guard.</summary>
    [Fact]
    public async Task SlowlyTypedBarcodeThenEnter_IsHandledOnceAsAManualSearch()
    {
        var product = await SeedProductAsync("8901030826501");
        var buffer = new ScannerInputBuffer();
        var scanned = new List<string>();
        buffer.BarcodeScanned += scanned.Add;

        var now = DateTimeOffset.UtcNow;
        const string barcode = "8901030826501";
        for (var i = 0; i < barcode.Length; i++)
        {
            // 300ms apart: human typing speed.
            buffer.OnCharacter(barcode[i], now.AddMilliseconds(i * 300));
        }

        var handledAsScan = buffer.OnEnterPressed(now.AddMilliseconds(barcode.Length * 300));

        Assert.False(handledAsScan);
        Assert.Empty(scanned);

        // The caller therefore performs exactly one manual lookup — one unit, not two.
        Assert.Equal(product.Id, (await _lookup.LookupAsync(barcode))!.Id);
    }

    public void Dispose() => _fixture.Dispose();
}
