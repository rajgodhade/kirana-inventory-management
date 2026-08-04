namespace Kirana.App.ViewModels;

/// <summary>Flattened row for the Suppliers list (PRD §28). No <c>required</c> members — see the
/// Phase 5 note on avoiding required members on types reachable from a bound ViewModel.</summary>
public sealed class SupplierRowViewModel
{
    public int Id { get; init; }
    public string SupplierCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public decimal OutstandingBalance { get; init; }
    public bool IsActive { get; init; }
}
