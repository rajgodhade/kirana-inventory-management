using System.Diagnostics;
using Kirana.Application.Billing;
using Kirana.Domain.Entities;

namespace Kirana.Application.Taxation;

/// <summary>The canonical sales GST engine. New sales are calculated once here and persisted as
/// line/invoice snapshots. Every downstream consumer aggregates those stored snapshots instead of
/// calculating tax again.</summary>
public sealed class GstCalculationService : IGstCalculationService
{
    public static GstCalculationService Shared { get; } = new();

    public CartTotals CalculateSales(
        IReadOnlyList<CartLine> lines, decimal billDiscountPercent, bool isGstEnabledForStore)
    {
        ValidateInputs(lines, billDiscountPercent);

        // Keep full decimal precision through the pricing pipeline. Monetary rounding happens only
        // when the final immutable line snapshots are materialized below.
        var raw = lines.Select(line =>
        {
            var gross = line.Quantity * line.UnitPrice;
            var itemDiscount = gross * line.DiscountPercent / 100m;
            var afterItemDiscount = Math.Max(0m, gross - itemDiscount);

            // CalculationMode is retained as a historical/audit attribute, but GST law requires
            // every promotion discount to reduce consideration before the taxable value is found.
            var requestedPromotion = line.PromotionBeforeTaxDiscountAmount + line.PromotionAfterTaxDiscountAmount;
            var promotionDiscount = Math.Min(requestedPromotion, afterItemDiscount);
            var afterPromotion = afterItemDiscount - promotionDiscount;
            var billDiscount = afterPromotion * billDiscountPercent / 100m;
            var netForTax = Math.Max(0m, afterPromotion - billDiscount);
            var rate = isGstEnabledForStore ? line.GstRatePercent : 0m;

            decimal taxable;
            decimal gst;
            if (line.PricingType == PricingType.Inclusive && rate > 0m)
            {
                taxable = netForTax / (1m + rate / 100m);
                gst = netForTax - taxable;
            }
            else
            {
                taxable = netForTax;
                gst = rate > 0m ? taxable * rate / 100m : 0m;
            }

            return new RawSalesLine(line, gross, itemDiscount, promotionDiscount, billDiscount, taxable, gst);
        }).ToList();

        var gross = FinalizeAmounts(raw.Select(x => x.Gross).ToList());
        var itemDiscounts = FinalizeAmounts(raw.Select(x => x.ItemDiscount).ToList());
        var promotionDiscounts = FinalizeAmounts(raw.Select(x => x.PromotionDiscount).ToList());
        var taxable = raw.Select(x => RoundCurrency(x.Taxable)).ToArray();
        var gst = raw.Select((x, index) => x.Line.PricingType == PricingType.Inclusive
            ? RoundCurrency(x.Taxable + x.Gst) - taxable[index]
            : RoundCurrency(x.Gst)).ToArray();

        var lineResults = raw.Select((x, index) => new CartLineResult
        {
            Line = x.Line,
            GrossAmount = gross[index],
            DiscountAmount = itemDiscounts[index],
            PromotionDiscountAmount = promotionDiscounts[index],
            TaxableAmount = taxable[index],
            GstAmount = gst[index],
            LineTotal = taxable[index] + gst[index],
        }).ToList();

        var preRoundTotal = lineResults.Sum(x => x.LineTotal);
        var grandTotal = Math.Round(preRoundTotal, 0, MidpointRounding.AwayFromZero);
        var totals = new CartTotals
        {
            Lines = lineResults,
            SubTotal = gross.Sum(),
            ItemDiscountTotal = itemDiscounts.Sum(),
            PromotionDiscountTotal = promotionDiscounts.Sum(),
            BillDiscountPercent = billDiscountPercent,
            BillDiscountAmount = RoundCurrency(raw.Sum(x => x.BillDiscount)),
            TaxableTotal = taxable.Sum(),
            GstTotal = gst.Sum(),
            RoundOffAmount = grandTotal - preRoundTotal,
            GrandTotal = grandTotal,
        };

        var snapshots = lineResults.Select((line, index) => new GstSnapshotLine
        {
            TransactionId = 1,
            RatePercent = isGstEnabledForStore ? raw[index].Line.GstRatePercent : 0m,
            TaxableAmount = line.TaxableAmount,
            GstAmount = line.GstAmount,
            PricingType = raw[index].Line.PricingType,
        }).ToList();
        for (var index = 0; index < lineResults.Count; index++)
        {
            var line = lineResults[index];
            var expected = raw[index].Line.PricingType == PricingType.Inclusive
                ? RoundCurrency(raw[index].Taxable + raw[index].Gst)
                : line.TaxableAmount + line.GstAmount;
            if (line.LineTotal != expected)
            {
                Trace.TraceError("Pricing-mode invariant mismatch for product {0}: mode={1}, expected={2}, actual={3}",
                    line.Line.ProductId, line.Line.PricingType, expected, line.LineTotal);
                throw new InvalidOperationException("The pricing calculation failed its internal consistency checks.");
            }
        }
        if (!ValidateStored(snapshots, new GstStoredTotals
        {
            TaxableTotal = totals.TaxableTotal,
            GstTotal = totals.GstTotal,
            RoundOffAmount = totals.RoundOffAmount,
            GrandTotal = totals.GrandTotal,
        }, "new sale"))
        {
            throw new InvalidOperationException("The GST calculation failed its internal consistency checks.");
        }

        return totals;
    }

    public IReadOnlyList<GstSlabSummary> SummarizeStored(IReadOnlyList<GstSnapshotLine> lines) => lines
        .GroupBy(x => new { x.RatePercent, x.PricingType })
        .Select(group => new GstSlabSummary
        {
            RatePercent = group.Key.RatePercent,
            TaxableAmount = group.Sum(x => x.TaxableAmount),
            GstAmount = group.Sum(x => x.GstAmount),
            InvoiceCount = group.Select(x => x.TransactionId).Distinct().Count(),
            PricingType = group.Key.PricingType,
        })
        .OrderBy(x => x.RatePercent).ThenBy(x => x.PricingType)
        .ToList();

    public bool ValidateStored(IReadOnlyList<GstSnapshotLine> lines, GstStoredTotals totals, string context)
    {
        var taxable = lines.Sum(x => x.TaxableAmount);
        var gst = lines.Sum(x => x.GstAmount);
        var expectedGrand = taxable + gst + totals.RoundOffAmount;
        var valid = taxable == totals.TaxableTotal && gst == totals.GstTotal && expectedGrand == totals.GrandTotal;
        if (!valid)
        {
            Trace.TraceError(
                "GST invariant mismatch for {0}: line taxable={1}, stored taxable={2}, line GST={3}, stored GST={4}, expected grand={5}, stored grand={6}",
                context, taxable, totals.TaxableTotal, gst, totals.GstTotal, expectedGrand, totals.GrandTotal);
        }

        return valid;
    }

    private static void ValidateInputs(IReadOnlyList<CartLine> lines, decimal billDiscountPercent)
    {
        if (billDiscountPercent is < 0m or > 100m)
            throw new ArgumentException("Bill discount percent must be between 0 and 100.", nameof(billDiscountPercent));

        foreach (var line in lines)
        {
            if (line.Quantity <= 0m) throw new ArgumentException($"Quantity for product {line.ProductId} must be positive.");
            if (line.UnitPrice < 0m) throw new ArgumentException($"Unit price for product {line.ProductId} cannot be negative.");
            if (line.DiscountPercent is < 0m or > 100m)
                throw new ArgumentException($"Discount percent for product {line.ProductId} must be between 0 and 100.");
            if (line.PromotionBeforeTaxDiscountAmount < 0m || line.PromotionAfterTaxDiscountAmount < 0m)
                throw new ArgumentException($"Promotion discount for product {line.ProductId} cannot be negative.");
            GstRatePolicy.EnsureSupported(line.GstRatePercent, nameof(line.GstRatePercent));
        }
    }

    internal static decimal RoundCurrency(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<decimal> FinalizeAmounts(IReadOnlyList<decimal> raw)
    {
        if (raw.Count == 0) return [];
        var result = raw.Select(RoundCurrency).ToArray();
        var target = RoundCurrency(raw.Sum());
        var difference = target - result.Sum();
        if (difference != 0m)
        {
            // Assign the paise residual deterministically to the largest amount. This preserves the
            // invoice total without repeatedly rounding intermediate stages.
            var index = raw.Select((value, i) => (value, i)).OrderByDescending(x => x.value).ThenBy(x => x.i).First().i;
            result[index] += difference;
        }
        return result;
    }

    private sealed record RawSalesLine(
        CartLine Line, decimal Gross, decimal ItemDiscount, decimal PromotionDiscount,
        decimal BillDiscount, decimal Taxable, decimal Gst);
}
