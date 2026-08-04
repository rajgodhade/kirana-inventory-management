using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Real-time stock level for a product (PRD §24), one row per product. Never mutate
/// <see cref="QuantityOnHand"/> directly — always go through InventoryService so a matching
/// <see cref="StockMovement"/> row is written in the same transaction (PRD §25, §43).
/// </summary>
public class Inventory : Entity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal QuantityOnHand { get; set; }
}
