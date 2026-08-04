using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ProductEditDialog : ContentDialog
{
    public ProductEditViewModel ViewModel { get; }

    public ProductEditDialog(ProductEditViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = viewModel.IsEditMode ? "Edit Product" : "Add Product";
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
}
