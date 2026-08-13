using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Barcodes;

/// <summary>
/// Phase 13B gave <see cref="BarcodeService"/> an <see cref="IPermissionEnforcer"/>, closing a real
/// pre-existing gap: it had no permission check at all, while the barcode-label dialog could reach a
/// persisting write gated only by UI state. Every mutation now requires
/// <see cref="PermissionKeys.ProductsEdit"/> — a Cashier can scan and print, but cannot re-point a
/// barcode at a different product.
/// <para>The pure format/symbology helpers stay permission-free on purpose: they run per keystroke
/// for the live preview and touch no data.</para>
/// </summary>
public class BarcodeAuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BarcodeService _sut;
    private readonly int _ownerId;
    private readonly int _cashierId;
    private readonly int _productId;
    private readonly int _barcodeId;

    public BarcodeAuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _cashierId = _fixture.SeedCashierAsync().GetAwaiter().GetResult().Id;

        _sut = new BarcodeService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context));

        var product = new Product
        {
            ProductCode = "PRD-BGATE1",
            Name = "Gated Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            IsActive = true,
            Barcodes =
            {
                new ProductBarcode
                {
                    Value = "GATED-CODE",
                    NormalizedValue = "GATED-CODE",
                    Symbology = BarcodeSymbology.Code128,
                    IsPrimary = true,
                    IsActive = true,
                },
            },
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.SaveChanges();

        _productId = product.Id;
        _barcodeId = product.Barcodes.First().Id;
    }

    [Fact]
    public async Task AddBarcodeAsync_RequiresProductsEdit()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AddBarcodeAsync(_productId, "NEW-CODE", makePrimary: false, _cashierId));
    }

    [Fact]
    public async Task UpdateBarcodeValueAsync_RequiresProductsEdit()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.UpdateBarcodeValueAsync(_barcodeId, "CHANGED-CODE", _cashierId));
    }

    [Fact]
    public async Task SetPrimaryAsync_RequiresProductsEdit()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.SetPrimaryAsync(_barcodeId, _cashierId));
    }

    [Fact]
    public async Task SetBarcodeActiveAsync_RequiresProductsEdit()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.SetBarcodeActiveAsync(_barcodeId, isActive: false, _cashierId));
    }

    [Fact]
    public async Task RemoveBarcodeAsync_RequiresProductsEdit()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.RemoveBarcodeAsync(_barcodeId, _cashierId));
    }

    /// <summary>This is the gap that prompted adding the enforcer: the label dialog's "Generate"
    /// button persists a new barcode, and was previously reachable by anyone who could open it.</summary>
    [Fact]
    public async Task AssignBarcodeAsync_RequiresProductsEdit()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AssignBarcodeAsync(_productId, "GENERATED-CODE", _cashierId));
    }

    [Fact]
    public async Task DeniedMutation_LeavesNoPartialChange()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AddBarcodeAsync(_productId, "NEW-CODE", makePrimary: true, _cashierId));

        // Neither the rejected barcode nor a demoted primary may survive the refusal.
        Assert.False(await _fixture.Context.ProductBarcodes.AnyAsync(b => b.Value == "NEW-CODE"));
        var primary = Assert.Single(
            await _fixture.Context.ProductBarcodes.Where(b => b.ProductId == _productId && b.IsPrimary).ToListAsync());
        Assert.Equal("GATED-CODE", primary.Value);
    }

    [Fact]
    public async Task OwnerCanPerformEveryMutation()
    {
        var added = await _sut.AddBarcodeAsync(_productId, "OWNER-CODE", makePrimary: false, _ownerId);
        await _sut.SetPrimaryAsync(added.Id, _ownerId);
        await _sut.UpdateBarcodeValueAsync(added.Id, "OWNER-CODE-2", _ownerId);
        await _sut.SetBarcodeActiveAsync(added.Id, isActive: false, _ownerId);
        await _sut.SetBarcodeActiveAsync(added.Id, isActive: true, _ownerId);
        await _sut.RemoveBarcodeAsync(added.Id, _ownerId);

        Assert.False(await _fixture.Context.ProductBarcodes.AnyAsync(b => b.Id == added.Id));
    }

    /// <summary>Reading is not gated here — the POS scan path and the label dialog both need it, and
    /// a Cashier scanning a code is the normal case, not a privileged one.</summary>
    [Fact]
    public async Task GetForProductAsync_IsReadableWithoutProductsEdit()
    {
        var barcodes = await _sut.GetForProductAsync(_productId);

        Assert.Single(barcodes);
    }

    [Fact]
    public void FormatHelpers_AreUsableWithoutAnyUser()
    {
        // Per-keystroke live preview: no user context exists at this point.
        Assert.Equal(BarcodeSymbology.Ean13, _sut.DetermineSymbology("4006381333931"));
        Assert.True(_sut.IsValidEan13("4006381333931"));
        _sut.ValidateFormat("ABC-123");
    }

    public void Dispose() => _fixture.Dispose();
}
