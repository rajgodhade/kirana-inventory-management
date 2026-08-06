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
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }
}
