using Kirana.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class PaymentDialog : ContentDialog
{
    public PaymentViewModel ViewModel { get; }

    /// <summary>Settles a Cash line's shortfall onto the auto-balancing last line only once the
    /// cashier actually pauses typing "Cash Received" — the same 300ms debounce shape used for
    /// live-search elsewhere in this app, and for the same reason: reacting to every partial
    /// keystroke (typing "100" passes through "1", "10") would misfire mid-type instead of once
    /// on the settled value. See <see cref="PaymentViewModel.SettleCashShortfalls"/>.</summary>
    private readonly DispatcherTimer _shortfallSettleTimer;

    public PaymentDialog(PaymentViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;

        _shortfallSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _shortfallSettleTimer.Tick += OnShortfallSettleTimerTick;
    }

    private void OnShortfallSettleTimerTick(object? sender, object e)
    {
        _shortfallSettleTimer.Stop();
        ViewModel.SettleCashShortfalls();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.CompleteSaleCommand.ExecuteAsync(null);
            if (ViewModel.ErrorMessage is not null || ViewModel.CompletedSale is null)
            {
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is PaymentLineViewModel line)
        {
            ViewModel.RemovePaymentLine(line);
        }
    }

    /// <summary>"Remaining"/the live Payment Summary need to reflect the Amount typed into any line,
    /// live — without this, they only updated on Add/Remove Payment Method, same staleness class as
    /// every other search box fixed elsewhere in this app today.</summary>
    private void OnPaymentFieldTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.RecalculateRemaining();

        // Restart rather than let a running timer fire mid-edit: every keystroke pushes the
        // shortfall settle-check another 300ms into the future until the cashier actually stops.
        _shortfallSettleTimer.Stop();
        _shortfallSettleTimer.Start();
    }

    private void OnPaymentFieldChanged(object sender, SelectionChangedEventArgs e) => ViewModel.RecalculateRemaining();

    private void OnQuickTender100Click(object sender, RoutedEventArgs e) => ApplyQuickTender(sender, 100m);
    private void OnQuickTender200Click(object sender, RoutedEventArgs e) => ApplyQuickTender(sender, 200m);
    private void OnQuickTender500Click(object sender, RoutedEventArgs e) => ApplyQuickTender(sender, 500m);
    private void OnQuickTender1000Click(object sender, RoutedEventArgs e) => ApplyQuickTender(sender, 1000m);

    /// <summary>Exact Amount: tenders exactly what that line's own Amount is (₹0 change), not the
    /// bill's grand total — a split line's "exact" is its own share, never the whole sale.</summary>
    private void OnExactAmountClick(object sender, RoutedEventArgs e) => ApplyQuickTender(sender, amount: null);

    private void ApplyQuickTender(object sender, decimal? amount)
    {
        if ((sender as Button)?.Tag is PaymentLineViewModel line)
        {
            ViewModel.ApplyQuickTender(line, amount);
        }
    }

    private void OnCloseIconClick(object sender, RoutedEventArgs e) => Hide();
}
