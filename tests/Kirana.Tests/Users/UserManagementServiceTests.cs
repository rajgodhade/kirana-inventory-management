using Kirana.Application.Authentication;
using Kirana.Application.Users;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Users;

public class UserManagementServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly UserManagementService _sut;
    private readonly int _ownerId;

    public UserManagementServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _sut = new UserManagementService(
            _fixture.Context, new BCryptPasswordHasher(), new EfAuditLogger(_fixture.Context), new PermissionEnforcer(_fixture.Context));
    }

    private async Task<int> GetRoleIdAsync(string name) =>
        (await _fixture.Context.Roles.SingleAsync(r => r.Name == name)).Id;

    private CreateUserRequest ValidCreateRequest(string username, int roleId, string? pin = null) => new()
    {
        Username = username,
        FullName = "New User",
        Password = "Sup3rSecret!",
        Pin = pin,
        RoleId = roleId,
    };

    [Fact]
    public async Task CreateAsync_CreatesUserWithHashedPasswordAndRole()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");

        var user = await _sut.CreateAsync(ValidCreateRequest("cashier1", cashierRoleId), _ownerId);

        Assert.Equal("cashier1", user.Username);
        Assert.NotEqual("Sup3rSecret!", user.PasswordHash);
        Assert.True(new BCryptPasswordHasher().Verify("Sup3rSecret!", user.PasswordHash));
        Assert.Equal(cashierRoleId, user.RoleId);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task CreateAsync_HashesOptionalPin()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");

        var user = await _sut.CreateAsync(ValidCreateRequest("cashier2", cashierRoleId, pin: "4321"), _ownerId);

        Assert.NotNull(user.PinHash);
        Assert.True(new BCryptPasswordHasher().Verify("4321", user.PinHash!));
    }

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicateUsername()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        await _sut.CreateAsync(ValidCreateRequest("dupuser", cashierRoleId), _ownerId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync(ValidCreateRequest("dupuser", cashierRoleId), _ownerId));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPasswordTooShort()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        var request = new CreateUserRequest { Username = "shortpw", FullName = "X", Password = "abc", RoleId = cashierRoleId };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request, _ownerId));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPinIsNotFourToSixDigits()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");

        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(ValidCreateRequest("badpin", cashierRoleId, pin: "12"), _ownerId));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(ValidCreateRequest("badpin2", cashierRoleId, pin: "abcd"), _ownerId));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerLacksUsersManagePermission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        var cashierRoleId = await GetRoleIdAsync("Cashier");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.CreateAsync(ValidCreateRequest("newuser", cashierRoleId), cashier.Id));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerIsNull()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.CreateAsync(ValidCreateRequest("newuser", cashierRoleId), performedByUserId: null));
    }

    [Fact]
    public async Task UpdateAsync_ChangesFullNameAndRole()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        var managerRoleId = await GetRoleIdAsync("Manager");
        var user = await _sut.CreateAsync(ValidCreateRequest("promoteme", cashierRoleId), _ownerId);

        var updated = await _sut.UpdateAsync(user.Id, new UpdateUserRequest { FullName = "Promoted User", RoleId = managerRoleId }, _ownerId);

        Assert.Equal("Promoted User", updated.FullName);
        Assert.Equal(managerRoleId, updated.RoleId);
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatesAndReactivatesUser()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        var user = await _sut.CreateAsync(ValidCreateRequest("togglable", cashierRoleId), _ownerId);

        await _sut.SetActiveAsync(user.Id, isActive: false, _ownerId);
        var deactivated = (await _sut.GetAllUsersAsync()).Single(u => u.Id == user.Id);
        Assert.False(deactivated.IsActive);

        await _sut.SetActiveAsync(user.Id, isActive: true, _ownerId);
        var reactivated = (await _sut.GetAllUsersAsync()).Single(u => u.Id == user.Id);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_Throws_WhenDeactivatingTheLastActiveOwner()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SetActiveAsync(_ownerId, isActive: false, _ownerId));
    }

    [Fact]
    public async Task SetActiveAsync_Succeeds_WhenAnotherActiveOwnerExists()
    {
        var ownerRoleId = await GetRoleIdAsync("Owner");
        var secondOwner = await _sut.CreateAsync(ValidCreateRequest("owner2", ownerRoleId), _ownerId);

        await _sut.SetActiveAsync(_ownerId, isActive: false, secondOwner.Id);

        var original = (await _sut.GetAllUsersAsync()).Single(u => u.Id == _ownerId);
        Assert.False(original.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenReassigningLastActiveOwnersRoleAway()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateAsync(_ownerId, new UpdateUserRequest { FullName = "Test Owner", RoleId = cashierRoleId }, _ownerId));
    }

    [Fact]
    public async Task ResetPasswordAsync_ChangesHashAndClearsLockout()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        var user = await _sut.CreateAsync(ValidCreateRequest("lockeduser", cashierRoleId), _ownerId);

        var entity = await _fixture.Context.Users.FirstAsync(u => u.Id == user.Id);
        entity.FailedLoginAttempts = 5;
        entity.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
        await _fixture.Context.SaveChangesAsync();

        await _sut.ResetPasswordAsync(user.Id, "BrandNewPass1", _ownerId);

        var reloaded = await _fixture.Context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.True(new BCryptPasswordHasher().Verify("BrandNewPass1", reloaded.PasswordHash));
        Assert.Equal(0, reloaded.FailedLoginAttempts);
        Assert.Null(reloaded.LockedUntilUtc);
    }

    [Fact]
    public async Task SetPinAsync_SetsAndClearsPin()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        var user = await _sut.CreateAsync(ValidCreateRequest("pinuser", cashierRoleId), _ownerId);

        await _sut.SetPinAsync(user.Id, "9999", _ownerId);
        var withPin = await _fixture.Context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.True(new BCryptPasswordHasher().Verify("9999", withPin.PinHash!));

        await _sut.SetPinAsync(user.Id, null, _ownerId);
        var cleared = await _fixture.Context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Null(cleared.PinHash);
    }

    [Fact]
    public async Task UnlockAccountAsync_ClearsFailedAttemptsAndLock()
    {
        var cashierRoleId = await GetRoleIdAsync("Cashier");
        var user = await _sut.CreateAsync(ValidCreateRequest("relockeduser", cashierRoleId), _ownerId);

        var entity = await _fixture.Context.Users.FirstAsync(u => u.Id == user.Id);
        entity.FailedLoginAttempts = 5;
        entity.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
        await _fixture.Context.SaveChangesAsync();

        await _sut.UnlockAccountAsync(user.Id, _ownerId);

        var reloaded = await _fixture.Context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(0, reloaded.FailedLoginAttempts);
        Assert.Null(reloaded.LockedUntilUtc);
    }

    [Fact]
    public async Task GetRolesAsync_ReturnsAllThreeSystemRoles()
    {
        var roles = await _sut.GetRolesAsync();

        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, r => r.Name == "Owner");
        Assert.Contains(roles, r => r.Name == "Manager");
        Assert.Contains(roles, r => r.Name == "Cashier");
    }

    public void Dispose() => _fixture.Dispose();
}
