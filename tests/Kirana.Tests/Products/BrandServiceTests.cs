using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Products;

public class BrandServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BrandService _sut;
    private readonly int _ownerId;

    public BrandServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _sut = new BrandService(_fixture.Context, new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));
    }

    [Fact]
    public async Task CreateAsync_AddsBrand()
    {
        var brand = await _sut.CreateAsync("Amul", _ownerId);

        Assert.Equal("Amul", brand.Name);
        Assert.True(brand.IsActive);
    }

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicateName()
    {
        await _sut.CreateAsync("Tata", _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CreateAsync("Tata", _ownerId));
    }

    [Fact]
    public async Task SetActiveAsync_Deactivates()
    {
        var brand = await _sut.CreateAsync("Nestle", _ownerId);

        await _sut.SetActiveAsync(brand.Id, isActive: false, _ownerId);

        var all = await _sut.GetAllAsync(includeInactive: true);
        Assert.False(all.Single().IsActive);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerLacksPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync("Amul", cashier.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
