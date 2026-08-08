namespace Kirana.Application.Purchasing;

/// <summary>
/// Pure purchase pricing/GST/discount/rounding math (PRD §28) — no I/O, so it's exercised
/// directly and deterministically by tests. <see cref="PurchaseService"/> is the only caller that
/// turns the result into persisted <c>Purchase</c>/<c>PurchaseItem</c> rows.
///
/// Algorithm per line: gross = qty × price; item discount is a percentage of gross; taxable/GST
/// is split out of the post-discount amount using tax-inclusive/exclusive rules — the same
/// convention as <see cref="Billing.CartPricingCalculator"/>. Unlike billing there is no
/// bill-level discount to scale across lines (purchases only support item-level discounts per
/// PRD §28); the grand total is rounded to the nearest whole rupee, same as billing.
/// </summary>
public static class PurchasePricingCalculator
{
    public static PurchaseTotals Calculate(IReadOnlyList<PurchaseLine> lines)
        => PurchaseGstCalculationService.Shared.Calculate(lines);
}
