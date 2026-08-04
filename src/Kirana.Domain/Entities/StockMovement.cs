using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One immutable row per stock change (PRD §25). <see cref="QuantityChange"/> is signed
/// (positive for increases, negative for decreases); <see cref="PreviousQuantity"/> and
/// <see cref="NewQuantity"/> pin down the ledger so history can be reconstructed exactly.
/// </summary>
public class StockMovement : Entity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public StockMovementType MovementType { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal PreviousQuantity { get; set; }
    public decimal NewQuantity { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Entity name of the transaction that caused this movement, e.g. "Purchase" (Phase 7+).</summary>
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public string? Reason { get; set; }
}
