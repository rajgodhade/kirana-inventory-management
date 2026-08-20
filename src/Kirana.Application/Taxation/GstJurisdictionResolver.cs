using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>
/// Pure jurisdiction resolver. StateCode snapshots are the sole authority; GSTIN prefixes,
/// display names, addresses, registration types, and today's master records are never inferred.
/// </summary>
public sealed class GstJurisdictionResolver : IGstJurisdictionResolver
{
    public static GstJurisdictionResolver Shared { get; } = new();

    public GstJurisdictionResolution ResolveSale(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        if (sale.GstIdentitySnapshotCapturedAtUtc is null)
        {
            return Unresolved(GstJurisdictionUnresolvedReason.LegacyTransaction, null, null);
        }

        return Resolve(
            sale.StoreStateCodeSnapshot,
            sale.CustomerStateCodeSnapshot,
            GstJurisdictionUnresolvedReason.MissingStoreState,
            GstJurisdictionUnresolvedReason.InvalidStoreState,
            GstJurisdictionUnresolvedReason.MissingCustomerState,
            GstJurisdictionUnresolvedReason.InvalidCustomerState);
    }

    public GstJurisdictionResolution ResolvePurchase(Purchase purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        if (purchase.GstIdentitySnapshotCapturedAtUtc is null)
        {
            return Unresolved(GstJurisdictionUnresolvedReason.LegacyTransaction, null, null);
        }

        return Resolve(
            purchase.SupplierStateCodeSnapshot,
            purchase.StoreStateCodeSnapshot,
            GstJurisdictionUnresolvedReason.MissingSupplierState,
            GstJurisdictionUnresolvedReason.InvalidSupplierState,
            GstJurisdictionUnresolvedReason.MissingStoreState,
            GstJurisdictionUnresolvedReason.InvalidStoreState);
    }

    private static GstJurisdictionResolution Resolve(
        string? sellerStateCode,
        string? buyerStateCode,
        GstJurisdictionUnresolvedReason missingSellerReason,
        GstJurisdictionUnresolvedReason invalidSellerReason,
        GstJurisdictionUnresolvedReason missingBuyerReason,
        GstJurisdictionUnresolvedReason invalidBuyerReason)
    {
        var seller = Normalize(sellerStateCode);
        var buyer = Normalize(buyerStateCode);

        if (seller is null)
        {
            return Unresolved(missingSellerReason, null, buyer);
        }

        if (!IndianGstStateCatalog.IsValidCode(seller))
        {
            return Unresolved(invalidSellerReason, seller, buyer);
        }

        if (buyer is null)
        {
            return Unresolved(missingBuyerReason, seller, null);
        }

        if (!IndianGstStateCatalog.IsValidCode(buyer))
        {
            return Unresolved(invalidBuyerReason, seller, buyer);
        }

        return new GstJurisdictionResolution(
            seller == buyer ? GstJurisdiction.IntraState : GstJurisdiction.InterState,
            GstJurisdictionUnresolvedReason.None,
            seller,
            buyer);
    }

    private static string? Normalize(string? stateCode) =>
        string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim();

    private static GstJurisdictionResolution Unresolved(
        GstJurisdictionUnresolvedReason reason, string? seller, string? buyer) =>
        new(GstJurisdiction.Unresolved, reason, seller, buyer);
}
