using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>A deterministic purchase supplier-identity result based on frozen transaction data.</summary>
public sealed record GstPurchaseTaxIdentityResolution(
    GstPurchasePartyClass Classification,
    GstIdentityClassificationReason Reason,
    GstRegistrationType? RegistrationType,
    bool GstinPresent,
    GstHistoricalIdentitySource HistoricalIdentitySource)
{
    public bool IsResolved => Classification != GstPurchasePartyClass.Unresolved;
}
