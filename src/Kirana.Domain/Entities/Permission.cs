using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A single grantable capability, e.g. "products.view", "pricing.viewPurchasePrice",
/// "invoices.cancel" (PRD §9). Seeded once at first setup; not user-editable.
/// </summary>
public class Permission : Entity
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
