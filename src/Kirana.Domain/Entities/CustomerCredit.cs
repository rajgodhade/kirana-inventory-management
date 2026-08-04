using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One Udhaar entry created when a sale is paid (in full or part) via
/// <see cref="PaymentMethod.CustomerCredit"/> (PRD §31). <see cref="Amount"/> is immutable — it is
/// what the originating invoice put on credit — while <see cref="RemainingAmount"/> is drawn down
/// by <see cref="CreditPaymentAllocation"/> rows as the customer repays. The invoice itself is
/// never altered by repayment.
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
