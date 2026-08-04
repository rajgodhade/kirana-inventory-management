using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class SupplierEditDialog : ContentDialog
{
    public SupplierEditViewModel ViewModel { get; }

    public SupplierEditDialog(SupplierEditViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DialogTitleText.Text = viewModel.DialogTitle;
        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.SaveCommand.ExecuteAsync(null);
            if (ViewModel.ErrorMessage is not null)
            {
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
