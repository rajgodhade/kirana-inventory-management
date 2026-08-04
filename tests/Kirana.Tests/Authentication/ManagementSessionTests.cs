using Kirana.Application.Authentication;
using Kirana.Domain.Entities;

namespace Kirana.Tests.Authentication;

public class ManagementSessionTests
{
    private static User MakeUser() => new() { Username = "test", FullName = "Test User", Role = new Role { Name = "Owner" } };

    [Fact]
    public void Unlock_SetsIsUnlockedAndCurrentUserAndPermissions()
    {
        var session = new ManagementSession();
        var user = MakeUser();

        session.Unlock(user, [PermissionKeys.ProductsEdit]);

        Assert.True(session.IsUnlocked);
        Assert.Same(user, session.CurrentUser);
        Assert.True(session.HasPermission(PermissionKeys.ProductsEdit));
        Assert.False(session.HasPermission(PermissionKeys.UsersManage));
    }

    [Fact]
    public void Lock_ClearsUnlockedStateAndPermissions()
    {
        var session = new ManagementSession();
        session.Unlock(MakeUser(), [PermissionKeys.ProductsEdit]);

        session.Lock();

        Assert.False(session.IsUnlocked);
        Assert.Null(session.CurrentUser);
        Assert.False(session.HasPermission(PermissionKeys.ProductsEdit));
    }

    [Fact]
    public void HasPermission_ReturnsFalse_WhenLocked_EvenIfKeyWasPreviouslyGranted()
    {
        var session = new ManagementSession();
        session.Unlock(MakeUser(), [PermissionKeys.ProductsEdit]);
        session.Lock();

        Assert.False(session.HasPermission(PermissionKeys.ProductsEdit));
    }

    [Fact]
    public void IsIdleTimeoutExceeded_ReturnsFalse_WhenLocked()
    {
        var session = new ManagementSession();

        Assert.False(session.IsIdleTimeoutExceeded(TimeSpan.Zero));
    }

    [Fact]
    public void IsIdleTimeoutExceeded_ReturnsFalse_ImmediatelyAfterUnlockOrTouch()
    {
        var session = new ManagementSession();
        session.Unlock(MakeUser(), []);

        Assert.False(session.IsIdleTimeoutExceeded(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task IsIdleTimeoutExceeded_ReturnsTrue_AfterTimeoutElapsesWithoutTouch()
    {
        var session = new ManagementSession();
        session.Unlock(MakeUser(), []);

        await Task.Delay(60);

        Assert.True(session.IsIdleTimeoutExceeded(TimeSpan.FromMilliseconds(30)));
    }

    [Fact]
    public async Task Touch_ResetsIdleClock()
    {
        var session = new ManagementSession();
        session.Unlock(MakeUser(), []);

        await Task.Delay(60);
        session.Touch();

        Assert.False(session.IsIdleTimeoutExceeded(TimeSpan.FromMilliseconds(30)));
    }

    [Fact]
    public void AutoLockMinutes_DefaultsToTen()
    {
        var session = new ManagementSession();

        Assert.Equal(10, session.AutoLockMinutes);
    }

    [Fact]
    public void AutoLockMinutes_IsMutable_ForSettingsScreenToUpdate()
    {
        var session = new ManagementSession();

        session.AutoLockMinutes = 30;

        Assert.Equal(30, session.AutoLockMinutes);
    }

    [Fact]
    public void IsPinLocked_IsFalse_Initially()
    {
        var session = new ManagementSession();

        Assert.False(session.IsPinLocked);
    }

    [Fact]
    public void IsPinLocked_BecomesTrue_AfterFiveFailedAttempts()
    {
        var session = new ManagementSession();

        for (var i = 0; i < 5; i++)
        {
            session.RecordFailedPinAttempt();
        }

        Assert.True(session.IsPinLocked);
        Assert.NotNull(session.PinLockedUntilUtc);
    }

    [Fact]
    public void IsPinLocked_IsFalse_AfterFourFailedAttempts()
    {
        var session = new ManagementSession();

        for (var i = 0; i < 4; i++)
        {
            session.RecordFailedPinAttempt();
        }

        Assert.False(session.IsPinLocked);
    }

    [Fact]
    public void ResetFailedPinAttempts_ClearsLockout()
    {
        var session = new ManagementSession();
        for (var i = 0; i < 5; i++)
        {
            session.RecordFailedPinAttempt();
        }

        session.ResetFailedPinAttempts();

        Assert.False(session.IsPinLocked);
        Assert.Null(session.PinLockedUntilUtc);
    }
}
