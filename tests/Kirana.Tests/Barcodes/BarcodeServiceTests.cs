using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Application.Authentication;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Barcodes;

public class BarcodeServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BarcodeService _sut;
    private int _ownerId;

    public BarcodeServiceTests()
    {
        var sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        var auditLogger = new EfAuditLogger(_fixture.Context);
        _sut = new BarcodeService(
            _fixture.Context, sequenceGenerator, auditLogger, new PermissionEnforcer(_fixture.Context));
    }

    /// <summary>Phase 13B gave BarcodeService a permission check, so mutations now need a real
    /// permitted user id rather than the null that used to pass.</summary>
    private async Task<int> OwnerIdAsync()
    {
        if (_ownerId == 0)
        {
            _ownerId = (await _fixture.SeedOwnerAsync()).Id;
        }

        return _ownerId;
    }

    private async Task<Product> SeedProductAsync(params string[] barcodes)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Test Product",
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
                Symbology = _sut.DetermineSymbology(barcodes[i]),
                IsPrimary = i == 0,
                IsActive = true,
            });
        }

        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<List<ProductBarcode>> BarcodesOfAsync(int productId) =>
        _fixture.Context.ProductBarcodes.Where(b => b.ProductId == productId).ToListAsync();

    // ---- Format / symbology (pure helpers, no permission required) ----

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

    // ---- Global uniqueness ----

    [Fact]
    public async Task EnsureAvailableAsync_Throws_WhenBarcodeAlreadyUsed()
    {
        await SeedProductAsync("DUPLICATE-BAR");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnsureAvailableAsync("DUPLICATE-BAR", excludingProductId: null));
    }

    /// <summary>Phase 13B decision 2: uniqueness is case-insensitive via the stored normalized value,
    /// resolving the old inconsistency where SQL compared case-sensitively but the importer deduped
    /// with OrdinalIgnoreCase.</summary>
    [Fact]
    public async Task EnsureAvailableAsync_Throws_ForCaseOnlyDifference()
    {
        await SeedProductAsync("abc-123");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnsureAvailableAsync("ABC-123", excludingProductId: null));
    }

    [Fact]
    public async Task EnsureAvailableAsync_Throws_ForSurroundingWhitespaceDifference()
    {
        await SeedProductAsync("SPACE-1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnsureAvailableAsync("  SPACE-1  ", excludingProductId: null));
    }

    [Fact]
    public async Task EnsureAvailableAsync_DoesNotThrow_WhenExcludingTheOwningProduct()
    {
        var product = await SeedProductAsync("OWN-BAR");

        var exception = await Record.ExceptionAsync(
            () => _sut.EnsureAvailableAsync("OWN-BAR", excludingProductId: product.Id));

        Assert.Null(exception);
    }

    /// <summary>A retired barcode keeps ownership of its value — otherwise reactivating it later
    /// could resurrect a duplicate that the unique index would then reject mid-save.</summary>
    [Fact]
    public async Task EnsureAvailableAsync_Throws_EvenWhenTheOwningBarcodeIsRetired()
    {
        var product = await SeedProductAsync("RETIRED-BAR", "ACTIVE-BAR");
        var retired = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "RETIRED-BAR");
        await _sut.SetBarcodeActiveAsync(retired.Id, isActive: false, await OwnerIdAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.EnsureAvailableAsync("RETIRED-BAR", excludingProductId: null));
    }

    [Fact]
    public async Task FindOwningProductIdAsync_ReturnsOwner_SoTheUiCanNameTheConflict()
    {
        var product = await SeedProductAsync("CONFLICT-1");

        Assert.Equal(product.Id, await _sut.FindOwningProductIdAsync("conflict-1"));
    }

    [Fact]
    public async Task FindOwningProductIdAsync_ReturnsNull_WhenBarcodeIsFree()
    {
        Assert.Null(await _sut.FindOwningProductIdAsync("NOBODY-HAS-THIS"));
    }

    // ---- Internal generation ----

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

    // ---- AddBarcodeAsync ----

    [Fact]
    public async Task AddBarcodeAsync_MakesTheFirstBarcodePrimaryAutomatically()
    {
        var product = await SeedProductAsync();

        var added = await _sut.AddBarcodeAsync(product.Id, "FIRST-CODE", makePrimary: false, await OwnerIdAsync());

        Assert.True(added.IsPrimary);
        Assert.True(added.IsActive);
    }

    [Fact]
    public async Task AddBarcodeAsync_KeepsExistingPrimary_WhenNotAskedToPromote()
    {
        var product = await SeedProductAsync("ORIGINAL");

        await _sut.AddBarcodeAsync(product.Id, "ALTERNATE", makePrimary: false, await OwnerIdAsync());

        var barcodes = await BarcodesOfAsync(product.Id);
        Assert.Equal("ORIGINAL", barcodes.Single(b => b.IsPrimary).Value);
    }

    [Fact]
    public async Task AddBarcodeAsync_PromotesAndDemotes_WhenMakePrimaryRequested()
    {
        var product = await SeedProductAsync("ORIGINAL");

        await _sut.AddBarcodeAsync(product.Id, "NEWPRIMARY", makePrimary: true, await OwnerIdAsync());

        var barcodes = await BarcodesOfAsync(product.Id);
        Assert.Equal("NEWPRIMARY", Assert.Single(barcodes, b => b.IsPrimary).Value);
    }

    [Fact]
    public async Task AddBarcodeAsync_GeneratesInternalBarcode_WhenValueOmitted()
    {
        var product = await SeedProductAsync();

        var added = await _sut.AddBarcodeAsync(product.Id, null, makePrimary: false, await OwnerIdAsync());

        Assert.True(Ean13.IsValid(added.Value));
        Assert.True(added.IsInternal);
    }

    [Fact]
    public async Task AddBarcodeAsync_StoresNormalizedValue_ForCaseInsensitiveLookup()
    {
        var product = await SeedProductAsync();

        var added = await _sut.AddBarcodeAsync(product.Id, "  mixed-Case  ", makePrimary: false, await OwnerIdAsync());

        Assert.Equal("MIXED-CASE", added.NormalizedValue);
        // The value as entered is preserved for display/printing, only trimmed.
        Assert.Equal("mixed-Case", added.Value);
    }

    [Fact]
    public async Task AddBarcodeAsync_Throws_WhenBarcodeBelongsToAnotherProduct()
    {
        await SeedProductAsync("TAKEN-BARCODE");
        var second = await SeedProductAsync();
        var ownerId = await OwnerIdAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddBarcodeAsync(second.Id, "TAKEN-BARCODE", makePrimary: false, ownerId));
    }

    [Fact]
    public async Task AddBarcodeAsync_Throws_WhenProductNotFound()
    {
        var ownerId = await OwnerIdAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AddBarcodeAsync(999, "ANY-CODE", makePrimary: false, ownerId));
    }

    [Fact]
    public async Task AddBarcodeAsync_WritesAuditLog()
    {
        var product = await SeedProductAsync();

        await _sut.AddBarcodeAsync(product.Id, "AUDIT-ADD", makePrimary: false, await OwnerIdAsync());

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodeAdded"));
    }

    // ---- One-primary-per-product invariant ----

    [Fact]
    public async Task SetPrimaryAsync_LeavesExactlyOnePrimary()
    {
        var product = await SeedProductAsync("A-CODE", "B-CODE", "C-CODE");
        var target = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "C-CODE");

        await _sut.SetPrimaryAsync(target.Id, await OwnerIdAsync());

        var barcodes = await BarcodesOfAsync(product.Id);
        Assert.Equal("C-CODE", Assert.Single(barcodes, b => b.IsPrimary).Value);
    }

    /// <summary>Promoting an OLDER barcode is the ordering hazard: SQLite checks the filtered unique
    /// index per statement and EF orders UPDATEs by primary key, so a batched demote+promote fails
    /// exactly when the new primary has the lower Id.</summary>
    [Fact]
    public async Task SetPrimaryAsync_Succeeds_WhenPromotingABarcodeOlderThanTheCurrentPrimary()
    {
        var product = await SeedProductAsync("OLDEST", "NEWER");
        var ownerId = await OwnerIdAsync();
        var barcodes = await BarcodesOfAsync(product.Id);
        var oldest = barcodes.Single(b => b.Value == "OLDEST");
        var newer = barcodes.Single(b => b.Value == "NEWER");

        await _sut.SetPrimaryAsync(newer.Id, ownerId);
        var exception = await Record.ExceptionAsync(() => _sut.SetPrimaryAsync(oldest.Id, ownerId));

        Assert.Null(exception);
        Assert.Equal("OLDEST", Assert.Single(await BarcodesOfAsync(product.Id), b => b.IsPrimary).Value);
    }

    /// <summary>Same hazard on the retire path: the auto-promoted barcode is the OLDEST remaining
    /// active one, so it reliably has a lower Id than the primary being retired.</summary>
    [Fact]
    public async Task SetBarcodeActiveAsync_AutoPromotionSucceeds_WhenPromotedBarcodeIsOlder()
    {
        var product = await SeedProductAsync("OLDEST", "NEWER");
        var ownerId = await OwnerIdAsync();
        var newer = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "NEWER");
        await _sut.SetPrimaryAsync(newer.Id, ownerId);

        var exception = await Record.ExceptionAsync(
            () => _sut.SetBarcodeActiveAsync(newer.Id, isActive: false, ownerId));

        Assert.Null(exception);
        Assert.Equal("OLDEST", Assert.Single(await BarcodesOfAsync(product.Id), b => b.IsPrimary).Value);
    }

    [Fact]
    public async Task SetPrimaryAsync_Throws_ForRetiredBarcode()
    {
        var product = await SeedProductAsync("KEEP", "RETIRE-ME");
        var target = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "RETIRE-ME");
        var ownerId = await OwnerIdAsync();
        await _sut.SetBarcodeActiveAsync(target.Id, isActive: false, ownerId);

        // A retired code can't be what label printing defaults to.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetPrimaryAsync(target.Id, ownerId));
    }

    [Fact]
    public async Task SetPrimaryAsync_WritesAuditLog()
    {
        var product = await SeedProductAsync("A-CODE", "B-CODE");
        var target = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "B-CODE");

        await _sut.SetPrimaryAsync(target.Id, await OwnerIdAsync());

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodePrimaryChanged"));
    }

    // ---- Retire / restore ----

    [Fact]
    public async Task SetBarcodeActiveAsync_RetiresWithoutDeleting_SoOldLabelsStayDiagnosable()
    {
        var product = await SeedProductAsync("KEEP", "RETIRE-ME");
        var target = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "RETIRE-ME");

        await _sut.SetBarcodeActiveAsync(target.Id, isActive: false, await OwnerIdAsync());

        var stored = await _fixture.Context.ProductBarcodes.SingleAsync(b => b.Id == target.Id);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task SetBarcodeActiveAsync_AutoPromotesOldestRemainingActive_WhenPrimaryRetired()
    {
        var product = await SeedProductAsync("PRIMARY", "SECOND", "THIRD");
        var primary = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "PRIMARY");

        await _sut.SetBarcodeActiveAsync(primary.Id, isActive: false, await OwnerIdAsync());

        var barcodes = await BarcodesOfAsync(product.Id);
        var newPrimary = Assert.Single(barcodes, b => b.IsPrimary);
        Assert.Equal("SECOND", newPrimary.Value);
        Assert.True(newPrimary.IsActive);
    }

    /// <summary>When nothing else is active the retired row keeps the primary flag, so a product with
    /// barcodes never ends up with none marked primary.</summary>
    [Fact]
    public async Task SetBarcodeActiveAsync_KeepsPrimaryFlagOnRetiredRow_WhenItWasTheOnlyBarcode()
    {
        var product = await SeedProductAsync("ONLY-CODE");
        var only = (await BarcodesOfAsync(product.Id)).Single();

        await _sut.SetBarcodeActiveAsync(only.Id, isActive: false, await OwnerIdAsync());

        var stored = await _fixture.Context.ProductBarcodes.SingleAsync(b => b.Id == only.Id);
        Assert.False(stored.IsActive);
        Assert.True(stored.IsPrimary);
    }

    /// <summary>Retiring a sole-active primary leaves the flag on the retired row, so reactivating it
    /// later could produce a second primary once another code was promoted. Reactivation restores the
    /// code, not its rank — otherwise the filtered unique index rejects the save outright.</summary>
    [Fact]
    public async Task SetBarcodeActiveAsync_DoesNotReclaimPrimary_WhenAnotherBarcodeTookItMeanwhile()
    {
        var product = await SeedProductAsync("ORIGINAL");
        var ownerId = await OwnerIdAsync();
        var original = (await BarcodesOfAsync(product.Id)).Single();

        // Retire the only barcode (keeps IsPrimary), add a replacement, then bring the old one back.
        await _sut.SetBarcodeActiveAsync(original.Id, isActive: false, ownerId);
        await _sut.AddBarcodeAsync(product.Id, "REPLACEMENT", makePrimary: true, ownerId);

        var exception = await Record.ExceptionAsync(
            () => _sut.SetBarcodeActiveAsync(original.Id, isActive: true, ownerId));

        Assert.Null(exception);
        var barcodes = await BarcodesOfAsync(product.Id);
        Assert.Equal("REPLACEMENT", Assert.Single(barcodes, b => b.IsPrimary).Value);
        Assert.True(barcodes.Single(b => b.Value == "ORIGINAL").IsActive);
    }

    [Fact]
    public async Task SetBarcodeActiveAsync_WritesDistinctAuditActions_ForRetireAndRestore()
    {
        var product = await SeedProductAsync("KEEP", "TOGGLE-ME");
        var target = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "TOGGLE-ME");

        await _sut.SetBarcodeActiveAsync(target.Id, isActive: false, await OwnerIdAsync());
        await _sut.SetBarcodeActiveAsync(target.Id, isActive: true, await OwnerIdAsync());

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodeDeactivated"));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodeReactivated"));
    }

    // ---- Update / remove ----

    [Fact]
    public async Task UpdateBarcodeValueAsync_RecomputesNormalizedValueAndSymbology()
    {
        var product = await SeedProductAsync("typo-code");
        var target = (await BarcodesOfAsync(product.Id)).Single();

        var updated = await _sut.UpdateBarcodeValueAsync(target.Id, "4006381333931", await OwnerIdAsync());

        Assert.Equal("4006381333931", updated.NormalizedValue);
        Assert.Equal(BarcodeSymbology.Ean13, updated.Symbology);
    }

    [Fact]
    public async Task UpdateBarcodeValueAsync_Throws_WhenNewValueBelongsToAnotherProduct()
    {
        await SeedProductAsync("OTHER-OWNER");
        var product = await SeedProductAsync("MINE");
        var target = (await BarcodesOfAsync(product.Id)).Single();
        var ownerId = await OwnerIdAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateBarcodeValueAsync(target.Id, "OTHER-OWNER", ownerId));
    }

    [Fact]
    public async Task RemoveBarcodeAsync_DeletesTheRow()
    {
        var product = await SeedProductAsync("KEEP", "MISTAKE");
        var target = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "MISTAKE");

        await _sut.RemoveBarcodeAsync(target.Id, await OwnerIdAsync());

        Assert.False(await _fixture.Context.ProductBarcodes.AnyAsync(b => b.Id == target.Id));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodeRemoved"));
    }

    // ---- GetForProductAsync ordering ----

    [Fact]
    public async Task GetForProductAsync_OrdersPrimaryFirstThenRetiredLast()
    {
        var product = await SeedProductAsync("PRIMARY", "SECOND", "THIRD");
        var third = (await BarcodesOfAsync(product.Id)).Single(b => b.Value == "THIRD");
        await _sut.SetBarcodeActiveAsync(third.Id, isActive: false, await OwnerIdAsync());

        var listed = await _sut.GetForProductAsync(product.Id);

        Assert.Equal("PRIMARY", listed[0].Value);
        Assert.Equal("THIRD", listed[^1].Value);
    }

    // ---- AssignBarcodeAsync (compatibility wrapper) ----

    [Fact]
    public async Task AssignBarcodeAsync_GeneratesInternalBarcode_WhenNoneProvided()
    {
        var product = await SeedProductAsync();

        var assigned = await _sut.AssignBarcodeAsync(product.Id, explicitBarcode: null, await OwnerIdAsync());

        Assert.True(Ean13.IsValid(assigned.Value));
    }

    [Fact]
    public async Task AssignBarcodeAsync_UsesExplicitBarcode_WhenProvided()
    {
        var product = await SeedProductAsync();

        var assigned = await _sut.AssignBarcodeAsync(product.Id, "MANUAL-CODE-1", await OwnerIdAsync());

        Assert.Equal("MANUAL-CODE-1", assigned.Value);
    }

    [Fact]
    public async Task AssignBarcodeAsync_Throws_WhenExplicitBarcodeAlreadyInUse()
    {
        await SeedProductAsync("TAKEN-BARCODE");
        var second = await SeedProductAsync();
        var ownerId = await OwnerIdAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignBarcodeAsync(second.Id, "TAKEN-BARCODE", ownerId));
    }

    /// <summary>The wrapper keeps its own audit action so the existing trail stays searchable.</summary>
    [Fact]
    public async Task AssignBarcodeAsync_WritesAuditLog()
    {
        var product = await SeedProductAsync();

        await _sut.AssignBarcodeAsync(product.Id, "AUDIT-TEST", await OwnerIdAsync());

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "BarcodeAssigned"));
    }

    [Fact]
    public async Task AssignBarcodeAsync_Throws_WhenProductNotFound()
    {
        var ownerId = await OwnerIdAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignBarcodeAsync(999, null, ownerId));
    }

    public void Dispose() => _fixture.Dispose();
}
