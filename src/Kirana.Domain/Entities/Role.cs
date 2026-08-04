using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Owner/Admin, Manager, Cashier are seeded system roles (PRD §9); Manager's permission
/// set is configurable, so permissions are always resolved through <see cref="RolePermission"/>
/// rather than hard-coded per role.
/// </summary>
public class Role : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<User> Users { get; set; } = new List<User>();
}
