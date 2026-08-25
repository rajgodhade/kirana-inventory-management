using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>Sale party classification combined with the independent Phase 18A-3 jurisdiction.</summary>
public sealed record GstTaxContext(
    GstTransactionClassification ClassificationResolution,
    GstJurisdictionResolution JurisdictionResolution)
{
    public GstTransactionClass Classification => ClassificationResolution.Classification;
    public GstJurisdiction Jurisdiction => JurisdictionResolution.Jurisdiction;
    public GstRegistrationType? RegistrationType => ClassificationResolution.RegistrationType;
    public string? SellerStateCode => JurisdictionResolution.SellerStateCode;
    public string? BuyerStateCode => JurisdictionResolution.BuyerStateCode;
}
