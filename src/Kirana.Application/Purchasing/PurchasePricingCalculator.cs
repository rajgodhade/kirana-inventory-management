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
    {
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for product {line.ProductId} must be positive.");
            }

            if (line.UnitPrice < 0)
            {
                throw new ArgumentException($"Unit price for product {line.ProductId} cannot be negative.");
            }

            if (line.DiscountPercent is < 0 or > 100)
            {
                throw new ArgumentException($"Discount percent for product {line.ProductId} must be between 0 and 100.");
            }
        }

        var lineResults = lines.Select(line =>
        {
            var gross = Round(line.Quantity * line.UnitPrice);
            var discount = Round(gross * line.DiscountPercent / 100m);
            var afterDiscount = gross - discount;

            decimal taxable, gst;
            if (line.IsTaxInclusive && line.GstRatePercent > 0)
            {
                taxable = Round(afterDiscount / (1 + line.GstRatePercent / 100m));
                gst = afterDiscount - taxable;
            }
            else if (line.GstRatePercent > 0)
            {
                taxable = afterDiscount;
                gst = Round(afterDiscount * line.GstRatePercent / 100m);
            }
            else
            {
                taxable = afterDiscount;
                gst = 0m;
            }

            return new PurchaseLineResult
            {
                Line = line,
                GrossAmount = gross,
                DiscountAmount = discount,
                TaxableAmount = taxable,
                GstAmount = gst,
                LineTotal = taxable + gst,
            };
        }).ToList();

        var subTotal = lineResults.Sum(l => l.GrossAmount);
        var discountTotal = lineResults.Sum(l => l.DiscountAmount);
        var taxableTotal = lineResults.Sum(l => l.TaxableAmount);
        var taxTotal = lineResults.Sum(l => l.GstAmount);
        var preRoundTotal = taxableTotal + taxTotal;
        var grandTotal = Math.Round(preRoundTotal, 0, MidpointRounding.AwayFromZero);
        var roundOff = grandTotal - preRoundTotal;

        return new PurchaseTotals
        {
            Lines = lineResults,
            SubTotal = subTotal,
            DiscountTotal = discountTotal,
            TaxableTotal = taxableTotal,
            TaxTotal = taxTotal,
            RoundOffAmount = roundOff,
            GrandTotal = grandTotal,
        };
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
