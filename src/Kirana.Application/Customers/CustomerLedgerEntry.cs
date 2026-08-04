namespace Kirana.Application.Customers;

/// <summary>
/// One row of a customer's Udhaar ledger (PRD §31) — either credit taken on a sale (debit,
/// increases what they owe) or a repayment (credit, reduces it) — merged chronologically with a
/// running balance. Rebuilt from CustomerCredit/CreditPayment rows on every request, so it is
/// always an accurate reconstruction rather than a separately maintained cache.
/// </summary>
public sealed class CustomerLedgerEntry
{
    public required DateTime DateUtc { get; init; }
    public required string EntryType { get; init; } // "Credit Sale" or "Repayment"
    public required string Reference { get; init; } // invoice number, or receipt number
    public decimal DebitAmount { get; init; }       // credit taken on a sale
    public decimal CreditAmount { get; init; }      // repayment received
    public decimal RunningBalance { get; init; }
    public string? Notes { get; init; }
}
