using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace Kirana.App.Views;

public sealed partial class PurchasePaymentDialog : ContentDialog
{
    private readonly PurchasesViewModel _viewModel;
    private readonly int _purchaseId;
    private readonly int _supplierId;
    private readonly PurchaseRowViewModel _row;
    private bool _suppressAmountValidation;

    public PurchasePaymentDialog(PurchasesViewModel viewModel, int purchaseId, int supplierId, PurchaseRowViewModel row)
    {
        _viewModel = viewModel;
        _purchaseId = purchaseId;
        _supplierId = supplierId;
        _row = row;
        InitializeComponent();

        PurchaseNumberText.Text = row.PurchaseNumber;
        SupplierNameText.Text = row.SupplierName;
        TotalText.Text = row.GrandTotal.ToString("C2");
        PaidText.Text = row.AmountPaid.ToString("C2");
        OutstandingText.Text = row.OutstandingAmount.ToString("C2");

        if (row.IsFullyOutstanding)
        {
            StatusBadgeText.Text = "\U0001F534 Outstanding";
            StatusBadgeBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["DangerSubtleBrush"];
            StatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["DangerBrush"];
        }
        else
        {
            StatusBadgeText.Text = "\U0001F7E1 Partially Paid";
            StatusBadgeBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["WarningSubtleBrush"];
            StatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["WarningBrush"];
        }

        // Outstanding is the amount a payment almost always intends to clear, so it's the
        // natural default rather than an empty box the owner has to look up and retype.
        _suppressAmountValidation = true;
        AmountBox.Text = row.OutstandingAmount.ToString("0.00");
        _suppressAmountValidation = false;

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private void OnPayFullClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        AmountBox.Text = _row.OutstandingAmount.ToString("0.00");
    }

    private void OnAmountBoxTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        if (_suppressAmountValidation)
        {
            return;
        }

        // Surfacing the overpayment as you type (rather than only on Save) keeps the outstanding
        // figure above meaningfully connected to what's being entered.
        if (decimal.TryParse(AmountBox.Text, out var amount) && amount > _row.OutstandingAmount)
        {
            ErrorBar.Message = $"Amount exceeds the outstanding balance of {_row.OutstandingAmount:C2}.";
            ErrorBar.IsOpen = true;
        }
        else
        {
            ErrorBar.IsOpen = false;
        }
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

            if (amount > _row.OutstandingAmount)
            {
                ErrorBar.Message = $"Amount exceeds the outstanding balance of {_row.OutstandingAmount:C2}.";
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
