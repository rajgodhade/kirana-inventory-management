namespace Kirana.Application.Taxation;

/// <summary>Why a transaction's GST jurisdiction could not be established safely.</summary>
public enum GstJurisdictionUnresolvedReason
{
    None = 0,
    LegacyTransaction,
    MissingStoreState,
    MissingCustomerState,
    MissingSupplierState,
    InvalidStoreState,
    InvalidCustomerState,
    InvalidSupplierState,
}
