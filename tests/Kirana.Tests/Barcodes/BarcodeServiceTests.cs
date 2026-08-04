using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Barcodes;

public class BarcodeServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BarcodeService _sut;

    public BarcodeServiceTests()
    {
        var sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        var auditLogger = new EfAuditLogger(_fixture.Context);
        _sut = new BarcodeService(_fixture.Context, sequenceGenerator, auditLogger);
    }

    private async Task<Product> SeedProductAsync(string? barcode = null)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Test Product",
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
    public void DetermineSymbology_ReturnsEan13_ForValidCheckDigit()
    {
        Assert.Equal(BarcodeSymbology.Ean13, _sut.DetermineSymbology("4006381333931"));
    }

    [Fact]
    public void DetermineSymbology_ReturnsCode128_ForNonEan13Value()
    {
        Assert.Equal(BarcodeSymbology.Code128, _sut.DetermineSymbology("ABC-12345"));
    }

    [Fact]
    public void DetermineSymbology_ReturnsCode128_ForThirteenDigitsWithWrongCheckDigit()
    {
        // Not a valid GS1 checksum, but still a perfectly usable CODE128 value — must not throw.
        Assert.Equal(BarcodeSymbology.Code128, _sut.DetermineSymbology("1112223334445"));
    }

    [Fact]
    public void ValidateFormat_Throws_WhenEmpty()
    {
        Assert.Throws<ArgumentException>(() => _sut.ValidateFormat("   "));
    }

    [Fact]
    public void ValidateFormat_Throws_WhenTooLong()
    {
        Assert.Throws<ArgumentException>(() => _sut.ValidateFormat(new string('1', 49)));
    }

    [Fact]
    public void ValidateFormat_Throws_ForNonPrintableCharacters()
    {
        Assert.Throws<ArgumentException>(() => _sut.ValidateFormat("BAR\t001"));
    }

    [Fact]
    public void ValidateFormat_DoesNotThrow_ForInvalidEan13Checksum()
    {
        // Wrong check digit is fine for storage — it's just treated as CODE128.
        var exception = Record.Exception(() => _sut.ValidateFormat("1112223334445"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task EnsureAvailableAsync_Throws_WhenBarcodeAlreadyUsed()
    {
        await SeedProductAsync("DUPLICATE-BAR");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnsureAvailableAsync("DUPLICATE-BAR", excludingProductId: null));
    }

    [Fact]
    public async Task EnsureAvailableAsync_DoesNotThrow_WhenExcludingTheOwningProduct()
    {
        var product = await SeedProductAsync("OWN-BAR");

        var exception = await Record.ExceptionAsync(
            () => _sut.EnsureAvailableAsync("OWN-BAR", excludingProductId: product.Id));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GenerateInternalBarcodeAsync_ProducesValidEan13()
    {
        var barcode = await _sut.GenerateInternalBarcodeAsync();

        Assert.Equal(13, barcode.Length);
        Assert.True(Ean13.IsValid(barcode));
        Assert.StartsWith("20", barcode);
    }

    [Fact]
    public async Task GenerateInternalBarcodeAsync_ProducesSequentiallyUniqueValues()
    {
        var first = await _sut.GenerateInternalBarcodeAsync();
        var second = await _sut.GenerateInternalBarcodeAsync();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task AssignBarcodeAsync_GeneratesInternalBarcode_WhenNoneProvided()
    {
        var product = await SeedProductAsync();

        var updated = await _sut.AssignBarcodeAsync(product.Id, explicitBarcode: null, performedByUserId: null);

        Assert.NotNull(updated.Barcode);
        Assert.True(Ean13.IsValid(updated.Barcode!));
    }

    [Fact]
    public async Task AssignBarcodeAsync_UsesExplicitBarcode_WhenProvided()
    {
        var product = await SeedProductAsync();

        var updated = await _sut.AssignBarcodeAsync(product.Id, "MANUAL-CODE-1", performedByUserId: null);

        Assert.Equal("MANUAL-CODE-1", updated.Barcode);
    }

    [Fact]
    public async Task AssignBarcodeAsync_Throws_WhenExplicitBarcodeAlreadyInUse()
    {
        await SeedProductAsync("TAKEN-BARCODE");
        var second = await SeedProductAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignBarcodeAsync(second.Id, "TAKEN-BARCODE", performedByUserId: null));
    }

    [Fact]
    public async Task AssignBarcodeAsync_WritesAuditLog()
    {
        var product = await SeedProductAsync();

        await _sut.AssignBarcodeAsync(product.Id, "AUDIT-TEST", performedByUserId: null);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodeAssigned"));
    }

    [Fact]
    public async Task AssignBarcodeAsync_Throws_WhenProductNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignBarcodeAsync(999, null, performedByUserId: null));
    }

    public void Dispose() => _fixture.Dispose();
}
