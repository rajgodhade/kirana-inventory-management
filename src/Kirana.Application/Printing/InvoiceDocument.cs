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
    public IReadOnlyList<InvoiceGstGroup> GstGroups { get; init; } = [];

    public decimal SubTotal { get; init; }
    public decimal ItemDiscountTotal { get; init; }
    public decimal BillDiscountPercent { get; init; }
    public decimal BillDiscountAmount { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal RoundOffAmount { get; init; }
    public decimal GrandTotal { get; init; }

    public bool HasGst => TaxTotal != 0;
}
