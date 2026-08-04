using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Returns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class NewPurchaseReturnPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public NewPurchaseReturnViewModel ViewModel { get; }

    public NewPurchaseReturnPage()
    {
        var services = App.Services;
        ViewModel = new NewPurchaseReturnViewModel(
            services.GetRequiredService<IPurchaseReturnService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus(FocusState.Programmatic);

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
            Bindings.Update();
        };
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        if (!string.IsNullOrWhiteSpace(ViewModel.SearchText))
        {
            _searchDebounce.Start();
        }
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
            Bindings.Update();
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        _searchDebounce.Stop();
        await ViewModel.SearchAsync();
        Bindings.Update();
    }

    private async void OnCandidateSelected(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is ReturnablePurchase purchase)
        {
            await ViewModel.SelectPurchaseAsync(purchase.PurchaseId);
            Bindings.Update();
        }
    }

    private void OnClearPurchaseClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedPurchase = null;
        ViewModel.Lines.Clear();
        Bindings.Update();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnProcessClick(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.ProcessAsync())
        {
            Frame.Navigate(typeof(PurchaseReturnsPage));
            return;
        }

        Bindings.Update();
    }
}
