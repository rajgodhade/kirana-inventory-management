using Kirana.App.Theming;
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
    // Same live-as-you-type search this app uses everywhere else — fires ~300ms after the user
    // stops typing so the list doesn't re-query on every keystroke, and Enter still works
    // immediately for anyone who prefers it. This page had been left on Enter-only search, which
    // reads as "typing doesn't do anything" since nothing happens until the key is pressed.
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

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

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        };
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    private async void OnSortOptionChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnShowInactiveChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    // Reads IsChecked directly rather than trusting the x:Bind TwoWay sync has already run before
    // this handler fires — CheckBox.Checked/Unchecked and the compiled x:Bind update subscribe to
    // the same event, and relying on subscription order to read the bound property here would
    // intermittently search with the previous (stale) value, as documented elsewhere in this app
    // (PurchasesPage's "Outstanding only" filter).
    private async void OnOutOfStockOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            ViewModel.OutOfStockOnly = checkBox.IsChecked == true;
        }

        await ViewModel.SearchAsync();
    }

    private async void OnExpiredOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            ViewModel.ExpiredOnly = checkBox.IsChecked == true;
        }

        await ViewModel.SearchAsync();
    }

    private async void OnManageCategoriesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CategoryManagementDialog(ViewModel.CurrentUserId).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.ReloadFilterOptionsAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnManageBrandsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new BrandManagementDialog(ViewModel.CurrentUserId).Themed(XamlRoot);
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
            ViewModel, App.Services.GetRequiredService<IBarcodeService>(), App.Services.GetRequiredService<IBarcodeRenderer>())).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnImportProductsClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditProducts)
        {
            return;
        }

        var dialog = new ProductImportDialog(new ProductImportViewModel(
            App.Services.GetRequiredService<IProductImportService>(), ViewModel.CurrentUserId)).Themed(XamlRoot);
        await dialog.ShowAsync();

        if (dialog.ImportedAnything)
        {
            // An import can add categories/brands too, so refresh the filter dropdowns as well as
            // the product list.
            await ViewModel.ReloadFilterOptionsAsync();
            await ViewModel.SearchAsync();
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProductRowViewModel row)
        {
            await EditProductAsync(row);
        }
    }

    private async void OnProductDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ProductRowViewModel row)
        {
            await EditProductAsync(row);
        }
    }

    private async Task EditProductAsync(ProductRowViewModel row)
    {
        if (!ViewModel.CanEditProducts)
        {
            return;
        }

        var product = await ViewModel.GetProductAsync(row.Id);
        if (product is null)
        {
            return;
        }

        var dialog = new ProductEditDialog(new ProductEditViewModel(
            ViewModel, App.Services.GetRequiredService<IBarcodeService>(), App.Services.GetRequiredService<IBarcodeRenderer>(), product)).Themed(XamlRoot);
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

        var dialog = new StockAdjustmentDialog(new StockAdjustmentViewModel(ViewModel, product)).Themed(XamlRoot);
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

        var dialog = new BatchManagementDialog(new BatchManagementViewModel(ViewModel, product)).Themed(XamlRoot);
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

        var dialog = new BarcodeLabelDialog(labelViewModel).Themed(XamlRoot);
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

        var dialog = new BarcodeLabelDialog(labelViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }
}
