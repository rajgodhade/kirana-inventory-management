using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One line of a finalized purchase. Every field that could later change on the
/// <see cref="Product"/> (name, code, SKU, HSN, unit, GST rate) is snapshotted here at purchase
/// time — mirrors <see cref="SaleItem"/>'s exact rationale: editing a product afterwards must
/// never alter a historical purchase record. <see cref="PurchasePriceSnapshot"/> is the
/// negotiated price for this specific purchase, never read back from <see cref="Product"/>.
/// </summary>
public class PurchaseItem : Entity
{
    public int PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // --- Historical snapshot ---
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public string? HsnCodeSnapshot { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public bool IsTaxInclusiveSnapshot { get; set; }
    public decimal GstRatePercentSnapshot { get; set; }

    // --- Quantities and computed amounts ---
    public decimal Quantity { get; set; }
    public decimal PurchasePriceSnapshot { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }

    // --- Optional batch/expiry data captured at purchase time (PRD §27) ---
    public string? BatchNumber { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}
