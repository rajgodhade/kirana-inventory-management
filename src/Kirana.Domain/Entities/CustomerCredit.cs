using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One Udhaar entry created when a sale is paid (in full or part) via
/// <see cref="PaymentMethod.CustomerCredit"/> (PRD §31). Repayment tracking
/// (<c>CreditPayment</c>, partial/full settlement UI) is Phase 8 — this phase only creates the
/// entry and rolls the amount into <see cref="Customer.CreditBalance"/>.
/// </summary>
public class CustomerCredit : Entity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
