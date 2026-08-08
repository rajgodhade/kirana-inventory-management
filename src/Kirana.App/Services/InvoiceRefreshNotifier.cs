namespace Kirana.App.Services;

/// <summary>In-process notification that a completed sale was saved. Read-only invoice surfaces
/// subscribe without coupling Billing to a particular page.</summary>
public sealed class InvoiceRefreshNotifier
{
    public event EventHandler? InvoicesChanged;
    public void NotifyInvoicesChanged() => InvoicesChanged?.Invoke(this, EventArgs.Empty);
}
