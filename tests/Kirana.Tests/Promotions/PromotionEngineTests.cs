using Kirana.Application.Promotions;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Promotions;

public sealed class PromotionEngineTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly PromotionEngine _engine;
    public PromotionEngineTests() => _engine = new PromotionEngine(_fixture.Context);

    [Fact] public async Task EntireStore_Percentage_Applies() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], percentage: 10); Assert.Equal(10, (await Evaluate(p)).Single().DiscountAmount); }
    [Fact] public async Task CategoryScope_OnlyMatchesCategory() { var c = new Category { Name = "Grocery" }; _fixture.Context.Categories.Add(c); await _fixture.Context.SaveChangesAsync(); var yes = await Product(categoryId: c.Id); var no = await Product("Other"); await Promo(PromotionScopeType.Category, [c.Id], percentage: 15); Assert.Equal(15, (await Evaluate(yes)).Single().DiscountAmount); Assert.Equal(0, (await Evaluate(no)).Single().DiscountAmount); }
    [Fact] public async Task BrandScope_OnlyMatchesBrand() { var b = new Brand { Name = "Amul" }; _fixture.Context.Brands.Add(b); await _fixture.Context.SaveChangesAsync(); var yes = await Product(brandId: b.Id); var no = await Product("Other"); await Promo(PromotionScopeType.Brand, [b.Id], percentage: 20); Assert.Equal(20, (await Evaluate(yes)).Single().DiscountAmount); Assert.Equal(0, (await Evaluate(no)).Single().DiscountAmount); }
    [Fact] public async Task ProductScope_OnlyMatchesSelectedProduct() { var yes = await Product(); var no = await Product("Other"); await Promo(PromotionScopeType.Product, [yes.Id], percentage: 25); Assert.Equal(25, (await Evaluate(yes)).Single().DiscountAmount); Assert.Equal(0, (await Evaluate(no)).Single().DiscountAmount); }
    [Fact] public async Task Inactive_IsIgnored() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], percentage: 10, active: false); Assert.Equal(0, (await Evaluate(p)).Single().DiscountAmount); }
    [Fact] public async Task Expired_IsIgnored() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], percentage: 10, start: DateTime.UtcNow.AddDays(-2), end: DateTime.UtcNow.AddDays(-1)); Assert.Equal(0, (await Evaluate(p)).Single().DiscountAmount); }
    [Fact] public async Task FutureSchedule_IsIgnored() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], percentage: 10, start: DateTime.UtcNow.AddDays(1), end: DateTime.UtcNow.AddDays(2)); Assert.Equal(0, (await Evaluate(p)).Single().DiscountAmount); }
    [Fact] public async Task MinimumQuantity_IsEnforced() { var p = await Product(); var promo = await Promo(PromotionScopeType.EntireStore, [], percentage: 10); promo.MinimumQuantity = 2; await _fixture.Context.SaveChangesAsync(); Assert.Equal(0, (await Evaluate(p, 1)).Single().DiscountAmount); Assert.Equal(20, (await Evaluate(p, 2)).Single().DiscountAmount); }
    [Fact] public async Task MinimumBill_IsEnforced() { var p = await Product(); var promo = await Promo(PromotionScopeType.EntireStore, [], percentage: 10); promo.MinimumBillAmount = 200; await _fixture.Context.SaveChangesAsync(); Assert.Equal(0, (await Evaluate(p, bill: 100)).Single().DiscountAmount); Assert.Equal(10, (await Evaluate(p, bill: 200)).Single().DiscountAmount); }
    [Fact] public async Task HighestDiscount_Wins_WhenNotStacking() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], "TEN", 10); await Promo(PromotionScopeType.EntireStore, [], "TWENTY", 20); var r = (await Evaluate(p)).Single(); Assert.Single(r.AppliedPromotions); Assert.Equal("TWENTY", r.AppliedPromotions[0].PromotionCode); }
    [Fact] public async Task HighestPriority_Wins_WhenConfigured() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], "BIG", 20, priority: 1); await Promo(PromotionScopeType.EntireStore, [], "PRIORITY", 5, priority: 20, mode: PromotionPriorityMode.HighestPriority); var r = (await Evaluate(p)).Single(); Assert.Equal("PRIORITY", Assert.Single(r.AppliedPromotions).PromotionCode); }
    [Fact] public async Task Stacking_AppliesEveryStackablePromotion() { var p = await Product(); await Promo(PromotionScopeType.EntireStore, [], "A", 10, stacking: true); await Promo(PromotionScopeType.EntireStore, [], "B", 10, stacking: true); var r = (await Evaluate(p)).Single(); Assert.Equal(2, r.AppliedPromotions.Count); Assert.Equal(19, r.DiscountAmount); }
    [Fact] public async Task MaximumDiscount_CapsBenefit() { var p = await Product(); var promo = await Promo(PromotionScopeType.EntireStore, [], percentage: 50); promo.MaximumDiscount = 12; await _fixture.Context.SaveChangesAsync(); Assert.Equal(12, (await Evaluate(p)).Single().DiscountAmount); }
    [Fact] public async Task MaximumUsage_IsEnforced() { var p = await Product(); var promo = await Promo(PromotionScopeType.EntireStore, [], percentage: 10); promo.MaximumUsage = 2; promo.CurrentUsage = 2; await _fixture.Context.SaveChangesAsync(); Assert.Equal(0, (await Evaluate(p)).Single().DiscountAmount); }
    [Fact] public async Task FlatAmount_IsPerUnit() { var p = await Product(); var promo = await Promo(PromotionScopeType.EntireStore, [], percentage: 10); promo.PromotionType = PromotionType.FlatAmount; promo.Percentage = null; promo.FlatAmount = 7; await _fixture.Context.SaveChangesAsync(); Assert.Equal(14, (await Evaluate(p, 2)).Single().DiscountAmount); }
    [Fact] public async Task FixedPrice_ReducesToConfiguredPrice() { var p = await Product(); var promo = await Promo(PromotionScopeType.EntireStore, [], percentage: 10); promo.PromotionType = PromotionType.FixedSellingPrice; promo.Percentage = null; promo.FixedPrice = 75; await _fixture.Context.SaveChangesAsync(); var result = (await Evaluate(p)).Single(); Assert.Equal(25, result.DiscountAmount); Assert.Equal(75, result.FinalUnitPrice); }
    [Fact] public void StatusCalculator_TransitionsWithoutManualAction() { var p = new Promotion { IsActive = true, Status = PromotionStatus.Scheduled, Schedule = new PromotionSchedule { StartAtUtc = DateTime.UtcNow.AddMinutes(-1), EndAtUtc = DateTime.UtcNow.AddMinutes(1) } }; Assert.Equal(PromotionStatus.Running, PromotionStatusCalculator.Calculate(p, DateTime.UtcNow)); Assert.Equal(PromotionStatus.Scheduled, PromotionStatusCalculator.Calculate(p, DateTime.UtcNow.AddMinutes(-2))); Assert.Equal(PromotionStatus.Expired, PromotionStatusCalculator.Calculate(p, DateTime.UtcNow.AddMinutes(2))); }
    [Fact] public void StatusCalculator_RespectsDailyWindow() { var now = DateTime.UtcNow; var p = new Promotion { IsActive = true, Schedule = new PromotionSchedule { StartAtUtc = now.AddDays(-1), EndAtUtc = now.AddDays(1), TimeZoneId = TimeZoneInfo.Utc.Id, DailyStartTime = now.TimeOfDay.Add(TimeSpan.FromHours(1)), DailyEndTime = now.TimeOfDay.Add(TimeSpan.FromHours(2)) } }; Assert.Equal(PromotionStatus.Scheduled, PromotionStatusCalculator.Calculate(p, now)); }

    private async Task<Product> Product(string name = "Milk", int? categoryId = null, int? brandId = null)
    {
        var p = new Product { ProductCode = Guid.NewGuid().ToString("N")[..12], Name = name, SellingPrice = 100, Mrp = 110, PurchasePrice = 80, CategoryId = categoryId, BrandId = brandId, IsActive = true };
        _fixture.Context.Products.Add(p); await _fixture.Context.SaveChangesAsync(); return p;
    }
    private async Task<Promotion> Promo(PromotionScopeType scope, IReadOnlyList<int> ids, string code = "OFFER", decimal percentage = 10, int priority = 0, bool stacking = false, PromotionPriorityMode mode = PromotionPriorityMode.HighestDiscount, bool active = true, DateTime? start = null, DateTime? end = null)
    {
        var p = new Promotion { PromotionCode = code, PromotionName = code, PromotionType = PromotionType.Percentage, Percentage = percentage, Priority = priority, PriorityMode = mode, AllowStacking = stacking, IsActive = active, Status = active ? PromotionStatus.Running : PromotionStatus.Draft, Schedule = new PromotionSchedule { StartAtUtc = start ?? DateTime.UtcNow.AddHours(-1), EndAtUtc = end ?? DateTime.UtcNow.AddHours(1), TimeZoneId = TimeZoneInfo.Utc.Id }, Scope = new PromotionScope { ScopeType = scope } };
        foreach (var id in ids) p.Scope.Targets.Add(new PromotionTarget { CategoryId = scope == PromotionScopeType.Category ? id : null, BrandId = scope == PromotionScopeType.Brand ? id : null, ProductId = scope == PromotionScopeType.Product ? id : null });
        _fixture.Context.Promotions.Add(p); await _fixture.Context.SaveChangesAsync(); return p;
    }
    private Task<IReadOnlyList<PromotionLineResult>> Evaluate(Product p, decimal quantity = 1, decimal? bill = null) => _engine.EvaluateCartAsync(new PromotionCartContext { Lines = [new PromotionLineContext { ProductId = p.Id, Quantity = quantity, UnitPrice = p.SellingPrice }], BillAmount = bill ?? p.SellingPrice * quantity, AtUtc = DateTime.UtcNow });
    public void Dispose() => _fixture.Dispose();
}
