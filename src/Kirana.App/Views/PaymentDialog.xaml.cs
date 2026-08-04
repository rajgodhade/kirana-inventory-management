using Kirana.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class PaymentDialog : ContentDialog
{
    public PaymentViewModel ViewModel { get; }

    public PaymentDialog(PaymentViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
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
}
