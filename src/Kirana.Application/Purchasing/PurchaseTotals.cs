namespace Kirana.Application.Purchasing;

/// <summary>Full result of pricing a purchase — everything <c>Purchase</c>/<c>PurchaseItem</c>
/// snapshot fields are populated from.</summary>
public sealed class PurchaseTotals
{
    public required IReadOnlyList<PurchaseLineResult> Lines { get; init; }
    public decimal SubTotal { get; init; }
    public decimal DiscountTotal { get; init; }
    public decimal TaxableTotal { get; init; }
    public decimal TaxTotal { get; init; }
    public decimal RoundOffAmount { get; init; }
    public decimal GrandTotal { get; init; }
}
