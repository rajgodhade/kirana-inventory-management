using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Inventories;
using Kirana.Application.Purchasing;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>Dashboard KPIs (PRD §51). Point-in-time figures (inventory value, outstanding
/// balances) are checked to ignore the selected date range, since they are balances, not
/// period flows.</summary>
public class DashboardServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly DashboardService _sut;
    private readonly SaleService _saleService;
    private readonly SupplierService _supplierService;
    private readonly PurchaseService _purchaseService;
    private readonly int _ownerId;

    public DashboardServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);
        var inventoryService = new InventoryService(_fixture.Context, audit, enforcer);
        var profitService = new ProfitReportService(_fixture.Context, enforcer);

        _sut = new DashboardService(_fixture.Context, inventoryService, profitService, enforcer);
        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);
        _supplierService = new SupplierService(_fixture.Context, seq, audit, enforcer);
        _purchaseService = new PurchaseService(_fixture.Context, seq, audit, enforcer);
    }

    private async Task<Product> SeedProductAsync(decimal purchasePrice = 60, decimal sellingPrice = 100, decimal stock = 100, decimal minimumStock = 0)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Dashboard Test Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = purchasePrice,
            Mrp = sellingPrice + 10,
            SellingPrice = sellingPrice,
            MinimumStock = minimumStock,
            IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private Task<Sale> SellAsync(Product product, decimal quantity, int? customerId = null) =>
        _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customerId,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = product.SellingPrice * quantity, AmountTendered = product.SellingPrice * quantity }],
            CashierUserId = _ownerId,
        });

    private static ReportDateRange TodayRange() => ReportDateRange.Resolve(ReportDatePreset.Today);

    [Fact]
    public async Task TotalSales_ReconcilesWithSumOfSalesInRange()
    {
        var product = await SeedProductAsync(sellingPrice: 100);
        await SellAsync(product, 3);
        await SellAsync(product, 2);

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(500m, summary.TotalSales);
        Assert.Equal(2, summary.BillCount);
        Assert.Equal(5m, summary.ItemsSold);
    }

    [Fact]
    public async Task TotalPurchases_ReconcilesWithSumOfPurchasesInRange()
    {
        var supplier = await _supplierService.CreateAsync(new CreateSupplierRequest { Name = "Dash Supplier", PerformedByUserId = _ownerId });
        var product = await SeedProductAsync();
        await _purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 10, UnitPrice = 50 }],
            CreatedByUserId = _ownerId,
        });

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(500m, summary.TotalPurchases);
    }

    [Fact]
    public async Task InventoryValue_IgnoresTheSelectedDateRange()
    {
        var product = await SeedProductAsync(purchasePrice: 60, stock: 100);

        var todaySummary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);
        var lastYearSummary = await _sut.GetSummaryAsync(
            ReportDateRange.Resolve(ReportDatePreset.Custom, new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 2)), _ownerId);

        Assert.Equal(6000m, todaySummary.InventoryValue); // 100 * 60
        Assert.Equal(todaySummary.InventoryValue, lastYearSummary.InventoryValue);
    }

    [Fact]
    public async Task CustomerOutstanding_IgnoresTheSelectedDateRange()
    {
        var customer = new Customer { CustomerCode = "CUST-000001", Name = "Dash Customer", IsActive = true };
        _fixture.Context.Customers.Add(customer);
        await _fixture.Context.SaveChangesAsync();

        var product = await SeedProductAsync(sellingPrice: 100);
        await _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 3 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = 300 }],
            CashierUserId = _ownerId,
        });

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(300m, summary.CustomerOutstanding);

        var oldRangeSummary = await _sut.GetSummaryAsync(
            ReportDateRange.Resolve(ReportDatePreset.Custom, new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 2)), _ownerId);
        Assert.Equal(300m, oldRangeSummary.CustomerOutstanding);
    }

    [Fact]
    public async Task LowStockCount_ReflectsProductsAtOrBelowMinimum()
    {
        await SeedProductAsync(stock: 2, minimumStock: 5);
        await SeedProductAsync(stock: 50, minimumStock: 5);

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);
        Assert.Equal(1, summary.LowStockCount);
    }

    [Fact]
    public async Task ProfitFields_AreNull_ForAUserWithoutProfitPermission()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-dash", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        var product = await SeedProductAsync();
        await SellAsync(product, 1);

        var summary = await _sut.GetSummaryAsync(TodayRange(), manager.Id);

        Assert.False(summary.CanViewProfit);
        Assert.Null(summary.GrossProfit);
        Assert.Null(summary.NetProfit);
        // The rest of the dashboard is still usable — the whole call is not refused.
        Assert.Equal(100m, summary.TotalSales);
    }

    [Fact]
    public async Task ProfitFields_ArePopulated_ForOwner()
    {
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 1);

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.True(summary.CanViewProfit);
        Assert.Equal(40m, summary.GrossProfit);
    }

    [Fact]
    public async Task TopCustomers_RanksByRevenueDescending()
    {
        var big = new Customer { CustomerCode = "CUST-000001", Name = "Big Spender", IsActive = true };
        var small = new Customer { CustomerCode = "CUST-000002", Name = "Small Spender", IsActive = true };
        _fixture.Context.Customers.AddRange(big, small);
        await _fixture.Context.SaveChangesAsync();

        var product = await SeedProductAsync(sellingPrice: 100);
        await SellAsync(product, 5, big.Id);
        await SellAsync(product, 1, small.Id);

        var top = await _sut.GetTopCustomersAsync(TodayRange(), _ownerId, take: 5);

        Assert.Equal("Big Spender", top[0].Name);
        Assert.Equal(500m, top[0].Amount);
        Assert.Equal("Small Spender", top[1].Name);
    }

    [Fact]
    public async Task RecentSales_ReturnsNewestFirst()
    {
        var product = await SeedProductAsync();
        var first = await SellAsync(product, 1);
        var second = await SellAsync(product, 1);

        var recent = await _sut.GetRecentSalesAsync(_ownerId, take: 5);

        Assert.Equal(second.InvoiceNumber, recent[0].InvoiceNumber);
        Assert.Equal(first.InvoiceNumber, recent[1].InvoiceNumber);
    }

    [Fact]
    public async Task Charts_ReturnPointsForEveryRequestedSeries()
    {
        var product = await SeedProductAsync();
        await SellAsync(product, 2);

        var charts = await _sut.GetChartsAsync(TodayRange(), _ownerId);

        Assert.NotEmpty(charts.DailySalesTrend.Points);
        Assert.NotEmpty(charts.WeeklySales.Points);
        Assert.NotEmpty(charts.MonthlySales.Points);
        Assert.NotEmpty(charts.PaymentMethodDistribution.Points);
        Assert.True(charts.DailySalesTrend.Points.Sum(p => p.Value) >= 200m);
    }

    [Fact]
    public async Task Charts_GrossProfitTrend_IsEmpty_WithoutProfitPermission()
    {
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-chart", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        var charts = await _sut.GetChartsAsync(TodayRange(), manager.Id);

        Assert.Empty(charts.GrossProfitTrend.Points);
    }

    [Fact]
    public async Task RequiresReportsViewPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetSummaryAsync(TodayRange(), cashier.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetChartsAsync(TodayRange(), cashier.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
