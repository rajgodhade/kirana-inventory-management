namespace Kirana.Application.Billing;

/// <summary>One cart line as submitted to <see cref="ISaleService.CompleteSaleAsync"/> — price
/// and GST are always pulled from the current <c>Product</c> record at completion time, not
/// supplied by the caller, so a client can't smuggle in an arbitrary price.</summary>
public sealed class SaleLineInput
{
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public decimal DiscountPercent { get; init; }
}
