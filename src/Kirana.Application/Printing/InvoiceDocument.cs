namespace Kirana.Application.Printing;

/// <summary>
/// Everything needed to render a printed invoice/receipt (PRD §23), built once from an immutable
/// completed <c>Sale</c> and the current <c>Store</c> settings. Every sale-specific field here
/// comes from <c>Sale</c>/<c>SaleItem</c>/<c>Payment</c> historical snapshots, never a live
/// <c>Product</c> lookup — printing (including reprinting, possibly years later) must always
/// reproduce exactly what the customer was charged at sale time.
///
/// Deliberately has no <c>required</c> members: this type is exposed on a ViewModel bound in
/// WinUI XAML, and the WinUI 3 XAML compiler generates a parameterless-activator entry for every
/// type reachable from a bound ViewModel's public surface — that generated code fails to compile
/// against a type with unset required members, even when nothing actually constructs it that way.
/// <see cref="InvoiceDocumentBuilder"/> is the sole place that constructs this type and always
/// sets every field.
/// </summary>
public sealed class InvoiceDocument
{
    /// <summary>Internal Sale.Id — not printed, but needed by the caller to log a print/reprint
    /// audit entry against the right sale.</summary>
    public int SaleId { get; init; }

    // Store header
    public string StoreName { get; init; } = string.Empty;
    public string? StoreAddress { get; init; }
    public string? StoreContactNumber { get; init; }
    public string? StoreGstin { get; init; }
    public string? StoreLogoPath { get; init; }
    public string? FooterText { get; init; }

    // Invoice header
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime SaleDateUtc { get; init; }
    public string? CashierName { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerGstin { get; init; }

    public IReadOnlyList<InvoiceLine> Lines { get; init; } = [];
    public IReadOnlyList<InvoicePaymentLine> Payments { get; init; } = [];

    /// <summary>The receipt-ready Payment Summary rows — built once by
    /// <see cref="PaymentSummaryBuilder"/> from <see cref="Payments"/>, showing only the fields
    /// relevant to the actual payment scenario (e.g. no "Cash Received"/"Change Returned" for a
    /// UPI-only sale, no such rows at all for a split where cash was tendered exactly). The renderer
    /// displays these as-is rather than deciding anything itself.</summary>
    public IReadOnlyList<InvoicePaymentSummaryLine> PaymentSummaryLines { get; init; } = [];

    public IReadOnlyList<InvoiceGstGroup> GstGroups { get; init; } = [];

    public decimal SubTotal { get; init; }
    public decimal ItemDiscountTotal { get; init; }
    public decimal PromotionDiscountTotal { get; init; }
    public decimal BillDiscountPercent { get; init; }
    public decimal BillDiscountAmount { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal RoundOffAmount { get; init; }
    public decimal GrandTotal { get; init; }

    /// <summary>Total of (MRP − what was actually charged) across every line, floored at zero —
    /// never negative even if a price override pushed a line above its own MRP. Zero for any sale
    /// completed before this field existed (its snapshot defaults to 0), which correctly suppresses
    /// the "You Saved" row on an old invoice rather than showing a meaningless number.</summary>
    public decimal TotalSavings { get; init; }

    public bool HasGst => TaxTotal != 0;
    public bool HasSavings => TotalSavings > 0;
}
