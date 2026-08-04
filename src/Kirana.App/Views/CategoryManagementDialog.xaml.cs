using Kirana.App.ViewModels;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class CategoryManagementDialog : ContentDialog
{
    public CategoryManagementViewModel ViewModel { get; }

    public CategoryManagementDialog(int? currentUserId)
    {
        ViewModel = new CategoryManagementViewModel(App.Services.GetRequiredService<ICategoryService>(), currentUserId);
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnToggleActiveClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Category category)
        {
            await ViewModel.ToggleActiveAsync(category);
        }
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
