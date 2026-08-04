namespace Kirana.App.ViewModels;

/// <summary>Flattened row for the Purchases list (PRD §28). No <c>required</c> members.</summary>
public sealed class PurchaseRowViewModel
{
    public int Id { get; init; }
    public string PurchaseNumber { get; init; } = "";
    public string SupplierName { get; init; } = "";
    public string DateText { get; init; } = "";
    public decimal GrandTotal { get; init; }
    public decimal OutstandingAmount { get; init; }
}
