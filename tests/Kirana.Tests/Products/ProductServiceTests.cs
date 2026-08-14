using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Products;

public class ProductServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductService _sut;
    private readonly int _ownerId;

    public ProductServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        var auditLogger = new EfAuditLogger(_fixture.Context);
        var permissionEnforcer = new PermissionEnforcer(_fixture.Context);
        var barcodeService = new BarcodeService(_fixture.Context, sequenceGenerator, auditLogger, permissionEnforcer);
        _sut = new ProductService(_fixture.Context, sequenceGenerator, auditLogger, barcodeService, permissionEnforcer);
    }

    private CreateProductRequest ValidRequest(string name = "Tata Salt 1kg", string? sku = "TATA-SALT-1KG", string? barcode = "8901030826501") => new()
    {
        Name = name,
        Sku = sku,
        Barcodes = barcode is null ? [] : [barcode],
        Unit = UnitOfMeasure.Piece,
        PurchasePrice = 18,
        Mrp = 25,
        SellingPrice = 24,
        GstRatePercent = 5,
        MinimumStock = 10,
        ReorderQuantity = 50,
        OpeningStock = 100,
        PerformedByUserId = _ownerId,
    };

    private CreateProductRequest ReplenishmentRequest(
        decimal reorder, decimal target, UnitOfMeasure unit = UnitOfMeasure.Piece, bool enabled = true) => new()
    {
        Name = $"Replenishment {Guid.NewGuid():N}", Sku = $"REP-{Guid.NewGuid():N}", Barcodes = [],
        Unit = unit, PurchasePrice = 10, Mrp = 12, SellingPrice = 11,
        MinimumStock = reorder, ReorderQuantity = target, ReplenishmentEnabled = enabled,
        OpeningStock = 0, PerformedByUserId = _ownerId,
    };

    [Fact]
    public async Task ReplenishmentConfiguration_PersistsDisabledByDefault()
    {
        var product = await _sut.CreateAsync(ValidRequest());
        Assert.False(product.ReplenishmentEnabled);
        Assert.Null(product.PreferredSupplierId);
    }

    [Fact]
    public async Task LegacyUpdatePath_PreservesExistingReplenishmentConfiguration()
    {
        var product = await _sut.CreateAsync(ReplenishmentRequest(20, 50));

        await _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = "Updated without replenishment fields",
            Unit = product.Unit,
            PurchasePrice = product.PurchasePrice,
            Mrp = product.Mrp,
            SellingPrice = product.SellingPrice,
            MinimumStock = product.MinimumStock,
            ReorderQuantity = product.ReorderQuantity,
            PerformedByUserId = _ownerId,
        });

        var persisted = await _fixture.Context.Products.SingleAsync(p => p.Id == product.Id);
        Assert.True(persisted.ReplenishmentEnabled);
    }

    [Fact]
    public async Task EditProduct_CanConfigureAndReloadReplenishmentSettings()
    {
        var supplier = new Supplier
        {
            SupplierCode = "SUP-REPLENISH",
            Name = "Preferred Replenishment Supplier",
            IsActive = true,
        };
        _fixture.Context.Suppliers.Add(supplier);
        await _fixture.Context.SaveChangesAsync();
        var product = await _sut.CreateAsync(ValidRequest());

        await _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = product.Name,
            Sku = product.Sku,
            Unit = product.Unit,
            PurchasePrice = product.PurchasePrice,
            Mrp = product.Mrp,
            SellingPrice = product.SellingPrice,
            MinimumStock = 12,
            ReorderQuantity = 40,
            UpdateReplenishmentConfiguration = true,
            ReplenishmentEnabled = true,
            PreferredSupplierId = supplier.Id,
            PerformedByUserId = _ownerId,
        });

        _fixture.Context.ChangeTracker.Clear();
        var reloaded = await _sut.GetByIdAsync(product.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded.ReplenishmentEnabled);
        Assert.Equal(12, reloaded.MinimumStock);
        Assert.Equal(40, reloaded.ReorderQuantity);
        Assert.Equal(supplier.Id, reloaded.PreferredSupplierId);
        Assert.Equal(supplier.Name, reloaded.PreferredSupplier!.Name);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(20, -1)]
    [InlineData(50, 20)]
    public async Task ReplenishmentConfiguration_RejectsInvalidBounds(decimal reorder, decimal target)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(ReplenishmentRequest(reorder, target)));
    }

    [Fact]
    public async Task ReplenishmentConfiguration_RejectsFractionalPieceAndAllowsFractionalKilogram()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateAsync(ReplenishmentRequest(10.5m, 20.5m)));
        var product = await _sut.CreateAsync(ReplenishmentRequest(
            10.5m, 20.5m, UnitOfMeasure.Kilogram));
        Assert.Equal(10.5m, product.MinimumStock);
        Assert.Equal(20.5m, product.ReorderQuantity);
    }

    [Fact]
    public async Task CreateAsync_GeneratesSequentialProductCodes()
    {
        var first = await _sut.CreateAsync(ValidRequest("Product A", "SKU-A", "BAR-A"));
        var second = await _sut.CreateAsync(ValidRequest("Product B", "SKU-B", "BAR-B"));

        Assert.Equal("PRD-000001", first.ProductCode);
        Assert.Equal("PRD-000002", second.ProductCode);
    }

    [Fact]
    public async Task CreateAsync_CreatesInventoryRowWithOpeningStock()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(100, inventory.QuantityOnHand);
    }

    [Fact]
    public async Task CreateAsync_WritesOpeningStockMovement()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        var movement = await _fixture.Context.StockMovements.SingleAsync(m => m.ProductId == product.Id);
        Assert.Equal(StockMovementType.OpeningStock, movement.MovementType);
        Assert.Equal(0, movement.PreviousQuantity);
        Assert.Equal(100, movement.NewQuantity);
        Assert.Equal(100, movement.QuantityChange);
    }

    [Fact]
    public async Task CreateAsync_DoesNotWriteMovement_WhenOpeningStockIsZero()
    {
        var request = new CreateProductRequest
        {
            Name = "No Opening Stock Product",
            Sku = "SKU-NO-STOCK",
            Barcodes = ["BAR-NO-STOCK"],
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            OpeningStock = 0,
            PerformedByUserId = _ownerId,
        };

        var product = await _sut.CreateAsync(request);

        Assert.False(await _fixture.Context.StockMovements.AnyAsync(m => m.ProductId == product.Id));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenNameMissing()
    {
        var request = new CreateProductRequest { Name = "  ", PurchasePrice = 1, Mrp = 1, SellingPrice = 1, PerformedByUserId = _ownerId };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_DefaultsPricingTypeToGstInclusive()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        Assert.Equal(PricingType.Inclusive, product.PricingType);
        Assert.True(product.IsTaxInclusive);
    }

    [Fact]
    public async Task CreateAsync_RejectsNonStandardGstSlab()
    {
        var request = new CreateProductRequest
        {
            Name = "Invalid GST Product",
            Sku = "GST-7",
            PurchasePrice = 80,
            Mrp = 110,
            SellingPrice = 100,
            GstRatePercent = 7,
            PerformedByUserId = _ownerId,
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
        Assert.Contains("0%, 5%, 12%, 18%, or 28%", error.Message);
    }

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicateSku()
    {
        await _sut.CreateAsync(ValidRequest("First", "DUP-SKU", "BAR-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(ValidRequest("Second", "DUP-SKU", "BAR-2")));
    }

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicateBarcode()
    {
        await _sut.CreateAsync(ValidRequest("First", "SKU-1", "DUP-BAR"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(ValidRequest("Second", "SKU-2", "DUP-BAR")));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenCategoryDoesNotExist()
    {
        var request = new CreateProductRequest
        {
            Name = "Orphan", PurchasePrice = 1, Mrp = 1, SellingPrice = 1, CategoryId = 999, PerformedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_AppliesChanges()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        var updated = await _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = "Tata Salt 1kg (Updated)",
            Sku = product.Sku,
            // Phase 13B: barcodes are no longer part of the update request — they're owned by
            // IBarcodeService and persist independently.
            Unit = product.Unit,
            PurchasePrice = 19,
            Mrp = 26,
            SellingPrice = 25,
            MinimumStock = 5,
            ReorderQuantity = 25,
            PerformedByUserId = _ownerId,
        });

        Assert.Equal("Tata Salt 1kg (Updated)", updated.Name);
        Assert.Equal(25, updated.SellingPrice);
    }

    [Fact]
    public async Task UpdateAsync_LogsPriceModification_WhenPriceChanges()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        await _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = product.Name,
            Sku = product.Sku,
            Unit = product.Unit,
            PurchasePrice = product.PurchasePrice,
            Mrp = product.Mrp,
            SellingPrice = product.SellingPrice + 5,
            PerformedByUserId = _ownerId,
        });

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceModification"));
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenProductNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateAsync(999, new UpdateProductRequest { Name = "X", PurchasePrice = 1, Mrp = 1, SellingPrice = 1, PerformedByUserId = _ownerId }));
    }

    [Fact]
    public async Task SetActiveAsync_TogglesActiveFlag()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        await _sut.SetActiveAsync(product.Id, isActive: false, performedByUserId: _ownerId);
        Assert.False((await _sut.GetByIdAsync(product.Id))!.IsActive);

        await _sut.SetActiveAsync(product.Id, isActive: true, performedByUserId: _ownerId);
        Assert.True((await _sut.GetByIdAsync(product.Id))!.IsActive);
    }

    [Fact]
    public async Task SearchAsync_ExactBarcodeMatch_IsPrioritizedFirst()
    {
        await _sut.CreateAsync(ValidRequest("Amul Milk 1L", "AMUL-1L", "1112223334445"));
        var target = await _sut.CreateAsync(ValidRequest("Amul Butter", "AMUL-BUTTER", "9998887776665"));

        var results = await _sut.SearchAsync(new ProductSearchQuery { SearchText = "9998887776665" });

        Assert.Equal(target.Id, results[0].Id);
    }

    /// <summary>POS search feeds straight into the cart: typing a retired code and pressing Enter
    /// must not sell against it. Both the exact-match bucket and the partial-LIKE bucket have to
    /// filter IsActive — only the exact bucket did at first, so the retired code still surfaced
    /// through the partial match and defeated retirement entirely.</summary>
    [Fact]
    public async Task SearchAsync_DoesNotSurfaceAProduct_ByItsRetiredBarcode()
    {
        var product = await _sut.CreateAsync(ValidRequest("Retired Code Product", "RETIRE-SKU", "ACTIVE-CODE"));
        var barcodeService = new BarcodeService(
            _fixture.Context, new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));
        var retired = await barcodeService.AddBarcodeAsync(product.Id, "OLDPACK-9", makePrimary: false, _ownerId);
        await barcodeService.SetBarcodeActiveAsync(retired.Id, isActive: false, _ownerId);

        Assert.Empty(await _sut.SearchAsync(new ProductSearchQuery { SearchText = "OLDPACK-9" }));
        // A partial fragment of the retired code must not surface it either.
        Assert.Empty(await _sut.SearchAsync(new ProductSearchQuery { SearchText = "OLDPACK" }));
        // ...while the product remains findable by its active code.
        Assert.Single(await _sut.SearchAsync(new ProductSearchQuery { SearchText = "ACTIVE-CODE" }));
    }

    [Fact]
    public async Task SearchAsync_ExactSkuMatch_ComesBeforePartialNameMatch()
    {
        var skuMatch = await _sut.CreateAsync(ValidRequest("Generic Product", "FINDME", "BAR-X"));
        await _sut.CreateAsync(ValidRequest("FINDME Flavored Snacks", "OTHER-SKU", "BAR-Y"));

        var results = await _sut.SearchAsync(new ProductSearchQuery { SearchText = "FINDME" });

        Assert.Equal(skuMatch.Id, results[0].Id);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_ExcludesInactiveProducts_ByDefault()
    {
        var product = await _sut.CreateAsync(ValidRequest());
        await _sut.SetActiveAsync(product.Id, isActive: false, performedByUserId: _ownerId);

        var results = await _sut.SearchAsync(new ProductSearchQuery { SearchText = product.Name });

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_FiltersByCategory()
    {
        var category = new Category { Name = "Groceries", IsActive = true };
        _fixture.Context.Categories.Add(category);
        await _fixture.Context.SaveChangesAsync();

        var inCategory = await _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Rice Bag", PurchasePrice = 10, Mrp = 15, SellingPrice = 14, CategoryId = category.Id, PerformedByUserId = _ownerId,
        });
        await _sut.CreateAsync(ValidRequest("Uncategorized Product", "SKU-UNCAT", "BAR-UNCAT"));

        var results = await _sut.SearchAsync(new ProductSearchQuery { CategoryId = category.Id });

        Assert.Single(results);
        Assert.Equal(inCategory.Id, results[0].Id);
    }

    // ===================== Phase 13A: units, pack sizes & unit conversion =====================

    [Fact]
    public async Task CreateAsync_DefaultsToPieceUnit_WhenNotSpecified()
    {
        var product = await _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Default Unit Product", PurchasePrice = 10, Mrp = 15, SellingPrice = 14, PerformedByUserId = _ownerId,
        });

        Assert.Equal(UnitOfMeasure.Piece, product.Unit);
    }

    [Fact]
    public async Task CreateAsync_CreatesProduct_WithPieceUnit()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        Assert.Equal(UnitOfMeasure.Piece, product.Unit);
    }

    [Fact]
    public async Task CreateAsync_CreatesProduct_WithKilogramUnit()
    {
        var product = await _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Loose Rice", Sku = "SKU-RICE", Barcodes = ["BAR-RICE"], Unit = UnitOfMeasure.Kilogram,
            PurchasePrice = 40, Mrp = 60, SellingPrice = 55, PerformedByUserId = _ownerId,
        });

        Assert.Equal(UnitOfMeasure.Kilogram, product.Unit);
        Assert.True(product.Unit.SupportsDecimalQuantity());
    }

    [Fact]
    public async Task CreateAsync_CreatesProduct_WithPacketUnit()
    {
        var product = await _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Amul Butter 500g", Sku = "SKU-BUTTER", Barcodes = ["BAR-BUTTER"], Unit = UnitOfMeasure.Packet,
            UnitDisplayText = "500g Pack", PurchasePrice = 200, Mrp = 260, SellingPrice = 250, PerformedByUserId = _ownerId,
        });

        Assert.Equal(UnitOfMeasure.Packet, product.Unit);
        Assert.Equal("500g Pack", product.UnitDisplayText);
    }

    [Fact]
    public async Task CreateAsync_Persists_NullPackFields_WhenNotProvided()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        var persisted = await _fixture.Context.Products.SingleAsync(p => p.Id == product.Id);
        Assert.Null(persisted.PurchasePackUnit);
        Assert.Null(persisted.PurchasePackSize);
        Assert.Null(persisted.UnitDisplayText);
    }

    [Fact]
    public async Task CreateAsync_Persists_ValidPackConfiguration()
    {
        var product = await _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Biscuit Box", Sku = "SKU-BISCUIT", Barcodes = ["BAR-BISCUIT"], Unit = UnitOfMeasure.Piece,
            PurchasePackUnit = UnitOfMeasure.Box, PurchasePackSize = 12,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, PerformedByUserId = _ownerId,
        });

        var persisted = await _fixture.Context.Products.SingleAsync(p => p.Id == product.Id);
        Assert.Equal(UnitOfMeasure.Box, persisted.PurchasePackUnit);
        Assert.Equal(12, persisted.PurchasePackSize);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPackUnitSetWithoutPackSize()
    {
        var request = new CreateProductRequest
        {
            Name = "Bad Pack Product", PurchasePackUnit = UnitOfMeasure.Box,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, PerformedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPackSizeSetWithoutPackUnit()
    {
        var request = new CreateProductRequest
        {
            Name = "Bad Pack Product", PurchasePackSize = 12,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, PerformedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task CreateAsync_Throws_WhenPackSizeIsZeroOrNegative(decimal packSize)
    {
        var request = new CreateProductRequest
        {
            Name = "Bad Pack Product", PurchasePackUnit = UnitOfMeasure.Box, PurchasePackSize = packSize,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, PerformedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPackUnitEqualsBaseUnit()
    {
        var request = new CreateProductRequest
        {
            Name = "Self Conversion Product", Unit = UnitOfMeasure.Piece,
            PurchasePackUnit = UnitOfMeasure.Piece, PurchasePackSize = 12,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, PerformedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_CanAddPackConfiguration_ToExistingProduct()
    {
        var product = await _sut.CreateAsync(ValidRequest());

        await _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = product.Name, Unit = UnitOfMeasure.Piece,
            PurchasePackUnit = UnitOfMeasure.Box, PurchasePackSize = 24,
            PurchasePrice = product.PurchasePrice, Mrp = product.Mrp, SellingPrice = product.SellingPrice,
            PerformedByUserId = _ownerId,
        });

        var persisted = await _fixture.Context.Products.SingleAsync(p => p.Id == product.Id);
        Assert.Equal(UnitOfMeasure.Box, persisted.PurchasePackUnit);
        Assert.Equal(24, persisted.PurchasePackSize);
    }

    [Fact]
    public async Task UpdateAsync_CanRemovePackConfiguration_BySettingBothNull()
    {
        var product = await _sut.CreateAsync(new CreateProductRequest
        {
            Name = "Biscuit Box", Sku = "SKU-BISCUIT2", Barcodes = ["BAR-BISCUIT2"], Unit = UnitOfMeasure.Piece,
            PurchasePackUnit = UnitOfMeasure.Box, PurchasePackSize = 12,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, PerformedByUserId = _ownerId,
        });

        await _sut.UpdateAsync(product.Id, new UpdateProductRequest
        {
            Name = product.Name, Unit = UnitOfMeasure.Piece,
            PurchasePrice = product.PurchasePrice, Mrp = product.Mrp, SellingPrice = product.SellingPrice,
            PerformedByUserId = _ownerId,
        });

        var persisted = await _fixture.Context.Products.SingleAsync(p => p.Id == product.Id);
        Assert.Null(persisted.PurchasePackUnit);
        Assert.Null(persisted.PurchasePackSize);
    }

    public void Dispose() => _fixture.Dispose();
}
