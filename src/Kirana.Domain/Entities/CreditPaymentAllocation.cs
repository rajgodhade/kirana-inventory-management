using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// How much of one <see cref="CreditPayment"/> was applied to one <see cref="CustomerCredit"/>
/// (PRD §31). This is what makes a repayment fully traceable to its originating invoice: a single
/// ₹500 payment can settle one invoice completely and another partially, and the ledger can show
/// exactly that rather than only a net balance change.
/// </summary>
public class CreditPaymentAllocation : Entity
{
    public int CreditPaymentId { get; set; }
    public CreditPayment CreditPayment { get; set; } = null!;

    public int CustomerCreditId { get; set; }
    public CustomerCredit CustomerCredit { get; set; } = null!;

    public decimal Amount { get; set; }
}
