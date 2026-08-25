namespace Kirana.Application.Taxation;

/// <summary>A deterministic GST jurisdiction decision and the historical evidence used.</summary>
public sealed record GstJurisdictionResolution(
    GstJurisdiction Jurisdiction,
    GstJurisdictionUnresolvedReason UnresolvedReason,
    string? SellerStateCode,
    string? BuyerStateCode)
{
    public bool IsResolved => Jurisdiction != GstJurisdiction.Unresolved;
}
