using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>Purchase supplier identity combined with the independent Phase 18A-3 jurisdiction.</summary>
public sealed record GstPurchaseTaxContext(
    GstPurchaseTaxIdentityResolution SupplierIdentityResolution,
    GstJurisdictionResolution JurisdictionResolution)
{
    public GstPurchasePartyClass SupplierClassification => SupplierIdentityResolution.Classification;
    public GstJurisdiction Jurisdiction => JurisdictionResolution.Jurisdiction;
    public GstRegistrationType? RegistrationType => SupplierIdentityResolution.RegistrationType;
    public string? SellerStateCode => JurisdictionResolution.SellerStateCode;
    public string? BuyerStateCode => JurisdictionResolution.BuyerStateCode;
}
