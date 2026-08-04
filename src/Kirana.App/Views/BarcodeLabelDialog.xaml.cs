using Kirana.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class BarcodeLabelDialog : ContentDialog
{
    public BarcodeLabelViewModel ViewModel { get; }

    public BarcodeLabelDialog(BarcodeLabelViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnGenerateClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is BarcodeLabelLineItem item)
        {
            await ViewModel.GenerateCommand.ExecuteAsync(item);
        }
    }

    private void OnRemoveClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is BarcodeLabelLineItem item)
        {
            ViewModel.RemoveItem(item);
        }
    }
}
