using Kirana.Application.Taxation;

namespace Kirana.Application.Billing;

/// <summary>
/// Pure cart pricing/GST/discount/rounding math (PRD §14, §19, §21) — no I/O, so it's exercised
/// directly and deterministically by tests. <see cref="SaleService"/> is the only caller that
/// turns the result into persisted <c>Sale</c>/<c>SaleItem</c> rows.
///
/// Algorithm per line: gross = qty × price; item discount is a percentage of gross; taxable/GST
/// is split out of the post-item-discount amount using tax-inclusive/exclusive rules. The
/// bill-level discount is then applied as one proportional scale-down across every line (so each
/// line's tax keeps making sense) rather than as a flat amount subtracted at the end. The grand
/// total is rounded to the nearest whole rupee (PRD §19 "Round-off"); everything else is rounded
/// to paise (2 decimals) as it's produced.
/// </summary>
public static class CartPricingCalculator
{
    public static CartTotals Calculate(IReadOnlyList<CartLine> lines, decimal billDiscountPercent, bool isGstEnabledForStore)
        => GstCalculationService.Shared.CalculateSales(lines, billDiscountPercent, isGstEnabledForStore);
}
