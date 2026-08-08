using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using System.Diagnostics;

namespace Kirana.Application.Purchasing;

/// <summary>Canonical purchase GST calculator. It deliberately has its own contract because
/// purchases do not have sales promotions or a bill-level discount.</summary>
public sealed class PurchaseGstCalculationService : IPurchaseGstCalculationService
{
    public static PurchaseGstCalculationService Shared { get; } = new();

    public PurchaseTotals Calculate(IReadOnlyList<PurchaseLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.Quantity <= 0m) throw new ArgumentException($"Quantity for product {line.ProductId} must be positive.");
            if (line.UnitPrice < 0m) throw new ArgumentException($"Unit price for product {line.ProductId} cannot be negative.");
            if (line.DiscountPercent is < 0m or > 100m)
                throw new ArgumentException($"Discount percent for product {line.ProductId} must be between 0 and 100.");
            GstRatePolicy.EnsureSupported(line.GstRatePercent, nameof(line.GstRatePercent));
        }

        var raw = lines.Select(line =>
        {
            var gross = line.Quantity * line.UnitPrice;
            var discount = gross * line.DiscountPercent / 100m;
            var netForTax = Math.Max(0m, gross - discount);
            decimal taxable;
            decimal gst;
            if (line.PricingType == PricingType.Inclusive && line.GstRatePercent > 0m)
            {
                taxable = netForTax / (1m + line.GstRatePercent / 100m);
                gst = netForTax - taxable;
            }
            else
            {
                taxable = netForTax;
                gst = line.GstRatePercent > 0m ? taxable * line.GstRatePercent / 100m : 0m;
            }
            return new RawPurchaseLine(line, gross, discount, taxable, gst);
        }).ToList();

        var results = raw.Select(x =>
        {
            var taxable = Round(x.Taxable);
            var gst = x.Line.PricingType == PricingType.Inclusive
                ? Round(x.Taxable + x.Gst) - taxable
                : Round(x.Gst);
            return new PurchaseLineResult
            {
                Line = x.Line,
                GrossAmount = Round(x.Gross),
                DiscountAmount = Round(x.Discount),
                TaxableAmount = taxable,
                GstAmount = gst,
                LineTotal = taxable + gst,
            };
        }).ToList();

        foreach (var result in results)
        {
            var expected = result.TaxableAmount + result.GstAmount;
            if (result.LineTotal != expected)
            {
                Trace.TraceError(
                    "Purchase pricing-mode invariant mismatch for product {0}: mode={1}, expected={2}, actual={3}",
                    result.Line.ProductId, result.Line.PricingType, expected, result.LineTotal);
                throw new InvalidOperationException("The purchase pricing calculation failed its internal consistency checks.");
            }
        }

        var taxableTotal = results.Sum(x => x.TaxableAmount);
        var taxTotal = results.Sum(x => x.GstAmount);
        var preRoundTotal = taxableTotal + taxTotal;
        var grandTotal = Math.Round(preRoundTotal, 0, MidpointRounding.AwayFromZero);
        return new PurchaseTotals
        {
            Lines = results,
            SubTotal = results.Sum(x => x.GrossAmount),
            DiscountTotal = results.Sum(x => x.DiscountAmount),
            TaxableTotal = taxableTotal,
            TaxTotal = taxTotal,
            RoundOffAmount = grandTotal - preRoundTotal,
            GrandTotal = grandTotal,
        };
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private sealed record RawPurchaseLine(PurchaseLine Line, decimal Gross, decimal Discount, decimal Taxable, decimal Gst);
}
