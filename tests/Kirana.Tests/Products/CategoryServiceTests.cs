using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Products;

public class CategoryServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CategoryService _sut;
    private readonly int _ownerId;

    public CategoryServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _sut = new CategoryService(_fixture.Context, new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));
    }

    [Fact]
    public async Task CreateAsync_AddsCategory()
    {
        var category = await _sut.CreateAsync("Beverages", _ownerId);

        Assert.Equal("Beverages", category.Name);
        Assert.True(category.IsActive);
    }

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicateName()
    {
        await _sut.CreateAsync("Snacks", _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync("Snacks", _ownerId));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenNameBlank()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync("   ", _ownerId));
    }

    [Fact]
    public async Task GetAllAsync_ExcludesInactive_ByDefault()
    {
        var category = await _sut.CreateAsync("Dairy", _ownerId);
        await _sut.SetActiveAsync(category.Id, isActive: false, _ownerId);

        var active = await _sut.GetAllAsync();
        var all = await _sut.GetAllAsync(includeInactive: true);

        Assert.Empty(active);
        Assert.Single(all);
    }

    [Fact]
    public async Task RenameAsync_UpdatesName()
    {
        var category = await _sut.CreateAsync("Cleaning", _ownerId);

        await _sut.RenameAsync(category.Id, "Household Cleaning", _ownerId);

        var all = await _sut.GetAllAsync();
        Assert.Equal("Household Cleaning", all.Single().Name);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerLacksPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync("Beverages", cashier.Id));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerIsNull()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync("Beverages", performedByUserId: null));
    }

    public void Dispose() => _fixture.Dispose();
}
