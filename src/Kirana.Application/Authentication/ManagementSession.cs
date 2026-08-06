using Kirana.Domain.Entities;

namespace Kirana.Application.Authentication;

/// <summary>
/// Tracks the current in-memory management session (PRD §8): once unlocked, the user can
/// move between authorized management screens without re-entering credentials, until
/// <see cref="Lock"/> is called explicitly ("Lock &amp; Return to Billing") or the configured
/// inactivity timeout elapses. Registered as a singleton — there is exactly one desktop
/// session per running instance of the app.
/// </summary>
public sealed class ManagementSession
{
    private const int MaxFailedPinAttempts = 5;
    private static readonly TimeSpan PinLockoutDuration = TimeSpan.FromMinutes(15);

    private readonly HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public bool IsUnlocked { get; private set; }
    public User? CurrentUser { get; private set; }
    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Minutes of Dashboard inactivity before an automatic lock (PRD §8) — loaded from
    /// <c>AppSettings.AutoLockMinutes</c> at startup and kept in sync whenever the Settings screen
    /// changes it, so the auto-lock monitor never needs its own DB round-trip on every tick.</summary>
    public int AutoLockMinutes { get; set; } = 10;

    /// <summary>Mirrors <c>AppSettings.RequirePinForPriceOverride</c>/<c>RequirePinForLargeDiscount</c>/
    /// <c>RequirePinForReprint</c> — loaded at startup and kept in sync by the Settings screen, same
    /// pattern as <see cref="AutoLockMinutes"/>. Read from POS/Management Home, which run without an
    /// unlocked session, so these can't be permission-gated the normal way.</summary>
    public bool RequirePinForPriceOverride { get; set; } = true;

    public bool RequirePinForLargeDiscount { get; set; } = true;

    public bool RequirePinForReprint { get; set; } = true;

    private int _failedPinAttempts;
    private DateTime? _pinLockedUntilUtc;

    /// <summary>True while global PIN-based auth (login-by-PIN and step-up authorization, which
    /// have no separate per-account username to rate-limit against) is locked out after repeated
    /// wrong guesses — a machine-wide throttle independent of any specific <see cref="User"/>'s
    /// own <see cref="User.LockedUntilUtc"/>.</summary>
    public bool IsPinLocked => _pinLockedUntilUtc is { } until && until > DateTime.UtcNow;

    public DateTime? PinLockedUntilUtc => IsPinLocked ? _pinLockedUntilUtc : null;

    public void RecordFailedPinAttempt()
    {
        if (IsPinLocked)
        {
            return;
        }

        _failedPinAttempts++;
        if (_failedPinAttempts >= MaxFailedPinAttempts)
        {
            _pinLockedUntilUtc = DateTime.UtcNow.Add(PinLockoutDuration);
        }
    }

    public void ResetFailedPinAttempts()
    {
        _failedPinAttempts = 0;
        _pinLockedUntilUtc = null;
    }

    public void Unlock(User user, IEnumerable<string> permissionKeys)
    {
        CurrentUser = user;
        _permissions.Clear();
        foreach (var key in permissionKeys)
        {
            _permissions.Add(key);
        }

        IsUnlocked = true;
        Touch();
    }

    public void Lock()
    {
        IsUnlocked = false;
        CurrentUser = null;
        _permissions.Clear();
    }

    public void Touch() => LastActivityUtc = DateTime.UtcNow;

    public bool HasPermission(string permissionKey) => IsUnlocked && _permissions.Contains(permissionKey);

    public bool IsIdleTimeoutExceeded(TimeSpan timeout) =>
        IsUnlocked && DateTime.UtcNow - LastActivityUtc > timeout;
}
