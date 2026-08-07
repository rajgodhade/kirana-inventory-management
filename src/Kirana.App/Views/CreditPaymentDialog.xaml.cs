using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class CreditPaymentDialog : ContentDialog
{
    private readonly CustomerLedgerViewModel _viewModel;

    /// <summary>The repayment recorded by this dialog, or null if it was cancelled or failed. The
    /// <em>caller</em> reads this and drives receipt printing after this dialog has closed — opening
    /// a print dialog from inside this one would nest ContentDialogs and kill the app.</summary>
    public CreditPayment? RecordedPayment { get; private set; }

    public bool ShouldPrintReceipt => PrintReceiptCheck.IsChecked == true;

    public CreditPaymentDialog(CustomerLedgerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        CustomerNameText.Text = viewModel.CustomerName;
        CustomerCodeText.Text = viewModel.CustomerCode;
        OutstandingText.Text = viewModel.OutstandingDisplay;

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void OnPayFullClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        AmountBox.Text = _viewModel.OutstandingBalance.ToString("0.00");

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            {
                ShowError("Enter a valid repayment amount greater than zero.");
                args.Cancel = true;
                return;
            }

            var method = (MethodBox.SelectedItem as ComboBoxItem)?.Tag switch
            {
                "Upi" => PaymentMethod.Upi,
                "Card" => PaymentMethod.Card,
                _ => PaymentMethod.Cash,
            };

            // The service is the authority on overpayment — this dialog deliberately does not
            // pre-validate against the displayed balance, which could be stale.
            var payment = await _viewModel.RecordRepaymentAsync(
                amount, method,
                string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text,
                string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text);

            if (payment is null)
            {
                ShowError(_viewModel.ErrorMessage ?? "Could not record the repayment.");
                args.Cancel = true;
                return;
            }

            RecordedPayment = payment;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
