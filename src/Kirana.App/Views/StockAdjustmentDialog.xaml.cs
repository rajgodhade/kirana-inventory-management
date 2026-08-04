using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class StockAdjustmentDialog : ContentDialog
{
    public StockAdjustmentViewModel ViewModel { get; }

    public StockAdjustmentDialog(StockAdjustmentViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = $"Adjust Stock — {viewModel.ProductName}";
        Loaded += async (_, _) => await ViewModel.LoadHistoryAsync();
    }
}
