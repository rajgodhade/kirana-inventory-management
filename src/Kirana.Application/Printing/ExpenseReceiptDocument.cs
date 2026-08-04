namespace Kirana.Application.Printing;

/// <summary>
/// A printable expense voucher (PRD §32) — the slip a shopkeeper files against a payment made.
/// No <c>required</c> members, for the same WinUI XAML-compiler reason as the other documents.
/// </summary>
public sealed class ExpenseReceiptDocument
{
    public int ExpenseId { get; init; }

    // Store header
    public string StoreName { get; init; } = string.Empty;
    public string? StoreAddress { get; init; }
    public string? StoreContactNumber { get; init; }
    public string? FooterText { get; init; }

    // Voucher
    public string ExpenseNumber { get; init; } = string.Empty;
    public DateTime ExpenseDateUtc { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string? ReferenceNumber { get; init; }
    public string? Description { get; init; }
    public string? Notes { get; init; }
    public string? RecordedByName { get; init; }
}
