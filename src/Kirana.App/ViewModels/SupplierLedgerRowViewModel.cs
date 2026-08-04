namespace Kirana.App.ViewModels;

/// <summary>Flattened row for the Supplier Ledger screen (PRD §29). No <c>required</c> members.</summary>
public sealed class SupplierLedgerRowViewModel
{
    public string DateText { get; init; } = "";
    public string EntryType { get; init; } = "";
    public string Reference { get; init; } = "";
    public string DebitText { get; init; } = "";
    public string CreditText { get; init; } = "";
    public string RunningBalanceText { get; init; } = "";
}
