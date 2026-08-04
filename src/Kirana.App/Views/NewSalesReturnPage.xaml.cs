using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Printing;
using Kirana.Application.Returns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class NewSalesReturnPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public NewSalesReturnViewModel ViewModel { get; }

    public NewSalesReturnPage()
    {
        var services = App.Services;
        ViewModel = new NewSalesReturnViewModel(
            services.GetRequiredService<ISalesReturnService>(),
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
        if ((sender as ListView)?.SelectedItem is ReturnableSale sale)
        {
            await ViewModel.SelectSaleAsync(sale.SaleId);
            Bindings.Update();
        }
    }

    private void OnClearSaleClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedSale = null;
        ViewModel.Lines.Clear();
        Bindings.Update();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnProcessClick(object sender, RoutedEventArgs e)
    {
        if (!await ViewModel.ProcessAsync())
        {
            Bindings.Update();
            return;
        }

        var completed = ViewModel.CompletedReturn!;
        Bindings.Update();

        // Ask about the receipt only after the return has committed — printing is never allowed to
        // affect whether the return itself succeeded.
        var confirm = new ContentDialog
        {
            Title = "Return recorded",
            Content = $"{completed.ReturnNumber} saved. Print the return receipt?",
            PrimaryButtonText = "Print receipt",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
        }.Themed(XamlRoot);

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await PrintReturnReceiptAsync(completed.Id);
        }

        Frame.Navigate(typeof(SalesReturnsPage));
    }

    /// <summary>Printing is fully separable from the return: a failure here is reported and never
    /// undoes the committed stock and refund.</summary>
    private async Task PrintReturnReceiptAsync(int salesReturnId)
    {
        var receiptService = App.Services.GetRequiredService<IReturnReceiptService>();
        try
        {
            var document = await receiptService.GetReturnReceiptAsync(salesReturnId, ViewModel.CurrentUserId);

            using var helper = new ReturnReceiptPrintHelper(App.MainWindow, document, InvoiceFormat.Thermal80mm);
            await helper.ShowPrintUIAsync();

            await receiptService.LogReturnPrintAsync(salesReturnId, ViewModel.CurrentUserId);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Could not print the receipt: {ex.Message}. The return is saved and can be reprinted.";
            Bindings.Update();
        }
    }
}
