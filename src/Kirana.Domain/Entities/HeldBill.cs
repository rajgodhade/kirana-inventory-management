using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// An in-progress cart set aside via "Hold" so the cashier can serve another customer, then
/// "Resume" it later (PRD §6, §19). Not a <see cref="Sale"/> — nothing here is final until the
/// bill is completed, so line items carry only what's needed to repopulate the cart, not a
/// historical snapshot.
/// </summary>
public class HeldBill : Entity
{
    public DateTime HeldAtUtc { get; set; } = DateTime.UtcNow;
    public int? CashierUserId { get; set; }
    public User? CashierUser { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal BillDiscountPercent { get; set; }
    public string? Note { get; set; }

    public ICollection<HeldBillItem> Items { get; set; } = new List<HeldBillItem>();
}
