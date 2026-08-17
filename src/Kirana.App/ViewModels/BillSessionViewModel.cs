using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// One browser-style billing tab. Holds everything that makes a bill distinct — cart, customer,
/// bill discount, manager authorizations, and the cashier's note — so several customers can be
/// served at once without losing anyone's basket.
///
/// This is a plain state container, deliberately: <see cref="PosShellViewModel"/> stays the single
/// "active bill" surface that every binding, dialog and <see cref="PaymentViewModel"/> already talks
/// to, and switching tabs snapshots the live state into the outgoing session then loads the
/// incoming one back. That keeps pricing, payment and hold/resume behaviour completely unchanged —
/// tabs are a storage concern, not a recalculation concern.
/// </summary>
public sealed partial class BillSessionViewModel : ObservableObject
{
    /// <summary>Stable identity for the tab, independent of its position in the strip.</summary>
    public required int Id { get; init; }

    [ObservableProperty]
    private string _title = "Bill 1";

    /// <summary>Highlights the selected tab in the strip.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Shows a dot on the tab so a cashier can see at a glance which parked bills still
    /// hold goods, without switching to each one.</summary>
    [ObservableProperty]
    private bool _hasItems;

    /// <summary>Customer name (or "Walk-in") shown under the tab title.</summary>
    [ObservableProperty]
    private string _customerSummary = "Walk-in";

    // --- Snapshotted bill state (only meaningful while this tab is inactive) ---

    public List<CartLineViewModel> Lines { get; set; } = [];

    public Customer? Customer { get; set; }

    public decimal BillDiscountPercent { get; set; }

    /// <summary>
    /// Which price level this bill sells at (Phase 15B-3). Per-tab, so a wholesale bill parked on
    /// tab 2 cannot leak its level into the retail bill on tab 1, and a brand-new tab starts at
    /// Retail because that is this field's default.
    /// </summary>
    public PriceLevel PriceLevel { get; set; } = PriceLevel.Retail;

    public int? DiscountAuthorizedByUserId { get; set; }

    public int? PriceOverrideAuthorizedByUserId { get; set; }

    /// <summary>Free-text note for this bill. Carried into <c>IHeldBillService.HoldAsync</c>'s
    /// existing <c>note</c> parameter when the bill is held — no schema change involved.</summary>
    public string Note { get; set; } = string.Empty;
}
