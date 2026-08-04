using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One payment made to a supplier (PRD §28-29) — reduces <see cref="Supplier.OutstandingBalance"/>
/// and, when <see cref="PurchaseId"/> is set, that purchase's own <see cref="Purchase.OutstandingAmount"/>.
/// <see cref="PurchaseId"/> is optional: a payment can be earmarked against one specific purchase
/// or simply recorded against the supplier's overall outstanding balance. Every payment — including
/// the one made at purchase-entry time — is one of these rows, so the supplier ledger never needs
/// to special-case "the first payment".
/// </summary>
public class SupplierPayment : Entity
{
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int? PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaymentDateUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public int? RecordedByUserId { get; set; }
    public User? RecordedByUser { get; set; }
}
