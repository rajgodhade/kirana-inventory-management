using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One Udhaar repayment received from a customer (PRD §31). Mirrors <see cref="SupplierPayment"/>
/// on the purchasing side. A repayment is settled against one or more outstanding
/// <see cref="CustomerCredit"/> entries via <see cref="Allocations"/>, so every rupee received can
/// be traced back to the specific invoice it settled — a repayment is never just an opaque
/// decrement of the customer's balance.
/// </summary>
public class CreditPayment : Entity
{
    public string ReceiptNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaymentDateUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public int? RecordedByUserId { get; set; }
    public User? RecordedByUser { get; set; }

    public ICollection<CreditPaymentAllocation> Allocations { get; set; } = new List<CreditPaymentAllocation>();
}
