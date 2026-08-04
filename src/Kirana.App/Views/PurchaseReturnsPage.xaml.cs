using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Returns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PurchaseReturnsPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public PurchaseReturnsViewModel ViewModel { get; }

    public PurchaseReturnsPage()
    {
        var services = App.Services;
        ViewModel = new PurchaseReturnsViewModel(
            services.GetRequiredService<IPurchaseReturnService>(),
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

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        _searchDebounce.Stop();
        await ViewModel.SearchAsync();
    }

    private void OnNewReturnClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(NewPurchaseReturnPage));

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PurchaseReturnRowViewModel row)
        {
            Frame.Navigate(typeof(PurchaseReturnDetailsPage), row.Id);
        }
    }
}
