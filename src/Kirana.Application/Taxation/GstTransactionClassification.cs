using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>A deterministic sale classification and the historical evidence behind it.</summary>
public sealed record GstTransactionClassification(
    GstTransactionClass Classification,
    GstIdentityClassificationReason Reason,
    GstRegistrationType? RegistrationType,
    bool GstinPresent,
    GstHistoricalIdentitySource HistoricalIdentitySource)
{
    public bool IsResolved => Classification != GstTransactionClass.Unresolved;
}
