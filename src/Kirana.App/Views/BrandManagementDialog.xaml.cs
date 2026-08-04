using Kirana.App.ViewModels;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class BrandManagementDialog : ContentDialog
{
    public BrandManagementViewModel ViewModel { get; }

    public BrandManagementDialog(int? currentUserId)
    {
        ViewModel = new BrandManagementViewModel(App.Services.GetRequiredService<IBrandService>(), currentUserId);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnToggleActiveClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Brand brand)
        {
            await ViewModel.ToggleActiveAsync(brand);
        }
    }
}
