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

    // --- LoginAsync: the dialog hands over a username and whatever secret was typed, and the
    // --- service works out whether it is a PIN or a password (PIN first, password second).

    [Fact]
    public async Task LoginAsync_Succeeds_WithCorrectPin()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginAsync("admin", "1234");

        Assert.True(result.Success);
        Assert.True(_session.IsUnlocked);
        Assert.True(_session.HasPermission(PermissionKeys.UsersManage));
    }

    [Fact]
    public async Task LoginAsync_Succeeds_WithCorrectPassword()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginAsync("admin", "S3cure!Pass");

        Assert.True(result.Success);
        Assert.True(_session.IsUnlocked);
    }

    [Fact]
    public async Task LoginAsync_FallsBackToPassword_WhenSecretIsNotThePin()
    {
        await SeedAdminAsync();

        // "S3cure!Pass" is not the PIN, so the PIN comparison fails first and the password wins.
        var result = await _sut.LoginAsync("admin", "S3cure!Pass");

        Assert.True(result.Success);

        var admin = await _fixture.Context.Users.FirstAsync(u => u.Username == "admin");
        Assert.Equal(0, admin.FailedLoginAttempts);
    }

    [Fact]
    public async Task LoginAsync_Succeeds_WithNumericPassword_ViaPasswordFallback()
    {
        await SeedAdminAsync();
        await SeedNumericPasswordUserAsync();

        // Regression: a 4-6 digit password used to be misread as a PIN and rejected outright.
        var result = await _sut.LoginAsync("numeric1", "987654");

        Assert.True(result.Success);
        Assert.True(_session.IsUnlocked);
    }

    [Fact]
    public async Task LoginAsync_Fails_WithAnotherUsersPin()
    {
        await SeedAdminAsync();
        await SeedCashierAsync();

        var result = await _sut.LoginAsync("admin", "4321");

        Assert.False(result.Success);
        Assert.False(_session.IsUnlocked);
    }

    [Fact]
    public async Task LoginAsync_RecordsExactlyOneFailedAttempt_WhenPinAndPasswordBothWrong()
    {
        await SeedAdminAsync();

        var result = await _sut.LoginAsync("admin", "not-the-pin-or-password");

        Assert.False(result.Success);

        var admin = await _fixture.Context.Users.FirstAsync(u => u.Username == "admin");
        Assert.Equal(1, admin.FailedLoginAttempts);
    }

    [Fact]
    public async Task LoginAsync_WritesExactlyOneAuditEntry_WhenPinAndPasswordBothWrong()
    {
        await SeedAdminAsync();

        await _sut.LoginAsync("admin", "not-the-pin-or-password");

        var failures = await _fixture.Context.AuditLogs
            .Where(e => e.Action == "FailedLogin")
            .ToListAsync();

        Assert.Single(failures);
    }

    [Fact]
    public async Task LoginAsync_WritesExactlyOneAuditEntry_OnSuccess()
    {
        await SeedAdminAsync();

        await _sut.LoginAsync("admin", "1234");

        var logins = await _fixture.Context.AuditLogs
            .Where(e => e.Action == "ManagementLogin")
            .ToListAsync();

        Assert.Single(logins);
    }

    [Fact]
    public async Task LoginAsync_TakesFiveWholeAttempts_ToLockTheAccount()
    {
        await SeedAdminAsync();

        // Four failures must not lock: each call is one attempt even though it compares two hashes.
        for (var i = 0; i < 4; i++)
        {
            await _sut.LoginAsync("admin", "wrong-on-both-counts");
        }

        var admin = await _fixture.Context.Users.FirstAsync(u => u.Username == "admin");
        Assert.Equal(4, admin.FailedLoginAttempts);
        Assert.Null(admin.LockedUntilUtc);

        // The PIN still works at this point, proving nothing locked early.
        var recovered = await _sut.LoginAsync("admin", "1234");
        Assert.True(recovered.Success);
    }

    [Fact]
    public async Task LoginAsync_LocksAccount_OnFifthFailedAttempt()
    {
        await SeedAdminAsync();

        for (var i = 0; i < 5; i++)
        {
            await _sut.LoginAsync("admin", "wrong-on-both-counts");
        }

        var result = await _sut.LoginAsync("admin", "1234");

        Assert.False(result.Success);
        Assert.Contains("locked", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_DoesNotTripGlobalPinLockout_ForNumericPasswordHolder()
    {
        await SeedAdminAsync();
        await SeedNumericPasswordUserAsync();

        // Four ambiguous misses must not exhaust the shared PIN throttle used by the
        // username-less quick unlock.
        for (var i = 0; i < 4; i++)
        {
            await _sut.LoginAsync("numeric1", "000000");
        }

        Assert.False(_session.IsPinLocked);
        Assert.True((await _sut.LoginWithPinAsync("1234")).Success);
    }

    private async Task<User> SeedNumericPasswordUserAsync()
    {
        var managerRole = await _fixture.Context.Roles.FirstAsync(r => r.Name == "Manager");
        var user = new User
        {
            Username = "numeric1",
            FullName = "Numeric Password User",
            PasswordHash = _hasher.Hash("987654"),
            PinHash = _hasher.Hash("5555"),
            Role = managerRole,
            IsActive = true,
        };
        _fixture.Context.Users.Add(user);
        await _fixture.Context.SaveChangesAsync();
        return user;
    }

    public void Dispose() => _fixture.Dispose();
}
