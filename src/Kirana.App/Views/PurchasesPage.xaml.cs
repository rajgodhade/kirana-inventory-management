using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Views;

public sealed partial class PurchasesPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public PurchasesViewModel ViewModel { get; }

    public PurchasesPage()
    {
        var services = App.Services;
        ViewModel = new PurchasesViewModel(
            services.GetRequiredService<IPurchaseService>(),
            services.GetRequiredService<ISupplierService>(),
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

    private async void OnFilterChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    // Reads IsChecked directly rather than trusting the x:Bind TwoWay sync has already run before
    // this handler fires — CheckBox.Checked/Unchecked and the compiled x:Bind update subscribe to
    // the same event, and relying on subscription order to read ViewModel.OutstandingOnly here
    // intermittently searched with the previous (stale) value.
    private async void OnOutstandingOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            ViewModel.OutstandingOnly = checkBox.IsChecked == true;
        }

        await ViewModel.SearchAsync();
    }

    private async void OnFilterSelectionChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnDateFilterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        await ViewModel.SearchAsync();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    private void OnNewPurchaseClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases)
        {
            return;
        }

        Frame.Navigate(typeof(PurchaseEntryPage));
    }

    // ================================ ROW HOVER / DOUBLE-CLICK ================================
    // A zebra-striped row already paints its own opaque background, which would hide
    // ListViewItem's own built-in hover visual (it sits behind the DataTemplate content) — so the
    // hover tint is applied directly to this Grid instead, restoring the affordance explicitly.

    private static readonly SolidColorBrush TransparentBrush = new(Microsoft.UI.Colors.Transparent);

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid grid)
        {
            return;
        }

        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("BrandSubtleBrush", out var brush))
        {
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)brush;
        }
    }

    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = TransparentBrush;
        }
    }

    private async void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PurchaseRowViewModel row)
        {
            await ShowDetailsAsync(row);
        }
    }

    // ===================================== ROW ACTIONS =====================================

    private async void OnViewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PurchaseRowViewModel row)
        {
            await ShowDetailsAsync(row);
        }
    }

    private async Task ShowDetailsAsync(PurchaseRowViewModel row)
    {
        var purchase = await ViewModel.GetPurchaseAsync(row.Id);
        if (purchase is null)
        {
            return;
        }

        var dialog = new PurchaseDetailsDialog(purchase).Themed(XamlRoot);
        await dialog.ShowAsync();
    }

    private async void OnRecordPaymentClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as FrameworkElement)?.Tag is not PurchaseRowViewModel row)
        {
            return;
        }

        var purchase = await ViewModel.GetPurchaseAsync(row.Id);
        if (purchase is null)
        {
            return;
        }

        var dialog = new PurchasePaymentDialog(ViewModel, purchase.Id, purchase.SupplierId).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PurchaseRowViewModel row)
        {
            return;
        }

        var purchase = await ViewModel.GetPurchaseAsync(row.Id);
        if (purchase is null)
        {
            return;
        }

        using var helper = new PurchasePrintHelper(App.MainWindow, purchase);
        try
        {
            await helper.ShowPrintUIAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Couldn't print",
                Content = ex.Message,
                CloseButtonText = "OK",
            };
            dialog.Themed(XamlRoot);
            await dialog.ShowAsync();
        }
    }

    private void OnCreateReturnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as FrameworkElement)?.Tag is not PurchaseRowViewModel)
        {
            return;
        }

        Frame.Navigate(typeof(NewPurchaseReturnPage));
    }
}
