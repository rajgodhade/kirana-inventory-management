namespace Kirana.App.ViewModels;

/// <summary>One pre-formatted row of the customer ledger, purchase history, or repayment history.
/// Formatting happens here rather than in XAML so the three lists can share one row type.</summary>
public sealed class CustomerLedgerRowViewModel
{
    public int Id { get; set; }

    public string DateText { get; set; } = string.Empty;

    public string EntryType { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string DebitText { get; set; } = string.Empty;

    public string CreditText { get; set; } = string.Empty;

    public string RunningBalanceText { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>True for repayment rows, which are the only ones offering a printable receipt.</summary>
    public bool CanPrintReceipt { get; set; }
}
