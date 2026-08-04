using Kirana.Application.Barcodes;
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

    private async Task<Product> SeedProductAsync(string barcode)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Scanned Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            Barcode = barcode,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task LookupAsync_FindsProduct_ByExactBarcode()
    {
        var product = await SeedProductAsync("8901030826501");

        var found = await _sut.LookupAsync("8901030826501");

        Assert.NotNull(found);
        Assert.Equal(product.Id, found!.Id);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenNoMatch()
    {
        await SeedProductAsync("8901030826501");

        var found = await _sut.LookupAsync("0000000000000");

        Assert.Null(found);
    }

    [Fact]
    public async Task LookupAsync_TrimsWhitespace_FromScannerInput()
    {
        await SeedProductAsync("8901030826501");

        var found = await _sut.LookupAsync("  8901030826501  ");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_ForEmptyInput()
    {
        var found = await _sut.LookupAsync("");

        Assert.Null(found);
    }

    public void Dispose() => _fixture.Dispose();
}
