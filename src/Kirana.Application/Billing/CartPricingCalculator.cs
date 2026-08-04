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
    {
        if (billDiscountPercent is < 0 or > 100)
        {
            throw new ArgumentException("Bill discount percent must be between 0 and 100.", nameof(billDiscountPercent));
        }

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

        var preBillDiscount = lines.Select(line =>
        {
            var gross = Round(line.Quantity * line.UnitPrice);
            var discount = Round(gross * line.DiscountPercent / 100m);
            var afterItemDiscount = gross - discount;

            var effectiveRate = isGstEnabledForStore ? line.GstRatePercent : 0m;
            decimal taxable, gst;
            if (line.IsTaxInclusive && effectiveRate > 0)
            {
                taxable = Round(afterItemDiscount / (1 + effectiveRate / 100m));
                gst = afterItemDiscount - taxable;
            }
            else if (effectiveRate > 0)
            {
                taxable = afterItemDiscount;
                gst = Round(afterItemDiscount * effectiveRate / 100m);
            }
            else
            {
                taxable = afterItemDiscount;
                gst = 0m;
            }

            return (Line: line, gross, discount, afterItemDiscount, taxable, gst);
        }).ToList();

        var subTotal = preBillDiscount.Sum(x => x.gross);
        var itemDiscountTotal = preBillDiscount.Sum(x => x.discount);
        var sumAfterItemDiscount = preBillDiscount.Sum(x => x.afterItemDiscount);

        var billDiscountAmount = Round(sumAfterItemDiscount * billDiscountPercent / 100m);
        var scaleFactor = sumAfterItemDiscount == 0
            ? 1m
            : (sumAfterItemDiscount - billDiscountAmount) / sumAfterItemDiscount;

        var lineResults = preBillDiscount.Select(x =>
        {
            var finalTaxable = Round(x.taxable * scaleFactor);
            var finalGst = Round(x.gst * scaleFactor);
            return new CartLineResult
            {
                Line = x.Line,
                GrossAmount = x.gross,
                DiscountAmount = x.discount,
                TaxableAmount = finalTaxable,
                GstAmount = finalGst,
                LineTotal = finalTaxable + finalGst,
            };
        }).ToList();

        var taxableTotal = lineResults.Sum(l => l.TaxableAmount);
        var gstTotal = lineResults.Sum(l => l.GstAmount);
        var preRoundTotal = taxableTotal + gstTotal;
        var grandTotal = Math.Round(preRoundTotal, 0, MidpointRounding.AwayFromZero);
        var roundOff = grandTotal - preRoundTotal;

        return new CartTotals
        {
            Lines = lineResults,
            SubTotal = subTotal,
            ItemDiscountTotal = itemDiscountTotal,
            BillDiscountPercent = billDiscountPercent,
            BillDiscountAmount = billDiscountAmount,
            TaxableTotal = taxableTotal,
            GstTotal = gstTotal,
            RoundOffAmount = roundOff,
            GrandTotal = grandTotal,
        };
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
