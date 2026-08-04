namespace Kirana.Application.Authentication;

/// <summary>
/// Server-side permission gate (PRD §9-10): looks a user up by id and checks their role's
/// permissions directly against the database, independent of <see cref="ManagementSession"/>.
/// Used by services reached from contexts where the ambient session is deliberately locked (POS
/// Billing Mode step-up actions like discount authorization or invoice reprint) as well as by
/// Management-mode services, so "unauthorized actions are blocked at the Application/service
/// layer" holds even if a caller bypasses the UI's own gating.
/// </summary>
public interface IPermissionEnforcer
{
    Task<bool> HasPermissionAsync(int? userId, string permissionKey, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> if the user doesn't hold the
    /// permission (including when <paramref name="userId"/> is null or the user is inactive).</summary>
    Task EnsureHasPermissionAsync(int? userId, string permissionKey, CancellationToken cancellationToken = default);
}
