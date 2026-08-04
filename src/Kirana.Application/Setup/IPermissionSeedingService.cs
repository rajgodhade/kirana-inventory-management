namespace Kirana.Application.Setup;

/// <summary>
/// Keeps an already-set-up store's <c>Permissions</c>/<c>RolePermissions</c> tables in sync with
/// the current <see cref="Domain.Entities.PermissionKeys.All"/> catalog (PRD §9). Unlike
/// <see cref="IFirstTimeSetupService"/> (which seeds everything exactly once on a brand-new
/// install), this is safe to call on every app launch: it only ever inserts permissions that are
/// missing, and only grants them to the built-in system roles' default sets — it never touches
/// permissions/role-mappings that already exist, so hand-tuned custom role permissions are never
/// overwritten.
/// </summary>
public interface IPermissionSeedingService
{
    Task SyncPermissionsAsync(CancellationToken cancellationToken = default);
}
