using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Inventories;
using Kirana.Application.Products;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class ProductsPage : Page
{
    public ProductsViewModel ViewModel { get; }

    public ProductsPage()
    {
        var services = App.Services;
        ViewModel = new ProductsViewModel(
            services.GetRequiredService<IProductService>(),
            services.GetRequiredService<ICategoryService>(),
            services.GetRequiredService<IBrandService>(),
            services.GetRequiredService<IInventoryService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ManagementPlaceholderPage));

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<IAuthenticationService>().LockAndReturnToBilling();
        Frame.Navigate(typeof(PosShellPage));
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await ViewModel.SearchAsync();
        }
    }

    private async void OnCategoryOrBrandChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnShowInactiveChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnManageCategoriesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CategoryManagementDialog(ViewModel.CurrentUserId) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.ReloadFilterOptionsAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnManageBrandsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new BrandManagementDialog(ViewModel.CurrentUserId) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.ReloadFilterOptionsAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnAddProductClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditProducts)
        {
            return;
        }

        var dialog = new ProductEditDialog(new ProductEditViewModel(
            ViewModel, App.Services.GetRequiredService<IBarcodeService>(), App.Services.GetRequiredService<IBarcodeRenderer>()))
        { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditProducts || (sender as Button)?.Tag is not ProductRowViewModel row)
        {
            return;
        }

        var product = await ViewModel.GetProductAsync(row.Id);
        if (product is null)
        {
            return;
        }

        var dialog = new ProductEditDialog(new ProductEditViewModel(
            ViewModel, App.Services.GetRequiredService<IBarcodeService>(), App.Services.GetRequiredService<IBarcodeRenderer>(), product))
        { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnAdjustStockClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageInventory || (sender as Button)?.Tag is not ProductRowViewModel row)
        {
            return;
        }

        var product = await ViewModel.GetProductAsync(row.Id);
        if (product is null)
        {
            return;
        }

        var dialog = new StockAdjustmentDialog(new StockAdjustmentViewModel(ViewModel, product)) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnBatchesClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageInventory || (sender as Button)?.Tag is not ProductRowViewModel row)
        {
            return;
        }

        var product = await ViewModel.GetProductAsync(row.Id);
        if (product is null)
        {
            return;
        }

        var dialog = new BatchManagementDialog(new BatchManagementViewModel(ViewModel, product)) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditProducts || (sender as Button)?.Tag is not ProductRowViewModel row)
        {
            return;
        }

        await ViewModel.SetActiveAsync(row.Id, !row.IsActive);
        await ViewModel.SearchAsync();
    }

    private async void OnLabelsClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageInventory || (sender as Button)?.Tag is not ProductRowViewModel row)
        {
            return;
        }

        var labelViewModel = new BarcodeLabelViewModel(
            ViewModel,
            App.Services.GetRequiredService<IBarcodeService>(),
            App.Services.GetRequiredService<IBarcodeRenderer>(),
            [row]);

        var dialog = new BarcodeLabelDialog(labelViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnBulkLabelsClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageInventory || ViewModel.Products.Count == 0)
        {
            return;
        }

        var labelViewModel = new BarcodeLabelViewModel(
            ViewModel,
            App.Services.GetRequiredService<IBarcodeService>(),
            App.Services.GetRequiredService<IBarcodeRenderer>(),
            ViewModel.Products);

        var dialog = new BarcodeLabelDialog(labelViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }
}
