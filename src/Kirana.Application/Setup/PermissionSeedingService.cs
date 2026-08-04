using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Setup;

public sealed class PermissionSeedingService(IKiranaDbContext db) : IPermissionSeedingService
{
    public async Task SyncPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken);
        if (store is not { SetupCompleted: true })
        {
            // A brand-new install hasn't run the setup wizard yet — FirstTimeSetupService will
            // seed everything fresh once it does, so there's nothing to sync yet.
            return;
        }

        var existingKeys = await db.Permissions.Select(p => p.Key).ToListAsync(cancellationToken);
        var existingKeySet = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = PermissionKeys.All.Where(p => !existingKeySet.Contains(p.Key)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var newPermissions = missing
            .Select(p => new Permission { Key = p.Key, Description = p.Description })
            .ToList();
        db.Permissions.AddRange(newPermissions);
        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Roles.ToListAsync(cancellationToken);
        foreach (var role in roles)
        {
            var defaultKeysForRole = DefaultKeysForSystemRole(role.Name);
            foreach (var permission in newPermissions)
            {
                if (defaultKeysForRole.Contains(permission.Key, StringComparer.OrdinalIgnoreCase))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Which of the newly-introduced permissions a built-in system role should receive
    /// by default. Custom (non-system) roles get nothing automatically — an administrator must
    /// opt them in explicitly via the User Management screen's role editor.</summary>
    private static IReadOnlyList<string> DefaultKeysForSystemRole(string roleName) => roleName switch
    {
        "Owner" => PermissionKeys.Owner,
        "Manager" => PermissionKeys.Manager,
        "Cashier" => PermissionKeys.Cashier,
        _ => [],
    };
}
