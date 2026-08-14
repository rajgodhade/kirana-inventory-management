using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Supplier master record (PRD §28-29). <see cref="SupplierCode"/> is the stable, auto-generated
/// identifier (e.g. "SUP-000001") and must never change after creation.
/// <see cref="OutstandingBalance"/> is a denormalized running total kept in sync by
/// <see cref="Purchase"/>/<see cref="SupplierPayment"/> writes — the supplier ledger (built from
/// those same rows) is the authoritative reconstruction if it ever needs re-deriving.
/// </summary>
public class Supplier : Entity
{
    public string SupplierCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Gstin { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;

    public decimal OutstandingBalance { get; set; }

    public ICollection<Purchase> Purchases { get; set; } = new List<Purchase>();
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
    public ICollection<Product> PreferredForProducts { get; set; } = new List<Product>();
    public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();
}
