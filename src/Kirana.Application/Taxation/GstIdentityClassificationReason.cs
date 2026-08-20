namespace Kirana.Application.Taxation;

/// <summary>Explains why a historical GST party identity resolved or remained unresolved.</summary>
public enum GstIdentityClassificationReason
{
    AuthoritativeRegistrationType,
    ExplicitWalkInCustomer,
    LegacyTransaction,
    MissingRegistrationType,
    MissingGstin,
    InvalidGstin,
}
