namespace Kirana.App.ViewModels;

/// <summary>One item row in the read-only Purchase Details dialog. Flattened straight off
/// <c>PurchaseItem</c>'s snapshot fields — no computation, just display formatting.</summary>
public sealed class PurchaseDetailsLineViewModel
{
    // Non-required (see PurchaseDetailsViewModel for why) — always built via object initializer in
    // PurchaseDetailsViewModel.FromPurchase, never constructed bare.
    public string ProductName { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string QuantityText { get; init; } = "";
    public string UnitText { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public string? BatchNumber { get; init; }

    public bool HasBatch => !string.IsNullOrWhiteSpace(BatchNumber);
}
