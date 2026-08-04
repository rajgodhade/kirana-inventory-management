using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One line returned to a supplier. Valued from the original <see cref="PurchaseItem"/>'s
/// negotiated price, never from the product's current purchase price — the credit owed is what
/// was actually paid for these goods.
/// </summary>
public class PurchaseReturnItem : Entity
{
    public int PurchaseReturnId { get; set; }
    public PurchaseReturn PurchaseReturn { get; set; } = null!;

    /// <summary>The exact purchase line being returned against — this is what caps the quantity.</summary>
    public int PurchaseItemId { get; set; }
    public PurchaseItem PurchaseItem { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // --- Historical snapshot, copied from the PurchaseItem ---
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public decimal PurchasePriceSnapshot { get; set; }
    public decimal GstRatePercentSnapshot { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Value of this returned line, prorated from the original line so the supplier is
    /// credited exactly what was charged, discount and GST included.</summary>
    public decimal LineReturnAmount { get; set; }

    /// <summary>Batch the goods were taken out of, where the product tracks batches.</summary>
    public string? BatchNumber { get; set; }

    public string? Reason { get; set; }
}
