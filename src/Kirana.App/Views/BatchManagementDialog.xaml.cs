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
        Title = $"Batches — {viewModel.ProductName}";
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
}
