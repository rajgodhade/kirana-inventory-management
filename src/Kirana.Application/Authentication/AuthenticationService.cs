using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Authentication;

public sealed class AuthenticationService(
    IKiranaDbContext db,
    IPasswordHasher passwordHasher,
    IAuditLogger auditLogger,
    ManagementSession session) : IAuthenticationService
{
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MaxFailedAttempts = 5;

    public async Task<AuthResult> LoginWithPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(u => u.Username == username, cancellationToken);
        return await AuthenticateAsync(user, password, isPin: false, cancellationToken);
    }

    public async Task<AuthResult> LoginWithUsernameAndPinAsync(string username, string pin, CancellationToken cancellationToken = default)
    {
        if (TryGetPinLockoutMessage(out var lockedMessage))
        {
            return AuthResult.Fail(lockedMessage);
        }

        var user = await FindUserAsync(u => u.Username == username, cancellationToken);
        return await AuthenticateAsync(user, pin, isPin: true, cancellationToken);
    }

    public async Task<AuthResult> LoginWithPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (TryGetPinLockoutMessage(out var lockedMessage))
        {
            return AuthResult.Fail(lockedMessage);
        }

        var user = await FindUserByPinAsync(pin, cancellationToken);
        if (user is null)
        {
            session.RecordFailedPinAttempt();
            await auditLogger.RecordAsync(null, "FailedLogin", nameof(User), reason: "PIN not recognized", cancellationToken: cancellationToken);
            return AuthResult.Fail("Incorrect PIN.");
        }

        return await AuthenticateAsync(user, pin, isPin: true, cancellationToken);
    }

    public async Task<AuthResult> AuthorizeAsync(string pin, string requiredPermission, CancellationToken cancellationToken = default)
    {
        if (TryGetPinLockoutMessage(out var lockedMessage))
        {
            return AuthResult.Fail(lockedMessage);
        }

        var user = await FindUserByPinAsync(pin, cancellationToken);
        if (user is null)
        {
            session.RecordFailedPinAttempt();
            await auditLogger.RecordAsync(null, "FailedAuthorization", nameof(User), reason: "PIN not recognized", cancellationToken: cancellationToken);
            return AuthResult.Fail("Incorrect PIN.");
        }

        if (IsAccountLocked(user, out var accountLockedMessage))
        {
            return AuthResult.Fail(accountLockedMessage);
        }

        session.ResetFailedPinAttempts();

        var hasPermission = user.Role.RolePermissions.Any(rp => rp.Permission.Key == requiredPermission);
        if (!hasPermission)
        {
            await auditLogger.RecordAsync(user.Id, "FailedAuthorization", nameof(User), user.Id.ToString(),
                reason: $"Missing permission '{requiredPermission}'", cancellationToken: cancellationToken);
            return AuthResult.Fail("This user is not permitted to authorize that action.");
        }

        await auditLogger.RecordAsync(user.Id, "ActionAuthorized", nameof(User), user.Id.ToString(),
            reason: requiredPermission, cancellationToken: cancellationToken);

        return AuthResult.Ok(user);
    }

    public void LockAndReturnToBilling()
    {
        var userId = session.CurrentUser?.Id;
        session.Lock();
        _ = auditLogger.RecordAsync(userId, "Lock", nameof(User));
    }

    private bool TryGetPinLockoutMessage(out string message)
    {
        if (session.IsPinLocked)
        {
            message = $"Too many incorrect PIN attempts. Try again after {session.PinLockedUntilUtc:t}.";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static bool IsAccountLocked(User user, out string message)
    {
        if (user.LockedUntilUtc is { } lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            message = $"Account locked. Try again after {lockedUntil:t}.";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private async Task<User?> FindUserByPinAsync(string pin, CancellationToken cancellationToken)
    {
        var candidates = await db.Users
            .Include(u => u.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Where(u => u.IsActive && u.PinHash != null)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(u => passwordHasher.Verify(pin, u.PinHash!));
    }

    private async Task<User?> FindUserAsync(Func<User, bool> predicate, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Include(u => u.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        return users.FirstOrDefault(predicate);
    }

    private async Task<AuthResult> AuthenticateAsync(User? user, string secret, bool isPin, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return AuthResult.Fail("Invalid credentials.");
        }

        if (IsAccountLocked(user, out var lockedMessage))
        {
            return AuthResult.Fail(lockedMessage);
        }

        var storedHash = isPin ? user.PinHash : user.PasswordHash;
        var isValid = storedHash is not null && passwordHasher.Verify(secret, storedHash);

        if (!isValid)
        {
            if (isPin)
            {
                session.RecordFailedPinAttempt();
            }

            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedUntilUtc = DateTime.UtcNow.Add(LockoutDuration);
            }

            await db.SaveChangesAsync(cancellationToken);
            await auditLogger.RecordAsync(user.Id, "FailedLogin", nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            return AuthResult.Fail("Invalid credentials.");
        }

        if (isPin)
        {
            session.ResetFailedPinAttempts();
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntilUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var permissionKeys = user.Role.RolePermissions.Select(rp => rp.Permission.Key);
        session.Unlock(user, permissionKeys);

        await auditLogger.RecordAsync(user.Id, "ManagementLogin", nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
        return AuthResult.Ok(user);
    }
}
