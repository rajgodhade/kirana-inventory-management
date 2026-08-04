using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Authentication;

public class PermissionEnforcerTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly PermissionEnforcer _sut;

    public PermissionEnforcerTests()
    {
        _sut = new PermissionEnforcer(_fixture.Context);
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsTrue_ForOwnerWithPermission()
    {
        var owner = await _fixture.SeedOwnerAsync();

        Assert.True(await _sut.HasPermissionAsync(owner.Id, PermissionKeys.UsersManage));
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsFalse_ForCashierWithoutPermission()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();

        Assert.False(await _sut.HasPermissionAsync(cashier.Id, PermissionKeys.UsersManage));
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsFalse_ForNullUserId()
    {
        Assert.False(await _sut.HasPermissionAsync(null, PermissionKeys.ProductsEdit));
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsFalse_ForUnknownUserId()
    {
        Assert.False(await _sut.HasPermissionAsync(999, PermissionKeys.ProductsEdit));
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsFalse_ForInactiveUser()
    {
        var owner = await _fixture.SeedOwnerAsync();
        owner.IsActive = false;
        await _fixture.Context.SaveChangesAsync();

        Assert.False(await _sut.HasPermissionAsync(owner.Id, PermissionKeys.UsersManage));
    }

    [Fact]
    public async Task EnsureHasPermissionAsync_Throws_WhenNotPermitted()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.EnsureHasPermissionAsync(null, PermissionKeys.ProductsEdit));
    }

    [Fact]
    public async Task EnsureHasPermissionAsync_DoesNotThrow_WhenPermitted()
    {
        var owner = await _fixture.SeedOwnerAsync();

        await _sut.EnsureHasPermissionAsync(owner.Id, PermissionKeys.ProductsEdit);
    }

    public void Dispose() => _fixture.Dispose();
}
