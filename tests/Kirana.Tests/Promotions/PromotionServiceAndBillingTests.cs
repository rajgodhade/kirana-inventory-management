using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Printing;
using Kirana.Application.Promotions;
using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Promotions;

public sealed class PromotionServiceAndBillingTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly EfAuditLogger _audit;
    private readonly PermissionEnforcer _permissions;
    private readonly PromotionService _service;
    private User? _owner;

    public PromotionServiceAndBillingTests()
    {
        _audit = new EfAuditLogger(_fixture.Context);
        _permissions = new PermissionEnforcer(_fixture.Context);
        _service = new PromotionService(_fixture.Context, _audit, _permissions);
    }

    [Fact] public async Task Create_RequiresPermission() { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateAsync(Request(null))); }
    [Fact] public async Task Create_PersistsScheduleScopeAndAudit() { var owner = await Owner(); var p = await _service.CreateAsync(Request(owner.Id)); Assert.True(p.Id > 0); Assert.NotNull(p.Schedule); Assert.Equal(PromotionScopeType.EntireStore, p.Scope!.ScopeType); Assert.Equal("PromotionCreated", (await _fixture.Context.AuditLogs.OrderBy(x => x.Id).LastAsync()).Action); }
    [Fact] public async Task DuplicateCode_IsRejected() { var owner = await Owner(); await _service.CreateAsync(Request(owner.Id)); await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(Request(owner.Id))); }
    [Fact] public async Task EndBeforeStart_IsRejected() { var owner = await Owner(); var r = Request(owner.Id, start: DateTime.UtcNow, end: DateTime.UtcNow.AddMinutes(-1)); await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(r)); }
    [Theory] [InlineData(0)] [InlineData(100)] [InlineData(110)] public async Task InvalidPercentage_IsRejected(decimal percentage) { var owner = await Owner(); await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(Request(owner.Id, percentage: percentage))); }
    [Fact] public async Task OverlappingNonStackingPromotion_IsRejected() { var owner = await Owner(); await _service.CreateAsync(Request(owner.Id)); var second = Request(owner.Id, code: "SECOND"); await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(second)); }
    [Fact] public async Task ExpiredNonStackingPromotion_DoesNotBlockActivation() { var owner = await Owner(); await _service.CreateAsync(Request(owner.Id, code: "EXPIRED", start: DateTime.UtcNow.AddDays(-2), end: DateTime.UtcNow.AddDays(-1))); var next = await _service.CreateAsync(Request(owner.Id, code: "NEXT")); Assert.True(next.IsActive); Assert.Equal(PromotionStatus.Running, next.Status); }
    [Fact] public async Task StackingPromotions_MayOverlap() { var owner = await Owner(); await _service.CreateAsync(Request(owner.Id, code: "ONE", stacking: true)); var second = await _service.CreateAsync(Request(owner.Id, code: "TWO", stacking: true)); Assert.True(second.Id > 0); }
    [Fact] public async Task Deactivate_ChangesStatusAndAudits() { var owner = await Owner(); var p = await _service.CreateAsync(Request(owner.Id)); await _service.SetActiveAsync(p.Id, false, owner.Id); Assert.Equal(PromotionStatus.Disabled, (await _fixture.Context.Promotions.FindAsync(p.Id))!.Status); Assert.Contains(await _fixture.Context.AuditLogs.ToListAsync(), x => x.Action == "PromotionDeactivated"); }
    [Fact] public async Task Update_ChangesValueAndWritesAudit() { var owner = await Owner(); var p = await _service.CreateAsync(Request(owner.Id)); await _service.UpdateAsync(p.Id, Request(owner.Id, percentage: 15)); var updated = await _fixture.Context.Promotions.FindAsync(p.Id); Assert.Equal(15, updated!.Percentage); Assert.Contains(await _fixture.Context.AuditLogs.ToListAsync(), x => x.Action == "PromotionUpdated"); }
    [Fact] public async Task Delete_RemovesUnusedPromotion() { var owner = await Owner(); var p = await _service.CreateAsync(Request(owner.Id)); await _service.DeleteAsync(p.Id, owner.Id); Assert.Null(await _fixture.Context.Promotions.FindAsync(p.Id)); Assert.Contains(await _fixture.Context.AuditLogs.ToListAsync(), x => x.Action == "PromotionDeleted"); }

    [Fact]
    public async Task SaleService_AutomaticallyAppliesPromotionAndSnapshotsIt()
    {
        var product = await Product(); await DirectPromotion(product, 10);
        var saleService = new SaleService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), _audit, _permissions, new PromotionEngine(_fixture.Context));
        var sale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90, AmountTendered = 90 }],
        });
        Assert.Equal(10, sale.PromotionDiscountTotal); Assert.Equal(90, sale.GrandTotal);
        var item = Assert.Single(sale.Items); Assert.Equal(10, item.PromotionDiscountAmount);
        Assert.Equal("AUTO10", Assert.Single(item.Promotions).PromotionCodeSnapshot);
        Assert.Contains(await _fixture.Context.AuditLogs.ToListAsync(), x => x.Action == "PromotionApplied");
    }

    [Fact]
    public async Task SaleService_IncrementsUsageOncePerSale()
    {
        var product = await Product(); var promotion = await DirectPromotion(product, 10);
        var saleService = new SaleService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), _audit, _permissions, new PromotionEngine(_fixture.Context));
        await saleService.CompleteSaleAsync(new CompleteSaleRequest { Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }], Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90, AmountTendered = 90 }] });
        Assert.Equal(1, (await _fixture.Context.Promotions.FindAsync(promotion.Id))!.CurrentUsage);
    }

    [Fact]
    public async Task PromotionPricingFailure_DoesNotPartiallyCommitSaleUsageOrStock()
    {
        var product = await Product(); var promotion = await DirectPromotion(product, 10);
        var saleService = new SaleService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), _audit, _permissions, new PromotionEngine(_fixture.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100, AmountTendered = 100 }],
        }));
        Assert.Empty(await _fixture.Context.Sales.ToListAsync());
        Assert.Equal(0, (await _fixture.Context.Promotions.FindAsync(promotion.Id))!.CurrentUsage);
        Assert.Equal(20, (await _fixture.Context.Inventories.SingleAsync()).QuantityOnHand);
    }

    [Fact]
    public async Task Delete_RefusesPromotionUsedOnHistoricalSale()
    {
        var owner = await Owner(); var product = await Product(); var promotion = await DirectPromotion(product, 10);
        var saleService = new SaleService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), _audit, _permissions, new PromotionEngine(_fixture.Context));
        await saleService.CompleteSaleAsync(new CompleteSaleRequest { Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }], Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90, AmountTendered = 90 }] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(promotion.Id, owner.Id));
    }

    [Fact]
    public async Task Receipt_ContainsPromotionAndSavings()
    {
        var product = await Product(); await DirectPromotion(product, 10);
        var saleService = new SaleService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), _audit, _permissions, new PromotionEngine(_fixture.Context));
        var sale = await saleService.CompleteSaleAsync(new CompleteSaleRequest { Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }], Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90, AmountTendered = 90 }] });
        var document = new InvoiceDocumentBuilder().Build(sale, new Store { Name = "Test" });
        Assert.Equal(10, document.PromotionDiscountTotal); Assert.Equal("Automatic 10%", Assert.Single(document.Lines).PromotionText); Assert.True(document.HasSavings);
    }

    [Fact]
    public void LegacyAfterTaxPromotion_IsAppliedBeforeGst()
    {
        var totals = CartPricingCalculator.Calculate([new CartLine { ProductId = 1, Quantity = 1, UnitPrice = 100, IsTaxInclusive = false, GstRatePercent = 18, PromotionAfterTaxDiscountAmount = 10 }], 0, true);
        Assert.Equal(16.20m, totals.GstTotal); Assert.Equal(106, totals.GrandTotal); Assert.Equal(10, totals.PromotionDiscountTotal);
    }

    [Fact]
    public async Task PerformanceReport_UsesHistoricalApplications()
    {
        var owner = await Owner(); var product = await Product(); await DirectPromotion(product, 10);
        var saleService = new SaleService(_fixture.Context, new EfSequenceGenerator(_fixture.Context), _audit, _permissions, new PromotionEngine(_fixture.Context));
        await saleService.CompleteSaleAsync(new CompleteSaleRequest { Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }], Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90, AmountTendered = 90 }] });
        var rows = await _service.GetPerformanceAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), owner.Id);
        var row = Assert.Single(rows); Assert.Equal(10, row.DiscountGiven); Assert.Equal(1, row.SalesGenerated); Assert.Equal(1, row.ProductsSold);
    }

    private SavePromotionRequest Request(int? userId, string code = "DIWALI", decimal percentage = 10, bool stacking = false, DateTime? start = null, DateTime? end = null) => new()
    {
        PromotionCode = code, PromotionName = "Diwali Offer", PromotionType = PromotionType.Percentage, Percentage = percentage,
        StartAtUtc = start ?? DateTime.UtcNow.AddHours(-1), EndAtUtc = end ?? DateTime.UtcNow.AddDays(1), TimeZoneId = TimeZoneInfo.Utc.Id,
        ScopeType = PromotionScopeType.EntireStore, AllowStacking = stacking, ActivateImmediately = true, PerformedByUserId = userId,
    };

    private async Task<User> Owner()
    {
        if (_owner is not null) return _owner;
        await new FirstTimeSetupService(_fixture.Context, new BCryptPasswordHasher()).CompleteSetupAsync(new CompleteSetupRequest { StoreName = "Test", OwnerName = "Owner", AdminUsername = "admin", AdminFullName = "Owner", AdminPassword = "S3cure!Pass" });
        _owner = await _fixture.Context.Users.Include(x => x.Role).SingleAsync(); return _owner;
    }

    private async Task<Product> Product()
    {
        var p = new Product { ProductCode = "PRD-TEST", Name = "Milk", SellingPrice = 100, Mrp = 110, PurchasePrice = 80, IsActive = true };
        _fixture.Context.Products.Add(p); _fixture.Context.Inventories.Add(new Inventory { Product = p, QuantityOnHand = 20 }); await _fixture.Context.SaveChangesAsync(); return p;
    }

    private async Task<Promotion> DirectPromotion(Product product, decimal percentage)
    {
        var p = new Promotion { PromotionCode = "AUTO10", PromotionName = "Automatic 10%", PromotionType = PromotionType.Percentage, Percentage = percentage, IsActive = true, Status = PromotionStatus.Running, Schedule = new PromotionSchedule { StartAtUtc = DateTime.UtcNow.AddHours(-1), EndAtUtc = DateTime.UtcNow.AddHours(1), TimeZoneId = TimeZoneInfo.Utc.Id }, Scope = new PromotionScope { ScopeType = PromotionScopeType.Product, Targets = [new PromotionTarget { ProductId = product.Id }] } };
        _fixture.Context.Promotions.Add(p); await _fixture.Context.SaveChangesAsync(); return p;
    }

    public void Dispose() => _fixture.Dispose();
}
