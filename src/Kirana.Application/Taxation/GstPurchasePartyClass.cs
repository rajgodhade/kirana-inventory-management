namespace Kirana.Application.Taxation;

/// <summary>
/// Purchase-side supplier identity. B2C terminology is intentionally not applied to procurement.
/// </summary>
public enum GstPurchasePartyClass
{
    Unresolved,
    RegisteredSupplier,
    UnregisteredSupplier,
}
