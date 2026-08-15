using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Expenses;
using Kirana.Application.Reports;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>
/// Profit calculations (PRD §51 "Profit Reports"). Sales, purchases, returns and expenses all go
/// through their real services, so these numbers are proven to reconcile with what those services
/// actually persisted rather than a hand-built fixture.
/// </summary>
public class ProfitReportServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProfitReportService _sut;
    private readonly SaleService _saleService;
    private readonly SalesReturnService _returnService;
    private readonly ExpenseService _expenseService;
    private readonly ExpenseCategoryService _categoryService;
    private readonly int _ownerId;

    public ProfitReportServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _sut = new ProfitReportService(_fixture.Context, enforcer);
        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);
        _returnService = new SalesReturnService(_fixture.Context, seq, audit, enforcer);
        _expenseService = new ExpenseService(_fixture.Context, seq, audit, enforcer);
        _categoryService = new ExpenseCategoryService(_fixture.Context, audit, enforcer);
    }

    private async Task<Product> SeedProductAsync(decimal purchasePrice = 60, decimal sellingPrice = 100, decimal stock = 100)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Profit Test Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = purchasePrice,
            Mrp = sellingPrice + 10,
            SellingPrice = sellingPrice,
            IsActive = true,
        }.WithRetailPrice();
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
    public async Task Revenue_EqualsGrossSales_WhenNothingWasReturned()
    {
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 5);

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(500m, summary.GrossSales);
        Assert.Equal(0m, summary.Returns);
        Assert.Equal(500m, summary.Revenue);
    }

    [Fact]
    public async Task CostOfGoodsSold_UsesCurrentPurchasePriceTimesQuantitySold()
    {
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 5);

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(300m, summary.CostOfGoodsSold); // 5 * 60
    }

    [Fact]
    public async Task GrossProfit_IsRevenueMinusCostOfGoodsSold()
    {
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 5);

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(500m - 300m, summary.GrossProfit);
    }

    [Fact]
    public async Task Returns_ReduceRevenueAndCostOfGoodsSold()
    {
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await _returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleItem.Id, Quantity = 2 }],
            RefundMethod = RefundMethod.Cash,
            ProcessedByUserId = _ownerId,
        });

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(500m, summary.GrossSales);
        Assert.Equal(200m, summary.Returns); // 2 * 100
        Assert.Equal(300m, summary.Revenue); // 500 - 200
        Assert.Equal(180m, summary.CostOfGoodsSold); // (5*60) - (2*60)
        Assert.Equal(120m, summary.GrossProfit); // 300 - 180
    }

    [Fact]
    public async Task DamagedReturns_StillReduceCostOfGoodsSold()
    {
        // The goods are unsellable, but they were never successfully sold either — the original
        // COGS figure must still be reversed regardless of what happens to the stock afterwards.
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await _returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleItem.Id, Quantity = 1, Disposition = ReturnDisposition.Damaged }],
            RefundMethod = RefundMethod.Cash,
            ProcessedByUserId = _ownerId,
        });

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(240m, summary.CostOfGoodsSold); // (5*60) - (1*60)
    }

    [Fact]
    public async Task Expenses_ReduceNetProfitButNotGrossProfit()
    {
        var product = await SeedProductAsync(purchasePrice: 60, sellingPrice: 100);
        await SellAsync(product, 5);

        var category = await _categoryService.CreateAsync(new CreateExpenseCategoryRequest { Name = "Rent", PerformedByUserId = _ownerId });
        await _expenseService.CreateAsync(new CreateExpenseRequest { ExpenseCategoryId = category.Id, Amount = 80, PerformedByUserId = _ownerId });

        var summary = await _sut.GetSummaryAsync(TodayRange(), _ownerId);

        Assert.Equal(200m, summary.GrossProfit); // unaffected by expenses
        Assert.Equal(80m, summary.Expenses);
        Assert.Equal(120m, summary.NetProfit); // 200 - 80
    }

    [Fact]
    public async Task SalesOutsideTheRange_AreExcluded()
    {
        var product = await SeedProductAsync();
        await SellAsync(product, 5);

        var yesterday = ReportDateRange.Resolve(ReportDatePreset.Yesterday);
        var summary = await _sut.GetSummaryAsync(yesterday, _ownerId);

        Assert.Equal(0m, summary.GrossSales);
        Assert.Equal(0m, summary.Revenue);
    }

    [Fact]
    public async Task RequiresReportsViewProfitPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetSummaryAsync(TodayRange(), cashier.Id));
    }

    [Fact]
    public async Task ManagerAlsoLacksProfitPermission()
    {
        // Manager holds ReportsView but was deliberately NOT given ReportsViewProfit — margin is
        // the single most sensitive figure in the store (PRD §6, §9).
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User { Username = "mgr-profit", FullName = "Manager", PasswordHash = "x", Role = managerRole, IsActive = true };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetSummaryAsync(TodayRange(), manager.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
