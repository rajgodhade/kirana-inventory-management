namespace Kirana.Application.Taxation;

/// <summary>
/// Centralized Phase 18A-5 GST component calculator. Given an explicit, already-resolved tax
/// context it produces the CGST/SGST/IGST split for one taxable value, or a typed unresolved
/// result when jurisdiction could not be established. It is deterministic and read-only, and it
/// owns no jurisdiction or classification policy of its own — those remain with the Phase 18A-3
/// resolver and Phase 18A-4 classifier composed through <see cref="IGstTaxContextResolver"/>.
/// </summary>
public interface IGstTaxCalculator
{
    /// <summary>Computes GST from the taxable value and rate (exclusive semantics; the inclusive
    /// pipeline passes the already-extracted taxable value, for which taxable × rate/100 is the
    /// same extracted tax).</summary>
    GstTaxCalculation Calculate(GstTaxContext context, decimal taxableValue, decimal gstRatePercent);

    /// <summary>Purchase-side overload of <see cref="Calculate"/>.</summary>
    GstTaxCalculation Calculate(GstPurchaseTaxContext context, decimal taxableValue, decimal gstRatePercent);

    /// <summary>Jurisdiction-level primitive. Prefer the context overloads at call sites that
    /// already hold a full <see cref="GstTaxContext"/>.</summary>
    GstTaxCalculation Calculate(GstJurisdictionResolution jurisdiction, decimal taxableValue, decimal gstRatePercent);

    /// <summary>Splits an authoritative, already-stored total GST amount (e.g. persisted line or
    /// aggregate snapshots) without recomputing it. Used by historical/reporting consumers so the
    /// stored totals always remain authoritative. The returned <c>GstRate</c> is always 0 because
    /// this operation applies no rate; the authoritative rate lives in the stored snapshots.</summary>
    GstTaxCalculation SplitStored(GstTaxContext context, decimal taxableValue, decimal totalGst);

    /// <summary>Purchase-side overload of <see cref="SplitStored"/>.</summary>
    GstTaxCalculation SplitStored(GstPurchaseTaxContext context, decimal taxableValue, decimal totalGst);

    /// <summary>Jurisdiction-level primitive of <see cref="SplitStored"/>.</summary>
    GstTaxCalculation SplitStored(GstJurisdictionResolution jurisdiction, decimal taxableValue, decimal totalGst);
}