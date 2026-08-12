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
        DialogTitleText.Text = viewModel.IsEditMode ? "Edit Product" : "Add Product";
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

    // Barcode row actions (Phase 13B). Wired as Click+Tag rather than per-row command bindings
    // because the rows live in a plain ItemsControl whose DataContext is the row, not the dialog's
    // ViewModel — the same Tag="{x:Bind}" pattern the other list templates in this app use.

    private async void OnSetPrimaryBarcodeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Microsoft.UI.Xaml.FrameworkElement)?.Tag is ProductBarcodeRowViewModel row)
        {
            await ViewModel.SetPrimaryBarcodeCommand.ExecuteAsync(row);
        }
    }

    private async void OnToggleBarcodeActiveClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Microsoft.UI.Xaml.FrameworkElement)?.Tag is ProductBarcodeRowViewModel row)
        {
            await ViewModel.ToggleBarcodeActiveCommand.ExecuteAsync(row);
        }
    }

    private async void OnRemoveBarcodeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Microsoft.UI.Xaml.FrameworkElement)?.Tag is ProductBarcodeRowViewModel row)
        {
            await ViewModel.RemoveBarcodeCommand.ExecuteAsync(row);
        }
    }
}
