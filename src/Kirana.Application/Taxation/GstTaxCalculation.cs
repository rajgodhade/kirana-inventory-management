namespace Kirana.Application.Taxation;

/// <summary>
/// Typed outcome of the centralized GST component calculation (Phase 18A-5).
/// <see cref="IsResolved"/> is the sole authority for whether the split may be used: a resolved
/// result whose components are all zero is a genuine 0%/exempt line, while an unresolved result
/// never carries component amounts and records exactly why jurisdiction could not be established.
/// The two states must never be conflated — an unresolved calculation is never assumed to be zero,
/// intra-state, or inter-state.
/// </summary>
public sealed record GstTaxCalculation(
    bool IsResolved,
    decimal TaxableValue,
    decimal GstRate,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal TotalGst,
    GstJurisdiction Jurisdiction,
    GstJurisdictionUnresolvedReason UnresolvedReason)
{
    /// <summary>Reconciliation invariant for any returned result: Cgst + Sgst + Igst == TotalGst.</summary>
    public bool ComponentsReconcile => Cgst + Sgst + Igst == TotalGst;

    internal static GstTaxCalculation Resolved(
        GstJurisdictionResolution jurisdiction,
        decimal taxableValue,
        decimal gstRate,
        decimal totalGst,
        decimal cgst,
        decimal sgst,
        decimal igst) => new(
            IsResolved: true,
            TaxableValue: taxableValue,
            GstRate: gstRate,
            Cgst: cgst,
            Sgst: sgst,
            Igst: igst,
            TotalGst: totalGst,
            Jurisdiction: jurisdiction.Jurisdiction,
            UnresolvedReason: GstJurisdictionUnresolvedReason.None);

    internal static GstTaxCalculation Unresolved(
        GstJurisdictionResolution jurisdiction,
        decimal taxableValue,
        decimal gstRate) => new(
            IsResolved: false,
            TaxableValue: taxableValue,
            GstRate: gstRate,
            Cgst: 0m,
            Sgst: 0m,
            Igst: 0m,
            TotalGst: 0m,
            Jurisdiction: jurisdiction.Jurisdiction,
            UnresolvedReason: jurisdiction.UnresolvedReason);
}