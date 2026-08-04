using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Goods sent back to a supplier against a finalized <see cref="Purchase"/> (PRD §34).
///
/// Mirrors <see cref="SalesReturn"/>: the original purchase is never modified, and the cap on
/// further returns comes from the sum of quantities already returned per line. Finalizing reduces
/// inventory and the supplier's outstanding balance in one transaction.
/// </summary>
public class PurchaseReturn : Entity
{
    public string ReturnNumber { get; set; } = string.Empty;

    public int PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;

    /// <summary>Snapshot of the purchase number so the return reads correctly without a live join.</summary>
    public string PurchaseNumberSnapshot { get; set; } = string.Empty;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateTime ReturnDateUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Value of the goods returned, which is credited against the supplier's balance.</summary>
    public decimal TotalReturnAmount { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public int? ProcessedByUserId { get; set; }
    public User? ProcessedByUser { get; set; }

    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}
