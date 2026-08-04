namespace Kirana.Application.Purchasing;

/// <summary>One row of a supplier's ledger — either a purchase (debit, increases what's owed) or
/// a payment (credit, reduces it) — merged and sorted chronologically with a running balance
/// (PRD §29). Built fresh from <c>Purchase</c>/<c>SupplierPayment</c> rows on every request, so
/// it's always an accurate reconstruction rather than a separately-maintained cache.</summary>
public sealed class SupplierLedgerEntry
{
    public required DateTime DateUtc { get; init; }
    public required string EntryType { get; init; } // "Purchase" or "Payment"
    public required string Reference { get; init; } // PurchaseNumber, or payment reference/method
    public decimal DebitAmount { get; init; } // purchase grand total
    public decimal CreditAmount { get; init; } // payment amount
    public decimal RunningBalance { get; init; }
    public string? Notes { get; init; }
}
