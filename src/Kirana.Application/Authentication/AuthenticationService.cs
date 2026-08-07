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

    /// <summary>
    /// Which stored secret(s) an attempt is allowed to match.
    /// </summary>
    private enum SecretKind
    {
        Pin,
        Password,

        /// <summary>
        /// The caller can't tell which one the user typed, so try the PIN and then the password
        /// as a single logical attempt.
        /// </summary>
        PinOrPassword,
    }

    public async Task<AuthResult> LoginAsync(string username, string secret, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(u => u.Username == username, cancellationToken);
        return await AuthenticateAsync(user, secret, SecretKind.PinOrPassword, cancellationToken);
    }

    public async Task<AuthResult> LoginWithPasswordAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(u => u.Username == username, cancellationToken);
        return await AuthenticateAsync(user, password, SecretKind.Password, cancellationToken);
    }

    public async Task<AuthResult> LoginWithUsernameAndPinAsync(string username, string pin, CancellationToken cancellationToken = default)
    {
        if (TryGetPinLockoutMessage(out var lockedMessage))
        {
            return AuthResult.Fail(lockedMessage);
        }

        var user = await FindUserAsync(u => u.Username == username, cancellationToken);
        return await AuthenticateAsync(user, pin, SecretKind.Pin, cancellationToken);
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

        return await AuthenticateAsync(user, pin, SecretKind.Pin, cancellationToken);
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

    /// <summary>
    /// The single place where a management login is decided. Every exit path here counts as
    /// exactly one attempt: at most one <see cref="User.FailedLoginAttempts"/> increment and at
    /// most one audit entry, however many stored secrets were compared.
    /// </summary>
    private async Task<AuthResult> AuthenticateAsync(User? user, string secret, SecretKind kind, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return AuthResult.Fail("Invalid credentials.");
        }

        if (IsAccountLocked(user, out var lockedMessage))
        {
            return AuthResult.Fail(lockedMessage);
        }

        // The PIN is tried first so that a numeric secret resolves the cheap way, but a password
        // that happens to look like a PIN still gets its chance on the same attempt.
        var matchedPin = kind is SecretKind.Pin or SecretKind.PinOrPassword
            && user.PinHash is not null
            && passwordHasher.Verify(secret, user.PinHash);

        var isValid = matchedPin
            || (kind is SecretKind.Password or SecretKind.PinOrPassword
                && user.PasswordHash is not null
                && passwordHasher.Verify(secret, user.PasswordHash));

        if (!isValid)
        {
            // Only the dedicated PIN entry points feed the shared PIN throttle. A PinOrPassword
            // attempt is ambiguous — counting it would let a numeric password trip the global
            // PIN lockout — so it is governed by this user's own lockout counter below.
            if (kind is SecretKind.Pin)
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

        if (matchedPin)
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
