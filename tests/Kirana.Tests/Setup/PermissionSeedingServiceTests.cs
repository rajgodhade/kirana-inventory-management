using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Setup;

public class PermissionSeedingServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly PermissionSeedingService _sut;

    public PermissionSeedingServiceTests()
    {
        _sut = new PermissionSeedingService(_fixture.Context);
    }

    [Fact]
    public async Task SyncPermissionsAsync_DoesNothing_WhenSetupNotCompleted()
    {
        await _sut.SyncPermissionsAsync();

        Assert.Equal(0, await _fixture.Context.Permissions.CountAsync());
    }

    [Fact]
    public async Task SyncPermissionsAsync_IsNoOp_WhenAllPermissionsAlreadyExist()
    {
        await _fixture.SeedOwnerAsync();
        var countBefore = await _fixture.Context.Permissions.CountAsync();
        var rolePermissionCountBefore = await _fixture.Context.RolePermissions.CountAsync();

        await _sut.SyncPermissionsAsync();

        Assert.Equal(countBefore, await _fixture.Context.Permissions.CountAsync());
        Assert.Equal(rolePermissionCountBefore, await _fixture.Context.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task SyncPermissionsAsync_AddsMissingPermission_AndGrantsItToDefaultRoles()
    {
        await _fixture.SeedOwnerAsync();

        // Simulate an older install predating PermissionKeys.SalesReprintInvoice: remove it and
        // its role links, as if this key never existed when the store was first set up.
        var reprintPermission = await _fixture.Context.Permissions.SingleAsync(p => p.Key == PermissionKeys.SalesReprintInvoice);
        var links = _fixture.Context.RolePermissions.Where(rp => rp.PermissionId == reprintPermission.Id);
        _fixture.Context.RolePermissions.RemoveRange(links);
        _fixture.Context.Permissions.Remove(reprintPermission);
        await _fixture.Context.SaveChangesAsync();

        Assert.False(await _fixture.Context.Permissions.AnyAsync(p => p.Key == PermissionKeys.SalesReprintInvoice));

        await _sut.SyncPermissionsAsync();

        var restored = await _fixture.Context.Permissions.SingleAsync(p => p.Key == PermissionKeys.SalesReprintInvoice);
        Assert.Equal("Reprint a completed invoice", restored.Description);

        var ownerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Owner");
        var managerRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var cashierRole = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Cashier");

        Assert.True(await _fixture.Context.RolePermissions.AnyAsync(rp => rp.RoleId == ownerRole.Id && rp.PermissionId == restored.Id));
        Assert.True(await _fixture.Context.RolePermissions.AnyAsync(rp => rp.RoleId == managerRole.Id && rp.PermissionId == restored.Id));
        Assert.False(await _fixture.Context.RolePermissions.AnyAsync(rp => rp.RoleId == cashierRole.Id && rp.PermissionId == restored.Id));
    }

    [Fact]
    public async Task SyncPermissionsAsync_IsIdempotent_WhenRunTwiceInARow()
    {
        await _fixture.SeedOwnerAsync();
        var reprintPermission = await _fixture.Context.Permissions.SingleAsync(p => p.Key == PermissionKeys.SalesReprintInvoice);
        _fixture.Context.RolePermissions.RemoveRange(_fixture.Context.RolePermissions.Where(rp => rp.PermissionId == reprintPermission.Id));
        _fixture.Context.Permissions.Remove(reprintPermission);
        await _fixture.Context.SaveChangesAsync();

        await _sut.SyncPermissionsAsync();
        var countAfterFirstRun = await _fixture.Context.Permissions.CountAsync(p => p.Key == PermissionKeys.SalesReprintInvoice);
        var rolePermissionCountAfterFirstRun = await _fixture.Context.RolePermissions.CountAsync();

        await _sut.SyncPermissionsAsync();

        Assert.Equal(countAfterFirstRun, await _fixture.Context.Permissions.CountAsync(p => p.Key == PermissionKeys.SalesReprintInvoice));
        Assert.Equal(rolePermissionCountAfterFirstRun, await _fixture.Context.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task SyncPermissionsAsync_DoesNotGrantNewPermission_ToCustomRoles()
    {
        await _fixture.SeedOwnerAsync();
        var customRole = new Role { Name = "Supervisor", IsSystemRole = false };
        _fixture.Context.Roles.Add(customRole);
        await _fixture.Context.SaveChangesAsync();

        var reprintPermission = await _fixture.Context.Permissions.SingleAsync(p => p.Key == PermissionKeys.SalesReprintInvoice);
        _fixture.Context.RolePermissions.RemoveRange(_fixture.Context.RolePermissions.Where(rp => rp.PermissionId == reprintPermission.Id));
        _fixture.Context.Permissions.Remove(reprintPermission);
        await _fixture.Context.SaveChangesAsync();

        await _sut.SyncPermissionsAsync();

        var restored = await _fixture.Context.Permissions.SingleAsync(p => p.Key == PermissionKeys.SalesReprintInvoice);
        Assert.False(await _fixture.Context.RolePermissions.AnyAsync(rp => rp.RoleId == customRole.Id && rp.PermissionId == restored.Id));
    }

    public void Dispose() => _fixture.Dispose();
}
