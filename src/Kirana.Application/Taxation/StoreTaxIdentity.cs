using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

public sealed record StoreTaxIdentity(
    string TradeName,
    string? LegalName,
    string? Gstin,
    string? StateCode,
    GstRegistrationType? RegistrationType);

public sealed class UpdateStoreTaxIdentityRequest
{
    public required string TradeName { get; init; }
    public string? LegalName { get; init; }
    public string? Gstin { get; init; }
    public string? StateCode { get; init; }
    public GstRegistrationType? RegistrationType { get; init; }
    public int? PerformedByUserId { get; init; }
}
