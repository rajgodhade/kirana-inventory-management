namespace Kirana.Domain.Entities;

/// <summary>Payment methods accepted at POS (PRD §20).</summary>
public enum PaymentMethod
{
    Cash,
    Upi,
    Card,
    CustomerCredit,
}
