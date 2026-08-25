using Kirana.Domain.Entities;

namespace Kirana.Application.Taxation;

/// <summary>
/// Resolves GST jurisdiction exclusively from immutable historical transaction snapshots.
/// Implementations must be pure and must never consult or modify current master records.
/// </summary>
public interface IGstJurisdictionResolver
{
    GstJurisdictionResolution ResolveSale(Sale sale);
    GstJurisdictionResolution ResolvePurchase(Purchase purchase);
}
