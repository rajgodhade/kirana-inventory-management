using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// An authorized manual correction to stock (Phase 13D) — for the cases that are NOT a sale,
/// purchase, return, or physical stock count: damage, expiry, loss, theft, found stock, or fixing a
/// wrong number.
///
/// <para><b>Immutable once written.</b> There is no edit, no delete, and no re-finalize. A mistaken
/// adjustment is corrected by making a compensating one (typically with
/// <see cref="InventoryAdjustmentReason.DataCorrection"/>), so the ledger keeps both the error and
/// the fix rather than quietly losing the error. This is the whole point of the record existing
/// separately from the <see cref="StockMovement"/> it produces.</para>
///
/// <para>Distinct from <see cref="StockCount"/> on purpose: a stock-take is evidence gathered by
/// counting the shelf, while this is somebody asserting a number. They carry different weight in a
/// shrinkage investigation, so they get different movement types and different records.</para>
/// </summary>
public class InventoryAdjustment : Entity
{
    /// <summary>Human-readable identifier, e.g. "ADJ-000001". Issued from the shared sequence
    /// infrastructure and stamped onto the resulting stock movement, so a ledger row can always be
    /// traced back to the reason and notes recorded here.</summary>
    public string AdjustmentNumber { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Product identity as it was at the moment of adjustment. Snapshotted for the same
    /// reason <see cref="SaleItem"/> and <see cref="StockCountItem"/> do it: renaming a product
    /// later must not rewrite the history of what was corrected.</summary>
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }

    /// <summary>The product's stocking unit at the time. Adjustments are always expressed in this
    /// unit — pack conversion (Phase 13A) is a purchase-side concern and is not applied here.</summary>
    public UnitOfMeasure UnitSnapshot { get; set; } = UnitOfMeasure.Piece;

    public InventoryAdjustmentDirection Direction { get; set; }

    /// <summary>Always positive — the magnitude. <see cref="Direction"/> carries the sign, and
    /// <see cref="SignedQuantity"/> combines them.</summary>
    public decimal AdjustmentQuantity { get; set; }

    /// <summary>Stock immediately before this adjustment, read fresh inside the transaction that
    /// wrote it — never a value carried down from the UI.</summary>
    public decimal PreviousQuantity { get; set; }

    /// <summary>Stock immediately after. Stored rather than recomputed so the record stays true
    /// even as later movements change the product's current stock.</summary>
    public decimal NewQuantity { get; set; }

    public InventoryAdjustmentReason Reason { get; set; }

    /// <summary>Free-text detail. Required when <see cref="Reason"/> is
    /// <see cref="InventoryAdjustmentReason.Other"/>, optional otherwise.</summary>
    public string? Notes { get; set; }

    public DateTime AdjustedAtUtc { get; set; } = DateTime.UtcNow;

    public int? AdjustedByUserId { get; set; }
    public User? AdjustedByUser { get; set; }

    /// <summary>The signed change actually applied to stock.</summary>
    public decimal SignedQuantity => Direction.ToSignedQuantity(AdjustmentQuantity);
}
