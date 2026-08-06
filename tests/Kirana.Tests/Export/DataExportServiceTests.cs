using Kirana.Application.Authentication;
using Kirana.Application.Export;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Export;

public class DataExportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private DataExportService CreateService() => new(_fixture.Context, new PermissionEnforcer(_fixture.Context));

    private async Task<Product> SeedProductAsync()
    {
        var category = new Category { Name = "Staples" };
        var brand = new Brand { Name = "Amul" };
        _fixture.Context.Categories.Add(category);
        _fixture.Context.Brands.Add(brand);
        await _fixture.Context.SaveChangesAsync();

        var product = new Product
        {
            ProductCode = "PRD-000001",
            Name = "Amul Butter 500g",
            Sku = "AMUL-BUT-500",
            Barcode = "8901234567890",
            CategoryId = category.Id,
            BrandId = brand.Id,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 210m,
            Mrp = 275m,
            SellingPrice = 260m,
            GstRatePercent = 12m,
            MinimumStock = 5m,
        };
        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();

        _fixture.Context.Inventories.Add(new Inventory { ProductId = product.Id, QuantityOnHand = 12m });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task BuildExportAsync_Products_IncludesTheCatalogueAndStock()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await SeedProductAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Products, owner.Id);

        Assert.Equal("Products", data.Title);
        Assert.Contains("Product Code", data.Columns);
        Assert.Contains("MRP", data.Columns);

        var row = Assert.Single(data.Rows);
        Assert.Equal("PRD-000001", row[0]);
        Assert.Equal("Amul Butter 500g", row[1]);
        Assert.Equal("Staples", row[4]);
        Assert.Equal("Amul", row[5]);
        Assert.Equal("275", row[data.Columns.ToList().IndexOf("MRP")]);
        Assert.Equal("12", row[data.Columns.ToList().IndexOf("Stock On Hand")]);
    }

    [Fact]
    public async Task BuildExportAsync_Products_BlanksPurchasePriceWithoutTheCostPermission()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        await SeedProductAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Products, cashier.Id);

        var costIndex = data.Columns.ToList().IndexOf("Purchase Price");
        // Cashier has products.view but not pricing.viewPurchasePrice — export must not become a
        // side door around a restriction that applies everywhere else in the app.
        Assert.Equal(string.Empty, data.Rows.Single()[costIndex]);
    }

    [Fact]
    public async Task BuildExportAsync_Categories_And_Brands_CountTheirProducts()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await SeedProductAsync();
        var service = CreateService();

        var categories = await service.BuildExportAsync(ExportDataset.Categories, owner.Id);
        Assert.Equal(["Staples", "1", "Yes"], categories.Rows.Single());

        var brands = await service.BuildExportAsync(ExportDataset.Brands, owner.Id);
        Assert.Equal(["Amul", "1", "Yes"], brands.Rows.Single());
    }

    [Fact]
    public async Task BuildExportAsync_Customers_IncludesOutstandingCredit()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Customers.Add(new Customer
        {
            CustomerCode = "CUST-000001",
            Name = "Rajendra Godhade",
            Phone = "9876543210",
            CreditBalance = 480m,
        });
        await _fixture.Context.SaveChangesAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Customers, owner.Id);

        var row = data.Rows.Single();
        Assert.Equal("CUST-000001", row[0]);
        Assert.Equal("Rajendra Godhade", row[1]);
        Assert.Equal("9876543210", row[2]);
        Assert.Equal("480", row[data.Columns.ToList().IndexOf("Outstanding Credit")]);
    }

    [Fact]
    public async Task BuildExportAsync_Suppliers_IncludesOutstandingBalance()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Suppliers.Add(new Supplier
        {
            SupplierCode = "SUP-000001",
            Name = "Sharma Distributors",
            OutstandingBalance = 1518.40m,
        });
        await _fixture.Context.SaveChangesAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Suppliers, owner.Id);

        Assert.Equal("1518.4", data.Rows.Single()[data.Columns.ToList().IndexOf("Outstanding Balance")]);
    }

    [Fact]
    public async Task BuildExportAsync_Inventory_FlagsStockBelowMinimum()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var product = await SeedProductAsync();

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand = 2m; // minimum is 5
        await _fixture.Context.SaveChangesAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Inventory, owner.Id);

        Assert.Equal("Yes", data.Rows.Single()[data.Columns.ToList().IndexOf("Below Minimum")]);
        Assert.Equal("420", data.Rows.Single()[data.Columns.ToList().IndexOf("Stock Value")]);
    }

    [Fact]
    public async Task BuildExportAsync_Sales_RespectsAnOptionalDateWindow()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Sales.Add(new Sale
        {
            InvoiceNumber = "INV-2026-000001",
            SaleDateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            GrandTotal = 100m,
        });
        _fixture.Context.Sales.Add(new Sale
        {
            InvoiceNumber = "INV-2026-000002",
            SaleDateUtc = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            GrandTotal = 200m,
        });
        await _fixture.Context.SaveChangesAsync();
        var service = CreateService();

        var all = await service.BuildExportAsync(ExportDataset.Sales, owner.Id);
        Assert.Equal(2, all.Rows.Count);
        Assert.Contains("all time", all.Subtitle);

        var windowed = await service.BuildExportAsync(
            ExportDataset.Sales, owner.Id,
            fromUtc: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            toUtc: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("INV-2026-000002", windowed.Rows.Single()[0]);
    }

    [Fact]
    public async Task BuildExportAsync_Sales_LabelsAWalkInCustomer()
    {
        var owner = await _fixture.SeedOwnerAsync();
        _fixture.Context.Sales.Add(new Sale { InvoiceNumber = "INV-2026-000001", GrandTotal = 50m });
        await _fixture.Context.SaveChangesAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Sales, owner.Id);

        Assert.Equal("Walk-in", data.Rows.Single()[data.Columns.ToList().IndexOf("Customer")]);
    }

    [Fact]
    public async Task BuildExportAsync_Purchases_IncludesTheSupplierAndOutstanding()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var supplier = new Supplier { SupplierCode = "SUP-000001", Name = "Sharma Distributors" };
        _fixture.Context.Suppliers.Add(supplier);
        await _fixture.Context.SaveChangesAsync();

        _fixture.Context.Purchases.Add(new Purchase
        {
            PurchaseNumber = "PUR-2026-000001",
            SupplierId = supplier.Id,
            GrandTotal = 2452m,
            AmountPaid = 1000m,
            OutstandingAmount = 1452m,
        });
        await _fixture.Context.SaveChangesAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Purchases, owner.Id);

        var row = data.Rows.Single();
        Assert.Equal("PUR-2026-000001", row[0]);
        Assert.Equal("Sharma Distributors", row[2]);
        Assert.Equal("1452", row[data.Columns.ToList().IndexOf("Outstanding")]);
    }

    [Fact]
    public async Task BuildExportAsync_Expenses_UsesTheCategorySnapshot()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var category = new ExpenseCategory { Name = "Electricity" };
        _fixture.Context.ExpenseCategories.Add(category);
        await _fixture.Context.SaveChangesAsync();

        _fixture.Context.Expenses.Add(new Expense
        {
            ExpenseNumber = "EXP-000001",
            ExpenseCategoryId = category.Id,
            CategoryNameSnapshot = "Electricity",
            Amount = 1500m,
            PaymentMethod = PaymentMethod.Cash,
        });
        await _fixture.Context.SaveChangesAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Expenses, owner.Id);

        var row = data.Rows.Single();
        Assert.Equal("EXP-000001", row[0]);
        Assert.Equal("Electricity", row[2]);
        Assert.Equal("1500", row[3]);
        Assert.Equal("Cash", row[4]);
    }

    [Theory]
    [InlineData(ExportDataset.Customers)]
    [InlineData(ExportDataset.Suppliers)]
    [InlineData(ExportDataset.Inventory)]
    [InlineData(ExportDataset.Sales)]
    [InlineData(ExportDataset.Purchases)]
    [InlineData(ExportDataset.Expenses)]
    public async Task BuildExportAsync_RefusesDatasetsTheUserCannotSeeElsewhere(ExportDataset dataset)
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().BuildExportAsync(dataset, cashier.Id));
    }

    [Fact]
    public async Task BuildExportAsync_AllowsCatalogueDatasetsForACashier()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        await SeedProductAsync();
        var service = CreateService();

        // Cashier holds products.view, which is what these three datasets are gated on.
        Assert.Single((await service.BuildExportAsync(ExportDataset.Products, cashier.Id)).Rows);
        Assert.Single((await service.BuildExportAsync(ExportDataset.Categories, cashier.Id)).Rows);
        Assert.Single((await service.BuildExportAsync(ExportDataset.Brands, cashier.Id)).Rows);
    }

    [Fact]
    public async Task BuildExportAsync_RefusesAnAnonymousCaller()
    {
        await _fixture.SeedOwnerAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => CreateService().BuildExportAsync(ExportDataset.Products, performedByUserId: null));
    }

    [Fact]
    public async Task ExportedData_RoundTripsThroughTheExistingCsvAndExcelWriters()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await SeedProductAsync();

        var data = await CreateService().BuildExportAsync(ExportDataset.Products, owner.Id);
        var exportService = new ReportExportService(new NullAuditLogger());

        var csv = exportService.BuildCsv(data);
        Assert.Contains("Amul Butter 500g", csv);
        Assert.Contains("Product Code", csv);

        var xlsx = exportService.BuildExcel(data);
        // "PK" — a real zip container, which is what an .xlsx is.
        Assert.Equal([0x50, 0x4B], xlsx.Take(2).ToArray());
    }

    private sealed class NullAuditLogger : Kirana.Application.Abstractions.IAuditLogger
    {
        public Task RecordAsync(
            int? userId, string action, string entity, string? entityId = null,
            string? previousValue = null, string? newValue = null, string? reason = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
