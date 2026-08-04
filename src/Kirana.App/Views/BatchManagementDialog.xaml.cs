using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class BatchManagementDialog : ContentDialog
{
    public BatchManagementViewModel ViewModel { get; }

    public BatchManagementDialog(BatchManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DialogTitleText.Text = $"Batches — {viewModel.ProductName}";
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
