using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class PurchasePaymentDialog : ContentDialog
{
    private readonly PurchasesViewModel _viewModel;
    private readonly int _purchaseId;
    private readonly int _supplierId;

    public PurchasePaymentDialog(PurchasesViewModel viewModel, int purchaseId, int supplierId)
    {
        _viewModel = viewModel;
        _purchaseId = purchaseId;
        _supplierId = supplierId;
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
                _purchaseId, _supplierId, amount, method,
                string.IsNullOrWhiteSpace(ReferenceBox.Text) ? null : ReferenceBox.Text);

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

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
