namespace Kirana.Application.Printing;

/// <summary>One printed invoice line, built only from <c>SaleItem</c>'s historical snapshot
/// fields — never from live <c>Product</c> data (PRD §14, §22, §23). No <c>required</c> members
/// — see the note on <see cref="InvoiceDocument"/> for why.</summary>
public sealed class InvoiceLine
{
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public string? HsnCode { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Mrp { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal PromotionDiscountAmount { get; init; }
    public string PromotionText { get; init; } = string.Empty;
    public bool HasPromotion => PromotionDiscountAmount > 0 && PromotionText.Length > 0;
    public decimal GstRatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstAmount { get; init; }
    public decimal LineTotal { get; init; }
}
