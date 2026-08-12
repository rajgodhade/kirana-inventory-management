using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One product line within a <see cref="StockCount"/> (Phase 13C).
///
/// <para>Carries snapshots of the product's identity at count time for the same reason
/// <see cref="SaleItem"/> and <see cref="PurchaseItem"/> do: a completed count is a historical
/// record, and renaming or re-coding a product later must not rewrite what was counted. The
/// snapshots are display/audit data — <see cref="ProductId"/> remains the authoritative link.</para>
/// </summary>
public class StockCountItem : Entity
{
    public int StockCountId { get; set; }
    public StockCount StockCount { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }

    /// <summary>The barcode that was scanned to reach this product, when it was added by scanning.
    /// Null for items added by search. Records WHICH of a product's several codes (Phase 13B) the
    /// counter actually read off the shelf, which is exactly what you want when reconciling a
    /// disputed count later.</summary>
    public string? BarcodeSnapshot { get; set; }

    /// <summary>The product's stocking unit at count time. Physical quantities are always expressed
    /// in this unit — pack conversion (Phase 13A) is a purchase-side concern and is deliberately not
    /// applied here.</summary>
    public UnitOfMeasure UnitSnapshot { get; set; } = UnitOfMeasure.Piece;

    /// <summary>Stock on hand when this item was added to the count. Frozen deliberately: the
    /// variance a counter sees must not shift under them because a sale happened mid-count.</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>What was physically found. Null until the item is actually counted, which is what
    /// separates "on the list" from "counted" in the progress indicator — a counted zero
    /// ("shelf is empty") is a real, meaningful answer and must not read as uncounted.</summary>
    public decimal? CountedQuantity { get; set; }

    /// <summary>Stock on hand at the moment of finalization, when it differed from
    /// <see cref="SystemQuantity"/>. Null when nothing moved during the count (the common case).
    /// Non-null marks an item whose adjustment was rebased onto live stock rather than onto the
    /// snapshot — recorded so the completion summary can explain itself.</summary>
    public decimal? SystemQuantityAtFinalization { get; set; }

    public DateTime? CountedAtUtc { get; set; }

    public string? Notes { get; set; }

    /// <summary>Physical minus system, against the SNAPSHOT — the variance the counter observed.
    /// Null while uncounted. Note the applied adjustment can differ when stock moved mid-count;
    /// that is the rebase, and it is reported separately rather than by rewriting this figure.</summary>
    public decimal? VarianceQuantity => CountedQuantity is null ? null : CountedQuantity - SystemQuantity;

    public bool IsCounted => CountedQuantity is not null;
}
