using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One returned line. Snapshots come from the original <see cref="SaleItem"/>, not from the live
/// <see cref="Product"/> — a return must be valued at what the customer actually paid, even if the
/// product's name or price has changed since (PRD §14, §22).
/// </summary>
public class SalesReturnItem : Entity
{
    public int SalesReturnId { get; set; }
    public SalesReturn SalesReturn { get; set; } = null!;

    /// <summary>The exact sale line being returned against — this is what caps the quantity.</summary>
    public int SaleItemId { get; set; }
    public SaleItem SaleItem { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // --- Historical snapshot, copied from the SaleItem ---
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public decimal UnitPriceSnapshot { get; set; }
    public decimal GstRatePercentSnapshot { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Value of this returned line, prorated from the original line so any discount and
    /// GST the customer actually paid are reflected in what comes back.</summary>
    public decimal LineRefundAmount { get; set; }

    public ReturnDisposition Disposition { get; set; } = ReturnDisposition.ReturnToStock;

    /// <summary>Batch the goods went back into, when the product tracks batches and the disposition
    /// is <see cref="ReturnDisposition.ReturnToStock"/>.</summary>
    public string? BatchNumber { get; set; }

    public string? Reason { get; set; }
}
