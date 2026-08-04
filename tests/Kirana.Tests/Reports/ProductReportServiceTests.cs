using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

public class ProductReportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductReportService _sut;
    private readonly SaleService _saleService;
    private readonly int _ownerId;

    public ProductReportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _sut = new ProductReportService(_fixture.Context, enforcer);
        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);
    }

    private async Task<Product> SeedProductAsync(
        string name, decimal purchasePrice = 60, decimal sellingPrice = 100, decimal stock = 1000, int? categoryId = null, int? brandId = null)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = purchasePrice,
            Mrp = sellingPrice + 10,
            SellingPrice = sellingPrice,
            CategoryId = categoryId,
            BrandId = brandId,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<Sale> SellAsync(Product product, decimal quantity) =>
        _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = product.SellingPrice * quantity, AmountTendered = product.SellingPrice * quantity }],
            CashierUserId = _ownerId,
        });

    private static ReportDateRange TodayRange() => ReportDateRange.Resolve(ReportDatePreset.Today);

    [Fact]
    public async Task MostSelling_OrdersByQuantityDescending()
    {
        var popular = await SeedProductAsync("Popular", sellingPrice: 10);
        var rare = await SeedProductAsync("Rare", sellingPrice: 10);
        await SellAsync(popular, 20);
        await SellAsync(rare, 2);

        var rows = await _sut.GetMostSellingAsync(TodayRange(), _ownerId);

        Assert.Equal("Popular", rows[0].ProductName);
        Assert.Equal(20m, rows[0].QuantitySold);
    }

    [Fact]
    public async Task LeastSelling_ExcludesProductsWithZeroSales()
    {
        var sold = await SeedProductAsync("Sold", sellingPrice: 10);
        await SeedProductAsync("NeverSold", sellingPrice: 10); // no sale
        await SellAsync(sold, 1);

        var rows = await _sut.GetLeastSellingAsync(TodayRange(), _ownerId);

        Assert.DoesNotContain(rows, r => r.ProductName == "NeverSold");
        Assert.Contains(rows, r => r.ProductName == "Sold");
    }

    [Fact]
    public async Task HighestRevenue_OrdersByRevenueNotQuantity()
    {
        // Fewer units of the pricier product outsell more units of the cheap one in revenue.
        var expensive = await SeedProductAsync("Expensive", sellingPrice: 1000);
        var cheap = await SeedProductAsync("Cheap", sellingPrice: 5);
        await SellAsync(expensive, 2);   // 2000
        await SellAsync(cheap, 100);     // 500

        var rows = await _sut.GetHighestRevenueAsync(TodayRange(), _ownerId);

        Assert.Equal("Expensive", rows[0].ProductName);
        Assert.Equal(2000m, rows[0].Revenue);
    }

    [Fact]
    public async Task HighestProfit_RequiresProfitPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetHighestProfitProductsAsync(TodayRange(), cashier.Id));

        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-prod", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetHighestProfitProductsAsync(TodayRange(), manager.Id));
    }

    [Fact]
    public async Task HighestProfit_OrdersByEstimatedProfit()
    {
        // Slim margin, high volume vs. fat margin, low volume — profit ordering differs from revenue.
        var thinMargin = await SeedProductAsync("ThinMargin", purchasePrice: 95, sellingPrice: 100);
        var fatMargin = await SeedProductAsync("FatMargin", purchasePrice: 10, sellingPrice: 100);
        await SellAsync(thinMargin, 10); // revenue 1000, profit 50
        await SellAsync(fatMargin, 5);   // revenue 500, profit 450

        var rows = await _sut.GetHighestProfitProductsAsync(TodayRange(), _ownerId);

        Assert.Equal("FatMargin", rows[0].ProductName);
        Assert.Equal(450m, rows[0].EstimatedProfit);
    }

    [Fact]
    public async Task Profit_IsNull_WithoutProfitPermission_ForOtherProductReports()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-prod2", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        var product = await SeedProductAsync("X", purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 1);

        var rows = await _sut.GetMostSellingAsync(TodayRange(), manager.Id);

        Assert.All(rows, r => Assert.Null(r.EstimatedProfit));
    }

    [Fact]
    public async Task DeadStock_OnlyIncludesProductsWithStockAndZeroSalesInRange()
    {
        var dead = await SeedProductAsync("DeadOne", stock: 50);
        var moving = await SeedProductAsync("MovingOne", stock: 50);
        await SeedProductAsync("NoStock", stock: 0); // zero stock — not dead stock, just out of stock
        await SellAsync(moving, 1);

        var rows = await _sut.GetDeadStockAsync(TodayRange(), _ownerId);

        Assert.Contains(rows, r => r.ProductName == "DeadOne");
        Assert.DoesNotContain(rows, r => r.ProductName == "MovingOne");
        Assert.DoesNotContain(rows, r => r.ProductName == "NoStock");
    }

    [Fact]
    public async Task DeadStock_StockValue_IsQuantityTimesPurchasePrice()
    {
        await SeedProductAsync("Dead", purchasePrice: 40, stock: 25);

        var rows = await _sut.GetDeadStockAsync(TodayRange(), _ownerId);

        var row = Assert.Single(rows);
        Assert.Equal(1000m, row.StockValue); // 25 * 40
    }

    [Fact]
    public async Task CategoryWiseSales_GroupsCorrectly()
    {
        var category = new Category { Name = "Beverages", IsActive = true };
        _fixture.Context.Categories.Add(category);
        await _fixture.Context.SaveChangesAsync();

        var inCat = await SeedProductAsync("Cola", sellingPrice: 20, categoryId: category.Id);
        var noCat = await SeedProductAsync("Loose Item", sellingPrice: 10);
        await SellAsync(inCat, 5);
        await SellAsync(noCat, 3);

        var rows = await _sut.GetCategoryWiseSalesAsync(TodayRange(), _ownerId);

        Assert.Equal(100m, rows.Single(r => r.CategoryName == "Beverages").Revenue);
        Assert.Equal(30m, rows.Single(r => r.CategoryName == "Uncategorized").Revenue);
    }

    [Fact]
    public async Task BrandWiseSales_GroupsCorrectly()
    {
        var brand = new Brand { Name = "Amul", IsActive = true };
        _fixture.Context.Brands.Add(brand);
        await _fixture.Context.SaveChangesAsync();

        var branded = await SeedProductAsync("Amul Milk", sellingPrice: 30, brandId: brand.Id);
        await SellAsync(branded, 4);

        var rows = await _sut.GetBrandWiseSalesAsync(TodayRange(), _ownerId);

        Assert.Equal(120m, rows.Single(r => r.BrandName == "Amul").Revenue);
    }

    [Fact]
    public async Task ProductWiseSales_HonoursReportFilter()
    {
        var target = await SeedProductAsync("Target", sellingPrice: 10);
        var other = await SeedProductAsync("Other", sellingPrice: 10);
        await SellAsync(target, 3);
        await SellAsync(other, 7);

        var rows = await _sut.GetProductWiseSalesAsync(TodayRange(), new ReportFilter { ProductId = target.Id }, _ownerId);

        var row = Assert.Single(rows);
        Assert.Equal("Target", row.ProductName);
        Assert.Equal(3m, row.QuantitySold);
    }

    [Fact]
    public async Task ProductSalesRows_SnapshotSurvivesLaterPriceChanges()
    {
        var product = await SeedProductAsync("Renamed Later", purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 2);

        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.SellingPrice = 500;
        await _fixture.Context.SaveChangesAsync();

        var rows = await _sut.GetMostSellingAsync(TodayRange(), _ownerId);

        Assert.Equal(200m, rows.Single().Revenue); // still 2 * 100, not 2 * 500
    }

    public void Dispose() => _fixture.Dispose();
}
