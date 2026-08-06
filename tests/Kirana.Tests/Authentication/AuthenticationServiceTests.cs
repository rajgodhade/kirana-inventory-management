using Kirana.Application.Authentication;
using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Authentication;

public class AuthenticationServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BCryptPasswordHasher _hasher = new();
    private readonly ManagementSession _session = new();
    private readonly AuthenticationService _sut;

    public AuthenticationServiceTests()
    {
        var auditLogger = new EfAuditLogger(_fixture.Context);
        _sut = new AuthenticationService(_fixture.Context, _hasher, auditLogger, _session);
    }

    private async Task SeedAdminAsync()
    {
        var setup = new FirstTimeSetupService(_fixture.Context, _hasher);
        await setup.CompleteSetupAsync(new CompleteSetupRequest
        {
            StoreName = "Sharma Kirana Store",
            OwnerName = "Ramesh Sharma",
            AdminUsername = "admin",
            AdminFullName = "Ramesh Sharma",
            AdminPassword = "S3cure!Pass",
            AdminPin = "1234",
        });
    }

    [Fact]
    public async Task LoginWithPasswordAsync_Succeeds_WithCorrectCredentials()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginWithPasswordAsync("admin", "S3cure!Pass");

        Assert.True(result.Success);
        Assert.True(_session.IsUnlocked);
        Assert.True(_session.HasPermission(PermissionKeys.UsersManage));
    }

    [Fact]
    public async Task LoginWithPasswordAsync_Fails_WithWrongPassword()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginWithPasswordAsync("admin", "wrong-password");

        Assert.False(result.Success);
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public async Task LoginWithPinAsync_Succeeds_WithCorrectPin()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginWithPinAsync("1234");

        Assert.True(result.Success);
        Assert.True(_session.IsUnlocked);
    }

    [Fact]
    public async Task LoginWithUsernameAndPinAsync_Succeeds_WithCorrectPinForNamedUser()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginWithUsernameAndPinAsync("admin", "1234");

        Assert.True(result.Success);
        Assert.True(_session.IsUnlocked);
    }

    [Fact]
    public async Task LoginWithUsernameAndPinAsync_Fails_WhenPinBelongsToAnotherUser()
    {
        await SeedAdminAsync();
        await SeedCashierAsync();

        var result = await _sut.LoginWithUsernameAndPinAsync("admin", "4321");

        Assert.False(result.Success);
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public async Task LockAndReturnToBilling_ClearsSession()
    {
        await SeedAdminAsync();
        await _sut.LoginWithPasswordAsync("admin", "S3cure!Pass");

        _sut.LockAndReturnToBilling();

        Assert.False(_session.IsUnlocked);
        Assert.Null(_session.CurrentUser);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_LocksAccount_AfterFiveFailedAttempts()
    {
        await SeedAdminAsync();

        for (var i = 0; i < 5; i++)
        {
            await _sut.LoginWithPasswordAsync("admin", "wrong-password");
        }

        var result = await _sut.LoginWithPasswordAsync("admin", "S3cure!Pass");

        Assert.False(result.Success);
        Assert.Contains("locked", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<User> SeedCashierAsync()
    {
        var cashierRole = await _fixture.Context.Roles.FirstAsync(r => r.Name == "Cashier");
        var cashier = new User
        {
            Username = "cashier1",
            FullName = "Cashier One",
            PasswordHash = _hasher.Hash("Cashier@123"),
            PinHash = _hasher.Hash("4321"),
            Role = cashierRole,
            IsActive = true,
        };
        _fixture.Context.Users.Add(cashier);
        await _fixture.Context.SaveChangesAsync();
        return cashier;
    }

    [Fact]
    public async Task AuthorizeAsync_Succeeds_ForUserWithPermission()
    {
        await SeedAdminAsync();

        var result = await _sut.AuthorizeAsync("1234", PermissionKeys.BillingApproveLargeDiscount);

        Assert.True(result.Success);
        Assert.NotNull(result.User);
    }

    [Fact]
    public async Task AuthorizeAsync_DoesNotUnlockManagementSession()
    {
        await SeedAdminAsync();

        await _sut.AuthorizeAsync("1234", PermissionKeys.BillingApproveLargeDiscount);

        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public async Task AuthorizeAsync_Fails_ForWrongPin()
    {
        await SeedAdminAsync();

        var result = await _sut.AuthorizeAsync("0000", PermissionKeys.BillingApproveLargeDiscount);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AuthorizeAsync_Fails_WhenUserLacksPermission()
    {
        await SeedAdminAsync();
        await SeedCashierAsync();

        var result = await _sut.AuthorizeAsync("4321", PermissionKeys.BillingApproveLargeDiscount);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AuthorizeAsync_Succeeds_ForOwner_ReprintingInvoice()
    {
        await SeedAdminAsync();

        var result = await _sut.AuthorizeAsync("1234", PermissionKeys.SalesReprintInvoice);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task AuthorizeAsync_Fails_ForCashier_ReprintingInvoice()
    {
        await SeedAdminAsync();
        await SeedCashierAsync();

        var result = await _sut.AuthorizeAsync("4321", PermissionKeys.SalesReprintInvoice);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task LoginWithPinAsync_LocksGlobalPinAuth_AfterFiveWrongGuesses()
    {
        await SeedAdminAsync();

        for (var i = 0; i < 5; i++)
        {
            await _sut.LoginWithPinAsync("0000");
        }

        var result = await _sut.LoginWithPinAsync("1234");

        Assert.False(result.Success);
        Assert.Contains("Too many", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(_session.IsPinLocked);
    }

    [Fact]
    public async Task AuthorizeAsync_IsBlockedByGlobalPinLockout_TriggeredViaLoginWithPinAsync()
    {
        await SeedAdminAsync();

        for (var i = 0; i < 5; i++)
        {
            await _sut.LoginWithPinAsync("0000");
        }

        var result = await _sut.AuthorizeAsync("1234", PermissionKeys.BillingApproveLargeDiscount);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task LoginWithPinAsync_ResetsGlobalPinLockoutCounter_OnSuccess()
    {
        await SeedAdminAsync();

        for (var i = 0; i < 4; i++)
        {
            await _sut.LoginWithPinAsync("0000");
        }

        var success = await _sut.LoginWithPinAsync("1234");
        Assert.True(success.Success);

        // One more wrong guess shouldn't trip the lockout since the counter reset on success.
        var afterReset = await _sut.LoginWithPinAsync("0000");
        Assert.False(afterReset.Success);
        Assert.False(_session.IsPinLocked);
    }

    [Fact]
    public async Task FailedLoginAttempts_NeverExposePlainTextSecretInAuditLog()
    {
        await SeedAdminAsync();

        await _sut.LoginWithPasswordAsync("admin", "wrong-password-guess");

        var entries = await _fixture.Context.AuditLogs.ToListAsync();
        Assert.DoesNotContain(entries, e =>
            (e.NewValue?.Contains("wrong-password-guess") ?? false) ||
            (e.PreviousValue?.Contains("wrong-password-guess") ?? false) ||
            (e.Reason?.Contains("wrong-password-guess") ?? false));
    }

    public void Dispose() => _fixture.Dispose();
}
