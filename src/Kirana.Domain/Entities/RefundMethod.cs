namespace Kirana.Domain.Entities;

/// <summary>
/// How money goes back to the customer on a sales return (PRD §33).
///
/// Deliberately separate from <see cref="PaymentMethod"/> rather than reusing it: a refund can be
/// <see cref="None"/> (an even exchange or a pure stock correction, where no money moves at all),
/// which is not a payment method, and <see cref="StoreCredit"/> settles against the customer's
/// Udhaar balance rather than moving cash.
/// </summary>
public enum RefundMethod
{
    Cash,
    Upi,
    Card,
    /// <summary>Credited to the customer's account — reduces what they owe, or leaves them in
    /// credit. Requires a customer on the original sale.</summary>
    StoreCredit,
    /// <summary>No money returned. Used for exchanges and adjustments, where the stock and the
    /// invoice record still need correcting.</summary>
    None,
}
