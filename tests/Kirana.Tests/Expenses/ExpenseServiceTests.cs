using Kirana.Application.Authentication;
using Kirana.Application.Expenses;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Expenses;

/// <summary>Expense categories and expenses (PRD §32), including the rule that expenses must never
/// touch inventory.</summary>
public class ExpenseServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ExpenseCategoryService _categories;
    private readonly ExpenseService _sut;
    private readonly int _ownerId;

    public ExpenseServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _categories = new ExpenseCategoryService(_fixture.Context, audit, enforcer);
        _sut = new ExpenseService(_fixture.Context, seq, audit, enforcer);
    }

    private async Task<ExpenseCategory> CategoryAsync(string name = "Rent") =>
        await _categories.CreateAsync(new CreateExpenseCategoryRequest { Name = name, PerformedByUserId = _ownerId });

    private Task<Expense> CreateExpenseAsync(
        ExpenseCategory category, decimal amount = 5000, DateTime? date = null,
        PaymentMethod method = PaymentMethod.Cash, string? description = null) =>
        _sut.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = category.Id,
            Amount = amount,
            ExpenseDateUtc = date,
            PaymentMethod = method,
            Description = description,
            PerformedByUserId = _ownerId,
        });

    // ---------------------------------------------------------------- categories

    [Fact]
    public async Task CreateCategory_Works()
    {
        var category = await CategoryAsync("Electricity");

        Assert.Equal("Electricity", category.Name);
        Assert.True(category.IsActive);
    }

    [Fact]
    public async Task CreateCategory_Throws_OnDuplicateName()
    {
        await CategoryAsync("Rent");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CategoryAsync("rent"));
    }

    [Fact]
    public async Task CreateCategory_Throws_OnBlankName() =>
        await Assert.ThrowsAsync<ArgumentException>(() => CategoryAsync("   "));

    [Fact]
    public async Task UpdateCategory_RenamesIt()
    {
        var category = await CategoryAsync("Powr");

        var updated = await _categories.UpdateAsync(category.Id, new UpdateExpenseCategoryRequest
        {
            Name = "Power", Description = "Electricity bills", PerformedByUserId = _ownerId,
        });

        Assert.Equal("Power", updated.Name);
        Assert.Equal("Electricity bills", updated.Description);
    }

    [Fact]
    public async Task SetCategoryActive_TogglesIt()
    {
        var category = await CategoryAsync();

        Assert.False((await _categories.SetActiveAsync(category.Id, false, _ownerId)).IsActive);
        Assert.True((await _categories.SetActiveAsync(category.Id, true, _ownerId)).IsActive);
    }

    [Fact]
    public async Task InactiveCategory_CannotTakeNewExpenses()
    {
        var category = await CategoryAsync();
        await _categories.SetActiveAsync(category.Id, false, _ownerId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExpenseAsync(category));
        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteCategory_Works_WhenUnused()
    {
        var category = await CategoryAsync("Temporary");

        await _categories.DeleteAsync(category.Id, _ownerId);

        Assert.Empty(await _fixture.Context.ExpenseCategories.Where(c => c.Id == category.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteCategory_Throws_WhenExpensesExist()
    {
        var category = await CategoryAsync();
        await CreateExpenseAsync(category);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _categories.DeleteAsync(category.Id, _ownerId));
        Assert.Contains("Deactivate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteCategory_Throws_ForSeededDefaults()
    {
        await _categories.SeedDefaultsAsync();
        var rent = await _fixture.Context.ExpenseCategories.FirstAsync(c => c.Name == "Rent");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _categories.DeleteAsync(rent.Id, _ownerId));
        Assert.Contains("built-in", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedDefaults_CreatesTheStandardHeadings()
    {
        await _categories.SeedDefaultsAsync();

        var names = await _fixture.Context.ExpenseCategories.Select(c => c.Name).ToListAsync();
        Assert.Contains("Rent", names);
        Assert.Contains("Electricity", names);
        Assert.Contains("Miscellaneous", names);
        Assert.Equal(ExpenseCategoryService.Defaults.Count, names.Count);
    }

    [Fact]
    public async Task SeedDefaults_IsIdempotent()
    {
        await _categories.SeedDefaultsAsync();
        await _categories.SeedDefaultsAsync();
        await _categories.SeedDefaultsAsync();

        Assert.Equal(ExpenseCategoryService.Defaults.Count, await _fixture.Context.ExpenseCategories.CountAsync());
    }

    [Fact]
    public async Task SeedDefaults_DoesNotDisturbExistingCategories()
    {
        // Mirrors an upgrade: the store already made its own "Rent" before the defaults shipped.
        var mine = await CategoryAsync("Rent");
        await _categories.SeedDefaultsAsync();

        var rents = await _fixture.Context.ExpenseCategories.Where(c => c.Name == "Rent").ToListAsync();
        var kept = Assert.Single(rents);
        Assert.Equal(mine.Id, kept.Id);
        Assert.False(kept.IsSystemDefault);
    }

    // ---------------------------------------------------------------- expenses

    [Fact]
    public async Task CreateExpense_Works()
    {
        var category = await CategoryAsync();

        var expense = await CreateExpenseAsync(category, amount: 12000, description: "November rent");

        Assert.StartsWith("EXP-", expense.ExpenseNumber);
        Assert.Equal(12000m, expense.Amount);
        Assert.Equal("Rent", expense.CategoryNameSnapshot);
        Assert.Equal(_ownerId, expense.CreatedByUserId);
    }

    [Fact]
    public async Task ExpenseNumbers_AreSequential()
    {
        var category = await CategoryAsync();

        var first = await CreateExpenseAsync(category);
        var second = await CreateExpenseAsync(category);

        Assert.Equal("EXP-000001", first.ExpenseNumber);
        Assert.Equal("EXP-000002", second.ExpenseNumber);
    }

    [Fact]
    public async Task CreateExpense_Throws_OnNonPositiveAmount()
    {
        var category = await CategoryAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => CreateExpenseAsync(category, amount: 0));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateExpenseAsync(category, amount: -5));
    }

    [Fact]
    public async Task CreateExpense_Throws_WhenCategoryMissing() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync(new CreateExpenseRequest
        {
            ExpenseCategoryId = 999, Amount = 100, PerformedByUserId = _ownerId,
        }));

    [Fact]
    public async Task Expense_NeverTouchesInventoryOrStockMovements()
    {
        var product = new Product
        {
            ProductCode = "PRD-EXP001", Name = "Untouched", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10, Mrp = 20, SellingPrice = 18, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 25 });
        await _fixture.Context.SaveChangesAsync();

        var category = await CategoryAsync();
        await CreateExpenseAsync(category, amount: 9000);

        Assert.Equal(25m, (await _fixture.Context.Inventories.AsNoTracking().FirstAsync()).QuantityOnHand);
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task UpdateExpense_ChangesAmountAndCategory()
    {
        var rent = await CategoryAsync("Rent");
        var power = await CategoryAsync("Electricity");
        var expense = await CreateExpenseAsync(rent, amount: 1000);

        var updated = await _sut.UpdateAsync(expense.Id, new UpdateExpenseRequest
        {
            ExpenseCategoryId = power.Id,
            Amount = 2500,
            ExpenseDateUtc = DateTime.UtcNow,
            PaymentMethod = PaymentMethod.Upi,
            PerformedByUserId = _ownerId,
        });

        Assert.Equal(2500m, updated.Amount);
        Assert.Equal("Electricity", updated.CategoryNameSnapshot);
        Assert.Equal(PaymentMethod.Upi, updated.PaymentMethod);
    }

    [Fact]
    public async Task DeleteExpense_RemovesIt()
    {
        var category = await CategoryAsync();
        var expense = await CreateExpenseAsync(category);

        await _sut.DeleteAsync(expense.Id, _ownerId);

        Assert.Null(await _sut.GetByIdAsync(expense.Id, _ownerId));
    }

    [Fact]
    public async Task RenamingACategory_DoesNotRewritePastExpenses()
    {
        var category = await CategoryAsync("Rent");
        var expense = await CreateExpenseAsync(category);

        await _categories.UpdateAsync(category.Id, new UpdateExpenseCategoryRequest
        {
            Name = "Shop Rent", PerformedByUserId = _ownerId,
        });

        var reloaded = await _sut.GetByIdAsync(expense.Id, _ownerId);
        Assert.Equal("Rent", reloaded!.CategoryNameSnapshot);
    }

    // ---------------------------------------------------------------- search & totals

    [Fact]
    public async Task Search_FiltersByCategoryDateAndMethod()
    {
        var rent = await CategoryAsync("Rent");
        var power = await CategoryAsync("Electricity");

        await CreateExpenseAsync(rent, 1000, DateTime.UtcNow.AddDays(-10), PaymentMethod.Cash);
        await CreateExpenseAsync(power, 2000, DateTime.UtcNow.AddDays(-1), PaymentMethod.Upi);

        Assert.Single(await _sut.SearchAsync(new ExpenseSearchQuery { ExpenseCategoryId = rent.Id }, _ownerId));
        Assert.Single(await _sut.SearchAsync(new ExpenseSearchQuery { PaymentMethod = PaymentMethod.Upi }, _ownerId));
        Assert.Single(await _sut.SearchAsync(
            new ExpenseSearchQuery { FromDateUtc = DateTime.UtcNow.AddDays(-3) }, _ownerId));
    }

    [Fact]
    public async Task Search_ByTextMatchesNumberCategoryAndDescription()
    {
        var category = await CategoryAsync("Transport");
        var expense = await CreateExpenseAsync(category, description: "Tempo hire for Diwali stock");

        Assert.Single(await _sut.SearchAsync(new ExpenseSearchQuery { SearchText = expense.ExpenseNumber }, _ownerId));
        Assert.Single(await _sut.SearchAsync(new ExpenseSearchQuery { SearchText = "Transport" }, _ownerId));
        Assert.Single(await _sut.SearchAsync(new ExpenseSearchQuery { SearchText = "Diwali" }, _ownerId));
    }

    [Fact]
    public async Task Totals_SumTheWholeFilteredSetNotJustThePage()
    {
        var rent = await CategoryAsync("Rent");
        var power = await CategoryAsync("Electricity");

        await CreateExpenseAsync(rent, 10000);
        await CreateExpenseAsync(rent, 5000);
        await CreateExpenseAsync(power, 2500);

        // MaxResults deliberately smaller than the number of rows.
        var totals = await _sut.GetTotalsAsync(new ExpenseSearchQuery { MaxResults = 1 }, _ownerId);

        Assert.Equal(17500m, totals.TotalAmount);
        Assert.Equal(3, totals.Count);
        Assert.Equal(15000m, totals.ByCategory.First(c => c.CategoryName == "Rent").TotalAmount);
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task ExpenseOperations_AreAudited()
    {
        var category = await CategoryAsync();
        var expense = await CreateExpenseAsync(category);

        await _sut.UpdateAsync(expense.Id, new UpdateExpenseRequest
        {
            ExpenseCategoryId = category.Id, Amount = 99, ExpenseDateUtc = DateTime.UtcNow, PerformedByUserId = _ownerId,
        });
        await _sut.DeleteAsync(expense.Id, _ownerId);

        var actions = await _fixture.Context.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains("ExpenseCategoryCreated", actions);
        Assert.Contains("ExpenseCreated", actions);
        Assert.Contains("ExpenseUpdated", actions);
        Assert.Contains("ExpenseDeleted", actions);
    }

    [Fact]
    public async Task CategoryLifecycle_IsAudited()
    {
        var category = await CategoryAsync("Packaging");
        await _categories.UpdateAsync(category.Id, new UpdateExpenseCategoryRequest { Name = "Packing", PerformedByUserId = _ownerId });
        await _categories.SetActiveAsync(category.Id, false, _ownerId);
        await _categories.SetActiveAsync(category.Id, true, _ownerId);

        var actions = await _fixture.Context.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains("ExpenseCategoryUpdated", actions);
        Assert.Contains("ExpenseCategoryDeactivated", actions);
        Assert.Contains("ExpenseCategoryReactivated", actions);
    }

    public void Dispose() => _fixture.Dispose();
}
