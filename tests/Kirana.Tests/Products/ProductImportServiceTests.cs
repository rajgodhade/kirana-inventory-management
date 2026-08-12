using System.Text;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Products;

public class ProductImportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductImportService _sut;
    private readonly ProductService _productService;
    private readonly int _ownerId;

    public ProductImportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        var auditLogger = new EfAuditLogger(_fixture.Context);
        var barcodeService = new BarcodeService(_fixture.Context, sequenceGenerator, auditLogger);
        var permissionEnforcer = new PermissionEnforcer(_fixture.Context);

        _sut = new ProductImportService(_fixture.Context, sequenceGenerator, auditLogger, barcodeService, permissionEnforcer);
        _productService = new ProductService(_fixture.Context, sequenceGenerator, auditLogger, barcodeService, permissionEnforcer);
    }

    private const string Header = "Name,SKU,Barcode,Category,Brand,Unit,Purchase Price,MRP,Selling Price,GST %,Minimum Stock,Reorder Quantity,Opening Stock";

    private static Stream CsvStream(params string[] dataRows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", dataRows) + "\r\n"));

    private Task<ProductImportPreview> PreviewAsync(params string[] dataRows) =>
        _sut.BuildPreviewAsync(CsvStream(dataRows), "products.csv", _ownerId);

    [Fact]
    public async Task BuildPreviewAsync_MarksValidRowAsNew()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100");

        Assert.Null(preview.FatalError);
        Assert.Equal(1, preview.NewCount);
        Assert.Equal(0, preview.ErrorCount);
        Assert.Equal(ProductImportRowStatus.New, preview.Rows.Single().Status);
    }

    [Fact]
    public async Task ImportWithoutPricingType_DefaultsToInclusive()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100");

        await _sut.CommitAsync(preview, _ownerId);

        var product = await _fixture.Context.Products.SingleAsync();
        Assert.Equal(PricingType.Inclusive, product.PricingType);
    }

    [Fact]
    public async Task ImportAcceptsExplicitExclusivePricingType()
    {
        const string header = Header + ",Pricing Type";
        var csv = header + "\r\nTata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100,Exclusive\r\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);
        await _sut.CommitAsync(preview, _ownerId);

        Assert.Equal(PricingType.Exclusive, (await _fixture.Context.Products.SingleAsync()).PricingType);
    }

    [Fact]
    public async Task ImportRejectsUnknownPricingType()
    {
        const string header = Header + ",Pricing Type";
        var csv = header + "\r\nTata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100,AddedSometimes\r\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);

        Assert.Contains(preview.Rows.Single().Errors, e => e.Contains("Pricing Type"));
    }

    [Fact]
    public async Task BuildPreviewAsync_WritesNothing()
    {
        await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100");

        Assert.Empty(await _fixture.Context.Products.ToListAsync());
        Assert.Empty(await _fixture.Context.Categories.ToListAsync());
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsMissingName()
    {
        var preview = await PreviewAsync(",TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100");

        var row = preview.Rows.Single();
        Assert.Equal(ProductImportRowStatus.Error, row.Status);
        Assert.Contains(row.Errors, e => e.Contains("Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsUnknownUnit()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Furlong,18,25,22,5,10,20,100");

        Assert.Contains(preview.Rows.Single().Errors, e => e.Contains("Furlong"));
    }

    [Fact]
    public async Task BuildPreviewAsync_ReportsNonNumericPrice()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,abc,5,10,20,100");

        Assert.Contains(preview.Rows.Single().Errors, e => e.Contains("Selling Price"));
    }

    [Fact]
    public async Task BuildPreviewAsync_RejectsFractionalStock_ForWholeUnitProduct()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10.5");

        Assert.Contains(preview.Rows.Single().Errors, e => e.Contains("whole-unit"));
    }

    [Fact]
    public async Task BuildPreviewAsync_AllowsFractionalStock_ForWeightBasedProduct()
    {
        var preview = await PreviewAsync("Basmati,BAS-1,,Grocery,,Kilogram,60,100,90,5,0,0,10.5");

        Assert.Equal(ProductImportRowStatus.New, preview.Rows.Single().Status);
    }

    [Fact]
    public async Task BuildPreviewAsync_DetectsDuplicateSkuWithinTheFile()
    {
        var preview = await PreviewAsync(
            "Tata Salt,DUP-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10",
            "Other Salt,DUP-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10");

        Assert.Equal(ProductImportRowStatus.New, preview.Rows[0].Status);
        Assert.Equal(ProductImportRowStatus.Error, preview.Rows[1].Status);
        Assert.Contains(preview.Rows[1].Errors, e => e.Contains("already used by row 2"));
    }

    [Fact]
    public async Task BuildPreviewAsync_MatchesExistingProductBySku_AsUpdate()
    {
        await _productService.CreateAsync(new CreateProductRequest
        {
            Name = "Existing", Sku = "TATA-1", Unit = UnitOfMeasure.Piece,
            SellingPrice = 10, Mrp = 12, PerformedByUserId = _ownerId,
        });

        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10");

        Assert.Equal(1, preview.UpdateCount);
        Assert.Equal(ProductImportRowStatus.Update, preview.Rows.Single().Status);
    }

    [Fact]
    public async Task BuildPreviewAsync_ListsCategoriesAndBrandsThatWouldBeCreated()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10");

        Assert.Contains("Grocery", preview.NewCategoryNames);
        Assert.Contains("Tata", preview.NewBrandNames);
    }

    [Fact]
    public async Task BuildPreviewAsync_FailsWhenNameColumnMissing()
    {
        var csv = "SKU,Selling Price\r\nTATA-1,22\r\n";
        var preview = await _sut.BuildPreviewAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "products.csv", _ownerId);

        Assert.True(preview.HasFatalError);
        Assert.False(preview.CanCommit);
    }

    [Fact]
    public async Task BuildPreviewAsync_HandlesQuotedFieldsContainingCommas()
    {
        var preview = await PreviewAsync("\"Salt, Iodised\",TATA-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10");

        var row = preview.Rows.Single();
        Assert.Equal(ProductImportRowStatus.New, row.Status);
        Assert.Equal("Salt, Iodised", row.Name);
    }

    [Fact]
    public async Task BuildPreviewAsync_StripsCurrencySymbolsAndSeparators()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,\"₹1,018\",25,\"₹1,022.50\",5,0,0,10");

        var row = preview.Rows.Single();
        Assert.Equal(ProductImportRowStatus.New, row.Status);
        Assert.Equal(1022.50m, row.SellingPrice);
    }

    [Fact]
    public async Task CommitAsync_CreatesProductsWithInventoryAndOpeningStockMovement()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100");
        var result = await _sut.CommitAsync(preview, _ownerId);

        Assert.Equal(1, result.CreatedCount);

        var product = await _fixture.Context.Products.SingleAsync();
        Assert.Equal("Tata Salt", product.Name);
        Assert.Matches(@"^PRD-\d{6}$", product.ProductCode);

        var inventory = await _fixture.Context.Inventories.SingleAsync();
        Assert.Equal(100m, inventory.QuantityOnHand);

        var movement = await _fixture.Context.StockMovements.SingleAsync();
        Assert.Equal(StockMovementType.OpeningStock, movement.MovementType);
        Assert.Equal(100m, movement.QuantityChange);
    }

    [Fact]
    public async Task CommitAsync_CreatesMissingCategoriesAndBrands()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,0,0,10");
        await _sut.CommitAsync(preview, _ownerId);

        var product = await _fixture.Context.Products.SingleAsync();
        var category = await _fixture.Context.Categories.SingleAsync();
        var brand = await _fixture.Context.Brands.SingleAsync();

        Assert.Equal("Grocery", category.Name);
        Assert.Equal("Tata", brand.Name);
        Assert.Equal(category.Id, product.CategoryId);
        Assert.Equal(brand.Id, product.BrandId);
    }

    [Fact]
    public async Task CommitAsync_ReusesExistingCategoryCaseInsensitively()
    {
        _fixture.Context.Categories.Add(new Category { Name = "Grocery" });
        await _fixture.Context.SaveChangesAsync();

        var preview = await PreviewAsync("Tata Salt,TATA-1,,grocery,,Piece,18,25,22,5,0,0,10");
        await _sut.CommitAsync(preview, _ownerId);

        Assert.Single(await _fixture.Context.Categories.ToListAsync());
    }

    [Fact]
    public async Task CommitAsync_SkipsErrorRowsButStillImportsValidOnes()
    {
        var preview = await PreviewAsync(
            "Good Product,GOOD-1,,Grocery,,Piece,18,25,22,5,0,0,10",
            ",BAD-1,,Grocery,,Piece,18,25,22,5,0,0,10");

        var result = await _sut.CommitAsync(preview, _ownerId);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal("Good Product", (await _fixture.Context.Products.SingleAsync()).Name);
    }

    [Fact]
    public async Task CommitAsync_UpdatesMatchedProductInPlace_WithoutCreatingDuplicate()
    {
        var existing = await _productService.CreateAsync(new CreateProductRequest
        {
            Name = "Old Name", Sku = "TATA-1", Unit = UnitOfMeasure.Piece,
            SellingPrice = 10, Mrp = 12, PerformedByUserId = _ownerId,
        });

        var preview = await PreviewAsync("New Name,TATA-1,,Grocery,,Piece,18,25,99,5,0,0,0");
        var result = await _sut.CommitAsync(preview, _ownerId);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.CreatedCount);

        var product = await _fixture.Context.Products.SingleAsync();
        Assert.Equal(existing.Id, product.Id);
        Assert.Equal("New Name", product.Name);
        Assert.Equal(99m, product.SellingPrice);
    }

    [Fact]
    public async Task CommitAsync_ImportsEveryValidRowInOneGo()
    {
        var preview = await PreviewAsync(
            "P1,S1,,Grocery,,Piece,1,2,2,5,0,0,1",
            "P2,S2,,Grocery,,Piece,1,2,2,5,0,0,2",
            "P3,S3,,Grocery,,Piece,1,2,2,5,0,0,3");

        var result = await _sut.CommitAsync(preview, _ownerId);

        Assert.Equal(3, result.CreatedCount);
        Assert.Equal(3, await _fixture.Context.Products.CountAsync());
        Assert.Equal(3, (await _fixture.Context.Products.Select(p => p.ProductCode).ToListAsync()).Distinct().Count());
    }

    [Fact]
    public async Task CommitAsync_WritesAnAuditEntry()
    {
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,,Piece,18,25,22,5,0,0,10");
        await _sut.CommitAsync(preview, _ownerId);

        Assert.Contains(
            await _fixture.Context.AuditLogs.ToListAsync(),
            a => a.Action == "ProductsImported");
    }

    [Fact]
    public async Task BuildPreviewAsync_Throws_WhenUserLacksProductsEditPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _sut.BuildPreviewAsync(CsvStream("Tata Salt,TATA-1,,Grocery,,Piece,18,25,22,5,0,0,10"), "products.csv", cashier.Id));
    }

    [Fact]
    public async Task CommitAsync_Throws_WhenUserLacksProductsEditPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,,Piece,18,25,22,5,0,0,10");

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.CommitAsync(preview, cashier.Id));
    }

    [Fact]
    public async Task ReviseRowAsync_FixesErrorRow_WithoutReuploadingTheFile()
    {
        var preview = await PreviewAsync(",BAD-1,,Grocery,,Piece,18,25,22,5,0,0,10");
        var badRow = preview.Rows.Single();
        Assert.Equal(ProductImportRowStatus.Error, badRow.Status);

        var fixedFields = new Dictionary<string, string>(badRow.RawFields) { [ProductImportParser.NormalizeHeader("Name")] = "Tata Salt" };
        var revised = await _sut.ReviseRowAsync(preview, badRow.RowNumber, fixedFields, _ownerId);

        Assert.Equal(ProductImportRowStatus.New, revised.Rows.Single().Status);
        Assert.Equal("Tata Salt", revised.Rows.Single().Name);
    }

    [Fact]
    public async Task ReviseRowAsync_DoesNotWriteAnything()
    {
        var preview = await PreviewAsync(",BAD-1,,Grocery,,Piece,18,25,22,5,0,0,10");
        var badRow = preview.Rows.Single();

        var fixedFields = new Dictionary<string, string>(badRow.RawFields) { [ProductImportParser.NormalizeHeader("Name")] = "Tata Salt" };
        await _sut.ReviseRowAsync(preview, badRow.RowNumber, fixedFields, _ownerId);

        Assert.Empty(await _fixture.Context.Products.ToListAsync());
    }

    [Fact]
    public async Task ReviseRowAsync_RevalidatesEveryRow_SoFixingADuplicateSkuClearsTheOtherRowToo()
    {
        var preview = await PreviewAsync(
            "First,DUP-1,,Grocery,,Piece,10,12,11,5,0,0,5",
            "Second,DUP-1,,Grocery,,Piece,10,12,11,5,0,0,5");

        Assert.Equal(ProductImportRowStatus.New, preview.Rows[0].Status);
        Assert.Equal(ProductImportRowStatus.Error, preview.Rows[1].Status);

        var secondRow = preview.Rows[1];
        var fixedFields = new Dictionary<string, string>(secondRow.RawFields) { [ProductImportParser.NormalizeHeader("SKU")] = "DUP-2" };
        var revised = await _sut.ReviseRowAsync(preview, secondRow.RowNumber, fixedFields, _ownerId);

        Assert.Equal(ProductImportRowStatus.New, revised.Rows[0].Status);
        Assert.Equal(ProductImportRowStatus.New, revised.Rows[1].Status);
        Assert.Equal(0, revised.ErrorCount);
    }

    [Fact]
    public async Task ReviseRowAsync_Throws_WhenUserLacksProductsEditPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        var preview = await PreviewAsync(",BAD-1,,Grocery,,Piece,18,25,22,5,0,0,10");
        var badRow = preview.Rows.Single();
        var fixedFields = new Dictionary<string, string>(badRow.RawFields) { [ProductImportParser.NormalizeHeader("Name")] = "Tata Salt" };

        await Assert.ThrowsAnyAsync<Exception>(() => _sut.ReviseRowAsync(preview, badRow.RowNumber, fixedFields, cashier.Id));
    }

    [Fact]
    public void BuildCsvTemplate_RoundTripsThroughTheParser()
    {
        var template = _sut.BuildCsvTemplate();
        var parsed = ProductImportParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(template)), "template.csv");

        Assert.Null(parsed.FatalError);
        Assert.Single(parsed.Rows);
        Assert.Equal("Tata Salt 1kg", parsed.Rows[0][ProductImportParser.NormalizeHeader("Name")]);
    }

    [Fact]
    public void Columns_FieldKeysMatchNormalizedRawFieldKeys()
    {
        // A correction form built from ProductImportRow.RawFields keyed by Columns[i].FieldKey must
        // actually find every value — this pins that the two stay in sync.
        var nameColumn = _sut.Columns.Single(c => c.CanonicalName == "Name");
        Assert.Equal(ProductImportParser.NormalizeHeader("Name"), nameColumn.FieldKey);
    }

    // ===================== Phase 13A: units, pack sizes & unit conversion =====================

    private const string HeaderWithPack = Header + ",Purchase Pack Unit,Purchase Pack Size,Unit Display Text";

    private static Stream CsvStreamWithPack(params string[] dataRows) =>
        new MemoryStream(Encoding.UTF8.GetBytes(HeaderWithPack + "\r\n" + string.Join("\r\n", dataRows) + "\r\n"));

    [Fact]
    public async Task Import_DefaultsPackFieldsToNull_WhenColumnsAbsent()
    {
        // The plain (no-pack-column) Header used throughout this file — proves old-format files
        // with no pack columns at all still import successfully with no pack configured.
        var preview = await PreviewAsync("Tata Salt,TATA-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100");
        await _sut.CommitAsync(preview, _ownerId);

        var product = await _fixture.Context.Products.SingleAsync();
        Assert.Null(product.PurchasePackUnit);
        Assert.Null(product.PurchasePackSize);
        Assert.Null(product.UnitDisplayText);
    }

    [Fact]
    public async Task Import_ParsesValidPackConfiguration_WhenColumnsPresent()
    {
        var stream = CsvStreamWithPack("Biscuit Box,BISC-1,,Grocery,Parle,Piece,18,25,22,5,10,20,100,Box,12,");
        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);
        await _sut.CommitAsync(preview, _ownerId);

        var product = await _fixture.Context.Products.SingleAsync();
        Assert.Equal(UnitOfMeasure.Box, product.PurchasePackUnit);
        Assert.Equal(12, product.PurchasePackSize);
    }

    [Fact]
    public async Task Import_RowError_WhenPackUnitTextIsUnrecognized()
    {
        var stream = CsvStreamWithPack("Biscuit Box,BISC-1,,Grocery,Parle,Piece,18,25,22,5,10,20,100,Crate,12,");
        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);

        Assert.Contains(preview.Rows.Single().Errors, e => e.Contains("Purchase Pack Unit"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task Import_RowError_WhenPackSizeIsZeroOrNegative(string packSize)
    {
        var stream = CsvStreamWithPack($"Biscuit Box,BISC-1,,Grocery,Parle,Piece,18,25,22,5,10,20,100,Box,{packSize},");
        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);

        Assert.Equal(ProductImportRowStatus.Error, preview.Rows.Single().Status);
    }

    [Fact]
    public async Task Import_RowError_WhenPackUnitEqualsUnitColumn()
    {
        var stream = CsvStreamWithPack("Biscuit Box,BISC-1,,Grocery,Parle,Piece,18,25,22,5,10,20,100,Piece,12,");
        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);

        Assert.Equal(ProductImportRowStatus.Error, preview.Rows.Single().Status);
    }

    [Fact]
    public async Task Import_RowError_ExcludesRowFromCommit_ButOtherValidRowsStillCommit()
    {
        var stream = CsvStreamWithPack(
            "Good Product,GOOD-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100,,,",
            "Bad Pack Product,BAD-1,,Grocery,Tata,Piece,18,25,22,5,10,20,100,Piece,12,");
        var preview = await _sut.BuildPreviewAsync(stream, "products.csv", _ownerId);

        Assert.Equal(1, preview.NewCount);
        Assert.Equal(1, preview.ErrorCount);

        var result = await _sut.CommitAsync(preview, _ownerId);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.SkippedErrorCount);
        Assert.Equal("Good Product", (await _fixture.Context.Products.SingleAsync()).Name);
    }

    [Fact]
    public void BuildCsvTemplate_IncludesNewOptionalPackColumns()
    {
        var template = _sut.BuildCsvTemplate();

        Assert.Contains("Purchase Pack Unit", template);
        Assert.Contains("Purchase Pack Size", template);
        Assert.Contains("Unit Display Text", template);
    }

    public void Dispose() => _fixture.Dispose();
}
