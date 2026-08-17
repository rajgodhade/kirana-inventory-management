using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One line of a completed sale. Every field that could later change on the <see cref="Product"/>
/// (name, code, SKU, HSN, unit, price, GST rate) is snapshotted here at sale time — PRD §14/§22
/// require that editing a product afterwards must never alter historical invoices, so this row
/// is the only thing ever read back for a past invoice, not a live join to <see cref="Product"/>.
/// </summary>
public class SaleItem : Entity
{
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // --- Historical snapshot (PRD §14, §22) ---
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public string? HsnCodeSnapshot { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public bool IsTaxInclusiveSnapshot { get; set; }
    public decimal GstRatePercentSnapshot { get; set; }

    // --- Quantities and computed amounts ---
    public decimal Quantity { get; set; }
    public decimal UnitPriceSnapshot { get; set; }

    /// <summary>The product's MRP at sale time — snapshotted for the same reason as everything
    /// else here: a later change to the product's MRP must never alter what a past invoice's
    /// "You Saved" figure was computed from.</summary>
    public decimal MrpSnapshot { get; set; }

    /// <summary>
    /// What this unit COST the shop at the moment it was sold (Phase 17A) — the counterpart to
    /// <see cref="UnitPriceSnapshot"/>, which records what it was sold FOR.
    ///
    /// <para>Profit reporting previously multiplied quantity by the product's <em>current</em>
    /// purchase price, so raising a product's cost today retroactively changed last month's
    /// reported profit. Snapshotting the cost here makes historical profit a fact rather than a
    /// figure that moves whenever master data does.</para>
    ///
    /// <para><b>Nullable, and null is meaningful.</b> It means "the cost of this line is not
    /// known" — the state of every sale recorded before this column existed. It emphatically does
    /// NOT mean zero: treating it as zero would report those lines at 100% margin, which is a
    /// worse answer than admitting the cost is unknown. Reports must exclude such lines from cost
    /// and disclose how many they excluded, never silently fold them in.
    ///
    /// Deliberately not backfilled: the shop's purchase price today is not evidence of what it
    /// paid a year ago, and inventing that number would look exactly like a real cost basis.</para>
    ///
    /// <para>Costing basis is the product's purchase price at sale time. The POS never consults
    /// <c>ProductBatch.PurchasePrice</c>, so <c>Product.PurchasePrice</c> is the authoritative
    /// cost the till actually knows. Weighted-average, FIFO and batch costing are a later
    /// decision (Phase 17C), not something to guess at here.</para>
    /// </summary>
    public decimal? UnitCostSnapshot { get; set; }

    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PromotionDiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }
    public ICollection<SaleItemPromotion> Promotions { get; set; } = new List<SaleItemPromotion>();
}
