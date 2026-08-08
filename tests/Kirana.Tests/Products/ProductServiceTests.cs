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
        var barcodeService = new BarcodeService(_fixture.Context, sequenceGenerator, auditLogger);
        var permissionEnforcer = new PermissionEnforcer(_fixture.Context);
        _sut = new ProductService(_fixture.Context, sequenceGenerator, auditLogger, barcodeService, permissionEnforcer);
    }

    private CreateProductRequest ValidRequest(string name = "Tata Salt 1kg", string? sku = "TATA-SALT-1KG", string? barcode = "8901030826501") => new()
    {
        Name = name,
        Sku = sku,
        Barcode = barcode,
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
            Barcode = "BAR-NO-STOCK",
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
            Barcode = product.Barcode,
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
            Barcode = product.Barcode,
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

    public void Dispose() => _fixture.Dispose();
}
