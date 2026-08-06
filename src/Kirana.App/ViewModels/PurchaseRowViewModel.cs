namespace Kirana.App.ViewModels;

/// <summary>Payment status of a purchase, derived entirely from its existing
/// <c>AmountPaid</c>/<c>OutstandingAmount</c> fields — a presentation-only classification, not a
/// new domain concept.</summary>
public enum PurchasePaymentStatus
{
    Paid,
    PartiallyPaid,
    Outstanding,
}

/// <summary>Flattened row for the Purchases list (PRD §28). No <c>required</c> members.</summary>
public sealed class PurchaseRowViewModel
{
    public int Id { get; init; }
    public int SupplierId { get; init; }
    public string PurchaseNumber { get; init; } = "";
    public string SupplierName { get; init; } = "";
    public string DateText { get; init; } = "";
    public DateTime PurchaseDateUtc { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal OutstandingAmount { get; init; }

    /// <summary>Position in the currently displayed list — drives zebra striping only, assigned
    /// after filtering settles the final row order.</summary>
    public int RowIndex { get; set; }

    public bool IsAlternateRow => RowIndex % 2 == 1;

    public bool HasOutstanding => OutstandingAmount > 0;

    /// <summary>Paid iff nothing is owed; otherwise partially paid iff something has already been
    /// paid, else fully outstanding. Calculated purely from the fields above — no new query.</summary>
    public PurchasePaymentStatus PaymentStatus => OutstandingAmount <= 0
        ? PurchasePaymentStatus.Paid
        : AmountPaid > 0
            ? PurchasePaymentStatus.PartiallyPaid
            : PurchasePaymentStatus.Outstanding;

    public bool IsPaid => PaymentStatus == PurchasePaymentStatus.Paid;
    public bool IsPartiallyPaid => PaymentStatus == PurchasePaymentStatus.PartiallyPaid;
    public bool IsFullyOutstanding => PaymentStatus == PurchasePaymentStatus.Outstanding;

    /// <summary>Single letter for the supplier avatar chip.</summary>
    public string SupplierInitial => string.IsNullOrWhiteSpace(SupplierName)
        ? "?"
        : SupplierName.TrimStart()[0].ToString().ToUpperInvariant();
}
