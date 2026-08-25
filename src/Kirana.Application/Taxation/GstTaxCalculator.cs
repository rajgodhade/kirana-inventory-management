namespace Kirana.Application.Taxation;

/// <summary>
/// The Phase 18A-5 GST arithmetic engine. Jurisdiction comes exclusively from the supplied
/// context (never from current master data); the split is IntraState → CGST + SGST,
/// InterState → IGST; unresolved jurisdiction yields a typed unresolved result with zero
/// components rather than a guess. Rounding reuses the application's single financial rounding
/// policy (<see cref="GstCalculationService.RoundCurrency"/>: 2 decimals, away from zero), and
/// intra-state splits round CGST once and take SGST as the exact remainder so components always
/// reconcile to the stored total.
/// </summary>
public sealed class GstTaxCalculator : IGstTaxCalculator
{
    public static GstTaxCalculator Shared { get; } = new();

    public GstTaxCalculation Calculate(GstTaxContext context, decimal taxableValue, decimal gstRatePercent)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CalculateCore(context.JurisdictionResolution, taxableValue, gstRatePercent, knownTotalGst: null);
    }

    public GstTaxCalculation Calculate(GstPurchaseTaxContext context, decimal taxableValue, decimal gstRatePercent)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CalculateCore(context.JurisdictionResolution, taxableValue, gstRatePercent, knownTotalGst: null);
    }

    public GstTaxCalculation Calculate(GstJurisdictionResolution jurisdiction, decimal taxableValue, decimal gstRatePercent)
    {
        ArgumentNullException.ThrowIfNull(jurisdiction);
        return CalculateCore(jurisdiction, taxableValue, gstRatePercent, knownTotalGst: null);
    }

    public GstTaxCalculation SplitStored(GstTaxContext context, decimal taxableValue, decimal totalGst)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CalculateCore(context.JurisdictionResolution, taxableValue, gstRatePercent: 0m, knownTotalGst: totalGst);
    }

    public GstTaxCalculation SplitStored(GstPurchaseTaxContext context, decimal taxableValue, decimal totalGst)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CalculateCore(context.JurisdictionResolution, taxableValue, gstRatePercent: 0m, knownTotalGst: totalGst);
    }

    public GstTaxCalculation SplitStored(GstJurisdictionResolution jurisdiction, decimal taxableValue, decimal totalGst)
    {
        ArgumentNullException.ThrowIfNull(jurisdiction);
        return CalculateCore(jurisdiction, taxableValue, gstRatePercent: 0m, knownTotalGst: totalGst);
    }

    private static GstTaxCalculation CalculateCore(
        GstJurisdictionResolution jurisdiction,
        decimal taxableValue,
        decimal gstRatePercent,
        decimal? knownTotalGst)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taxableValue);
        if (knownTotalGst.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(knownTotalGst.Value);
        }
        else
        {
            // Rate validation applies only when this call derives the tax amount from the rate;
            // SplitStored distributes an already-stored total whose rate lives in the snapshots.
            GstRatePolicy.EnsureSupported(gstRatePercent, nameof(gstRatePercent));
        }

        if (!jurisdiction.IsResolved)
        {
            // Never assume intra-state, inter-state, or zero. The stored/derived total remains the
            // caller's responsibility (e.g. the report's Unresolved column); no component amounts.
            return GstTaxCalculation.Unresolved(jurisdiction, taxableValue, gstRatePercent);
        }

        var totalGst = knownTotalGst ?? GstCalculationService.RoundCurrency(taxableValue * gstRatePercent / 100m);

        return jurisdiction.Jurisdiction switch
        {
            GstJurisdiction.IntraState => ResolvedIntraState(jurisdiction, taxableValue, gstRatePercent, totalGst),
            GstJurisdiction.InterState => GstTaxCalculation.Resolved(
                jurisdiction, taxableValue, gstRatePercent, totalGst,
                cgst: 0m, sgst: 0m, igst: totalGst),
            _ => throw new InvalidOperationException(
                $"Resolved jurisdiction cannot be {jurisdiction.Jurisdiction}."),
        };
    }

    private static GstTaxCalculation ResolvedIntraState(
        GstJurisdictionResolution jurisdiction,
        decimal taxableValue,
        decimal gstRate,
        decimal totalGst)
    {
        var cgst = GstCalculationService.RoundCurrency(totalGst / 2m);
        var sgst = totalGst - cgst;
        return GstTaxCalculation.Resolved(
            jurisdiction, taxableValue, gstRate, totalGst,
            cgst: cgst, sgst: sgst, igst: 0m);
    }
}
