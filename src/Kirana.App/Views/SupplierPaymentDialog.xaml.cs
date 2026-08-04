using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class SupplierPaymentDialog : ContentDialog
{
    private readonly SupplierLedgerViewModel _viewModel;

    public SupplierPaymentDialog(SupplierLedgerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (!decimal.TryParse(AmountBox.Text, out var amount) || amount <= 0)
            {
                ErrorBar.Message = "Enter a valid payment amount greater than zero.";
                ErrorBar.IsOpen = true;
                args.Cancel = true;
                return;
            }

            var method = (MethodBox.SelectedItem as ComboBoxItem)?.Tag switch
            {
                "Upi" => PaymentMethod.Upi,
                "Card" => PaymentMethod.Card,
                _ => PaymentMethod.Cash,
            };

            var succeeded = await _viewModel.RecordPaymentAsync(
                amount, method,
                string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text,
                string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text);

            if (!succeeded)
            {
                ErrorBar.Message = _viewModel.ErrorMessage;
                ErrorBar.IsOpen = true;
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
