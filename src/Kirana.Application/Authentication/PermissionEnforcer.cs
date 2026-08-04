using Kirana.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Authentication;

public sealed class PermissionEnforcer(IKiranaDbContext db) : IPermissionEnforcer
{
    public async Task<bool> HasPermissionAsync(int? userId, string permissionKey, CancellationToken cancellationToken = default)
    {
        if (userId is null)
        {
            return false;
        }

        var user = await db.Users
            .Include(u => u.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is { IsActive: true } && user.Role.RolePermissions.Any(rp => rp.Permission.Key == permissionKey);
    }

    public async Task EnsureHasPermissionAsync(int? userId, string permissionKey, CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionAsync(userId, permissionKey, cancellationToken))
        {
            throw new UnauthorizedAccessException($"This action requires the '{permissionKey}' permission.");
        }
    }
}
