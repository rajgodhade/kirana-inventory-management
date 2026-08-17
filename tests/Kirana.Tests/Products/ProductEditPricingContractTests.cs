using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Products;

/// <summary>
/// Phase 15A Step 3: the contract the Product Edit dialog depends on.
///
/// <para>The dialog cannot be unit-tested directly — Kirana.App targets a Windows/WinUI TFM that
/// this net10.0 test project cannot reference — so these tests exercise the boundary the dialog
/// actually talks to, and they do it the way the dialog does: by sending a FULL
/// <see cref="UpdateProductRequest"/> with every field populated on every save. The pricing tests
/// in ProductPricingIntegrationTests use a deliberately minimal request, so the "operator edits one
/// price and everything else survives" case is only covered here.</para>
///
/// <para>The other half of the contract is the two text boxes. "Retail price" is never blank
/// (required, non-nullable), while "Wholesale price" is a nullable string where BLANK means the
/// level is not configured — distinct from a typed 0. Those two readings are asserted as the null
/// and zero cases below.</para>
/// </summary>
public class ProductEditPricingContractTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductService _sut;
    private readonly int _ownerId;

    public ProductEditPricingContractTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        var barcodes = new BarcodeService(_fixture.Context, sequence, audit, permissions);
        var pricing = new ProductPricingService(_fixture.Context, audit, permissions);

        _sut = new ProductService(_fixture.Context, sequence, audit, barcodes, permissions, pricing);
    }

    /// <summary>Creates the product the dialog's Scenario A/C/D operate on: retail 22, wholesale 20.</summary>
    private Task<Product> SeedProductAsync(decimal retail = 22m, decimal? wholesale = 20m) =>
        _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Tata Salt 1kg",
            Sku = "TATA-SALT-1KG",
            Description = "Iodised salt",
            Unit = UnitOfMeasure.Piece,
            UnitDisplayText = "1kg Pack",
            PurchasePackUnit = UnitOfMeasure.Box,
            PurchasePackSize = 24m,
            PurchasePrice = 18m,
            Mrp = 25m,
            SellingPrice = retail,
            WholesalePrice = wholesale,
            DefaultDiscountPercent = 2m,
            GstRatePercent = 5m,
            HsnCode = "25010020",
            PricingType = PricingType.Inclusive,
            MinimumStock = 10m,
            ReorderQuantity = 40m,
            ReplenishmentEnabled = true,
            OpeningStock = 95m,
            PerformedByUserId = _ownerId,
        });

    /// <summary>
    /// Every field the dialog puts on the wire, echoed back from the loaded product exactly as the
    /// dialog's text boxes would — only the prices under test differ. That is what makes this a
    /// regression net: if UpdateAsync ever started dropping a field on a price-only edit, the
    /// dialog would silently erase it, and this is the shape that would catch it.
    /// </summary>
    private UpdateProductRequest DialogSave(Product product, decimal retail, decimal? wholesale) => new()
    {
        Name = product.Name,
        Sku = product.Sku,
        Description = product.Description,
        CategoryId = product.CategoryId,
        BrandId = product.BrandId,
        Unit = product.Unit,
        PurchasePackUnit = product.PurchasePackUnit,
        PurchasePackSize = product.PurchasePackSize,
        UnitDisplayText = product.UnitDisplayText,
        PurchasePrice = product.PurchasePrice,
        Mrp = product.Mrp,
        SellingPrice = retail,
        WholesalePrice = wholesale,
        DefaultDiscountPercent = product.DefaultDiscountPercent,
        GstRatePercent = product.GstRatePercent,
        HsnCode = product.HsnCode,
        PricingType = product.PricingType,
        TracksBatches = product.TracksBatches,
        MinimumStock = product.MinimumStock,
        ReorderQuantity = product.ReorderQuantity,
        UpdateReplenishmentConfiguration = true,
        ReplenishmentEnabled = product.ReplenishmentEnabled,
        PreferredSupplierId = product.PreferredSupplierId,
        PerformedByUserId = _ownerId,
    };

    private async Task<decimal?> LevelAsync(int productId, PriceLevel level) =>
        await _fixture.Context.ProductPrices.AsNoTracking()
            .Where(p => p.ProductId == productId && p.Level == level && p.IsActive)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefaultAsync();

    private Task<Product> ReloadAsync(int productId) =>
        _fixture.Context.Products.AsNoTracking().FirstAsync(p => p.Id == productId);

    // ---- 1/2. The values the dialog reads back into its two boxes ----

    [Fact]
    public async Task ReopeningAProduct_ShowsTheRetailAndWholesaleItWasSavedWith()
    {
        var product = await SeedProductAsync();

        var reloaded = await ReloadAsync(product.Id);

        // What the dialog binds...
        Assert.Equal(22m, reloaded.SellingPrice);
        Assert.Equal(20m, reloaded.WholesalePrice);
        // ...agrees with the authoritative rows behind the labels.
        Assert.Equal(22m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(20m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    // ---- 3/4. Blank means "not configured"; typed zero means zero ----

    [Fact]
    public async Task AProductWithNoWholesale_LoadsAsNotConfigured_NotZero()
    {
        var product = await SeedProductAsync(wholesale: null);

        var reloaded = await ReloadAsync(product.Id);

        // Null is what makes the box render empty. A 0 here would show "0" and quietly claim the
        // shop sells this at nothing wholesale.
        Assert.Null(reloaded.WholesalePrice);
        Assert.NotEqual(0m, reloaded.WholesalePrice);
        Assert.Null(await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    [Fact]
    public async Task AWholesaleOfZero_IsKeptAsAConfiguredZero()
    {
        var product = await SeedProductAsync(wholesale: 0m);

        var reloaded = await ReloadAsync(product.Id);

        Assert.Equal(0m, reloaded.WholesalePrice);
        Assert.NotNull(reloaded.WholesalePrice);
        Assert.Equal(0m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    [Fact]
    public async Task ClearingTheWholesaleBox_ReturnsTheProductToNotConfigured()
    {
        var product = await SeedProductAsync(wholesale: 20m);

        await _sut.UpdateAsync(product.Id, DialogSave(product, retail: 22m, wholesale: null));

        var reloaded = await ReloadAsync(product.Id);
        Assert.Null(reloaded.WholesalePrice);
        Assert.Null(await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    // ---- 5/6/7/8. Editing one level through a full dialog save ----

    [Fact]
    public async Task EditingRetailOnly_PersistsRetailAndLeavesWholesaleAlone()
    {
        var product = await SeedProductAsync();

        await _sut.UpdateAsync(product.Id, DialogSave(product, retail: 25m, wholesale: 20m));

        var reloaded = await ReloadAsync(product.Id);
        Assert.Equal(25m, reloaded.SellingPrice);
        Assert.Equal(20m, reloaded.WholesalePrice);
        Assert.Equal(25m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(20m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    [Fact]
    public async Task EditingWholesaleOnly_PersistsWholesaleAndLeavesRetailAlone()
    {
        var product = await SeedProductAsync();

        await _sut.UpdateAsync(product.Id, DialogSave(product, retail: 22m, wholesale: 19m));

        var reloaded = await ReloadAsync(product.Id);
        Assert.Equal(22m, reloaded.SellingPrice);
        Assert.Equal(19m, reloaded.WholesalePrice);
        Assert.Equal(22m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(19m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    // ---- 9/10. Negative values are refused by the service, which is what the dialog shows ----

    [Fact]
    public async Task ANegativeWholesale_IsRejectedAndLeavesBothLevelsIntact()
    {
        var product = await SeedProductAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(product.Id, DialogSave(product, retail: 22m, wholesale: -5m)));

        // The dialog surfaces ex.Message verbatim in its error InfoBar, so it has to read as
        // something an operator can act on.
        Assert.Contains("negative", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(22m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(20m, await LevelAsync(product.Id, PriceLevel.Wholesale));
    }

    [Fact]
    public async Task ANegativeRetail_IsRejectedAndLeavesBothLevelsIntact()
    {
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(product.Id, DialogSave(product, retail: -1m, wholesale: 20m)));

        Assert.Equal(22m, await LevelAsync(product.Id, PriceLevel.Retail));
        Assert.Equal(20m, await LevelAsync(product.Id, PriceLevel.Wholesale));
        Assert.Equal(22m, (await ReloadAsync(product.Id)).SellingPrice);
    }

    // ---- 11. A price-only edit must not disturb anything else on the product ----

    [Fact]
    public async Task ChangingOnlyThePrices_LeavesEveryOtherProductFieldUntouched()
    {
        var product = await SeedProductAsync();
        var before = await ReloadAsync(product.Id);
        var barcodesBefore = await _fixture.Context.ProductBarcodes
            .CountAsync(b => b.ProductId == product.Id);

        await _sut.UpdateAsync(product.Id, DialogSave(product, retail: 25m, wholesale: 19m));

        var after = await ReloadAsync(product.Id);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.ProductCode, after.ProductCode);
        Assert.Equal(before.Sku, after.Sku);
        Assert.Equal(before.Description, after.Description);
        Assert.Equal(before.Unit, after.Unit);
        Assert.Equal(before.UnitDisplayText, after.UnitDisplayText);
        Assert.Equal(before.PurchasePackUnit, after.PurchasePackUnit);
        Assert.Equal(before.PurchasePackSize, after.PurchasePackSize);
        Assert.Equal(before.PurchasePrice, after.PurchasePrice);
        Assert.Equal(before.Mrp, after.Mrp);
        Assert.Equal(before.DefaultDiscountPercent, after.DefaultDiscountPercent);
        Assert.Equal(before.GstRatePercent, after.GstRatePercent);
        Assert.Equal(before.HsnCode, after.HsnCode);
        Assert.Equal(before.PricingType, after.PricingType);
        Assert.Equal(before.TracksBatches, after.TracksBatches);
        Assert.Equal(before.MinimumStock, after.MinimumStock);
        Assert.Equal(before.ReorderQuantity, after.ReorderQuantity);
        Assert.Equal(before.ReplenishmentEnabled, after.ReplenishmentEnabled);
        Assert.Equal(before.PreferredSupplierId, after.PreferredSupplierId);
        Assert.Equal(before.IsActive, after.IsActive);

        // ...and the barcodes the left-hand column owns are still there.
        Assert.Equal(barcodesBefore,
            await _fixture.Context.ProductBarcodes.CountAsync(b => b.ProductId == product.Id));
    }

    /// <summary>Editing prices must never touch stock — the dialog's stock box is read-only in edit
    /// mode precisely because adjustments have to go through IInventoryService.</summary>
    [Fact]
    public async Task ChangingPrices_DoesNotMoveStock()
    {
        var product = await SeedProductAsync();
        var movementsBefore = await _fixture.Context.StockMovements.CountAsync(m => m.ProductId == product.Id);

        await _sut.UpdateAsync(product.Id, DialogSave(product, retail: 25m, wholesale: 19m));

        Assert.Equal(95m, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
        Assert.Equal(movementsBefore,
            await _fixture.Context.StockMovements.CountAsync(m => m.ProductId == product.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
