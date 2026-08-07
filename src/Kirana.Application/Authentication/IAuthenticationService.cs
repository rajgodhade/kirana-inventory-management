namespace Kirana.Application.Authentication;

/// <summary>
/// Handles the "Management Access" unlock dialog (PRD §7): username+password, or PIN.
/// On success this also unlocks the shared <see cref="ManagementSession"/> with the
/// user's resolved permission set.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Unlocks management for the named user with whichever secret they typed. The user is not
    /// expected to know whether their credential is a PIN or a password: the PIN is checked
    /// first, then the password, and the two comparisons count as a single login attempt —
    /// one increment toward the account lockout and one audit entry at most.
    /// </summary>
    Task<AuthResult> LoginAsync(string username, string secret, CancellationToken cancellationToken = default);

    Task<AuthResult> LoginWithPasswordAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlocks management for the named user using that user's PIN.
    /// </summary>
    Task<AuthResult> LoginWithUsernameAndPinAsync(string username, string pin, CancellationToken cancellationToken = default);

    Task<AuthResult> LoginWithPinAsync(string pin, CancellationToken cancellationToken = default);

    /// <summary>
    /// One-off authorization for a sensitive POS action (PRD §10) — e.g. a discount above the
    /// cashier's normal limit. Verifies the PIN belongs to an active user who holds
    /// <paramref name="requiredPermission"/> and audit-logs the authorization, but — unlike
    /// <see cref="LoginWithPinAsync"/> — does <em>not</em> unlock the shared
    /// <see cref="ManagementSession"/>; the cashier stays in Billing Mode throughout.
    /// </summary>
    Task<AuthResult> AuthorizeAsync(string pin, string requiredPermission, CancellationToken cancellationToken = default);

    void LockAndReturnToBilling();
}
