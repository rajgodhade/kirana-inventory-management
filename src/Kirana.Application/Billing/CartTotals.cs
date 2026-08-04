namespace Kirana.Application.Billing;

/// <summary>Full result of pricing a cart — everything <c>Sale</c>/<c>SaleItem</c> snapshot
/// fields are populated from (PRD §19, §21-22).</summary>
public sealed class CartTotals
{
    public required IReadOnlyList<CartLineResult> Lines { get; init; }
    public decimal SubTotal { get; init; }
    public decimal ItemDiscountTotal { get; init; }
    public decimal BillDiscountPercent { get; init; }
    public decimal BillDiscountAmount { get; init; }
    public decimal TaxableTotal { get; init; }
    public decimal GstTotal { get; init; }
    public decimal RoundOffAmount { get; init; }
    public decimal GrandTotal { get; init; }
}
