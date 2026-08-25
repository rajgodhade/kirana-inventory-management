namespace Kirana.Application.Taxation;

/// <summary>GST tax jurisdiction established from immutable transaction identity snapshots.</summary>
public enum GstJurisdiction
{
    Unresolved = 0,
    IntraState = 1,
    InterState = 2,
}
