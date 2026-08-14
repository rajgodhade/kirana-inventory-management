
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

public sealed partial class PurchaseOrderEntryPage : Page
{
    public PurchaseOrderEntryViewModel ViewModel { get; }
    private int? _id;
    private PurchaseOrderPrefill? _prefill;
    public PurchaseOrderEntryPage()
    {
        var s = App.Services;
        ViewModel = new(s.GetRequiredService<IProductService>(), s.GetRequiredService<ISupplierService>(), s.GetRequiredService<IPurchaseOrderService>(), s.GetRequiredService<IPurchaseGstCalculationService>(), s.GetRequiredService<ManagementSession>());
        InitializeComponent();
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPagePointerPressed), handledEventsToo: true);
        Loaded += OnLoaded;
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); _id = e.Parameter as int?; _prefill = e.Parameter as PurchaseOrderPrefill; }
    private async void OnLoaded(object sender, RoutedEventArgs e) { try { await ViewModel.InitializeAsync(_id, _prefill); PageTitle.Text = _id is null ? "New Purchase Order" : "Edit Purchase Order"; } catch (Exception ex) { ViewModel.ErrorMessage = ex.Message; } }
    private async void OnAddProductClick(object sender, RoutedEventArgs e) { await ViewModel.AddProductAsync(); ProductSearch.Focus(FocusState.Programmatic); }
    private async void OnProductSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        ViewModel.ClearSelectedProductForSearch();
        await ViewModel.UpdateProductSuggestionsAsync(box.Text);
        // Typing must never close the picker mid multi-selection (§13).
        ViewModel.OpenProductPicker();
    }
    private async void OnProductSearchGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (box.FocusState == FocusState.Programmatic) return;
        ViewModel.CloseSupplierSuggestions();
        await ViewModel.UpdateProductSuggestionsAsync(box.Text);
        ViewModel.OpenProductPicker();
    }
    private async void OnProductSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CloseProductPicker();
                break;
            case Windows.System.VirtualKey.Down:
                // Hand keyboard control to the result list without disturbing text entry.
                e.Handled = true;
                if (!ViewModel.IsProductPickerOpen)
                {
                    await ViewModel.UpdateProductSuggestionsAsync(ProductSearch.Text);
                    ViewModel.OpenProductPicker();
                }
                FocusFirstProductItem();
                break;
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                if (ViewModel.HasProductSelection) await AddSelectedProductsAsync();
                else await ViewModel.AddProductAsync();
                break;
        }
    }
    private void FocusFirstProductItem() => DispatcherQueue.TryEnqueue(() =>
    {
        if (ProductPickerList.Items.Count == 0) return;
        (ProductPickerList.ContainerFromIndex(0) as ListViewItem)?.Focus(FocusState.Keyboard);
    });
    private void OnProductItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProductPickerItemViewModel item) ViewModel.ToggleProductSelection(item);
    }
    private async void OnProductPickerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CloseProductPicker();
                ProductSearch.Focus(FocusState.Programmatic);
                break;
            case Windows.System.VirtualKey.Space:
            case Windows.System.VirtualKey.Enter:
                if ((sender as ListView)?.ContainerFromItem(GetFocusedPickerItem()) is not null
                    && GetFocusedPickerItem() is { } focused)
                {
                    e.Handled = true;
                    ViewModel.ToggleProductSelection(focused);
                }
                break;
            case Windows.System.VirtualKey.A when IsControlDown():
                e.Handled = true;
                ViewModel.SelectAllVisibleProducts();
                break;
        }
        await Task.CompletedTask;
    }
    private ProductPickerItemViewModel? GetFocusedPickerItem() =>
        XamlRoot is null ? null
            : (FocusManager.GetFocusedElement(XamlRoot) as FrameworkElement)?.DataContext as ProductPickerItemViewModel;
    private static bool IsControlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    private void OnSelectAllProductsClick(object sender, RoutedEventArgs e) => ViewModel.SelectAllVisibleProducts();
    private void OnClearProductSelectionClick(object sender, RoutedEventArgs e) => ViewModel.ClearProductSelection();
    private async void OnAddSelectedProductsClick(object sender, RoutedEventArgs e) => await AddSelectedProductsAsync();
    private async Task AddSelectedProductsAsync()
    {
        await ViewModel.AddSelectedProductsAsync();
        ProductSearch.Text = string.Empty;
        FocusSink.Focus(FocusState.Programmatic);
    }
    private void OnPagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var suggestion = FindSuggestion(source);
        var isSupplierSuggestion = suggestion is not null
            && ViewModel.SupplierSuggestions.Any(item => ReferenceEquals(item, suggestion));

        // Clicking inside the picker (including its footer buttons) must not dismiss it (§20).
        if (!IsInside(source, ProductSearch) && !IsInside(source, ProductPickerRoot))
        {
            ViewModel.CloseProductPicker();
            ClearSearchFocusAfterPointer(ProductSearch);
        }
        if (!IsInside(source, SupplierSearch) && !isSupplierSuggestion)
        {
            ViewModel.CloseSupplierSuggestions();
            ClearSearchFocusAfterPointer(SupplierSearch);
        }
    }
    private void ClearSearchFocusAfterPointer(Control searchBox)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is null) return;
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!IsInside(focusedElement, searchBox)) return;

            FocusSink.Focus(FocusState.Pointer);
        });
    }
    private static SearchSuggestionItem? FindSuggestion(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: SearchSuggestionItem suggestion }) return suggestion;
        }
        return null;
    }
    private static bool IsInside(DependencyObject? source, DependencyObject target)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, target)) return true;
        }
        return false;
    }
    private void OnSupplierSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        ViewModel.SupplierSearchText = sender.Text;
        if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ViewModel.ClearSelectedSupplierForSearch(sender.Text);
    }
    private void OnSupplierSearchGotFocus(object sender, RoutedEventArgs e) => ViewModel.FocusSupplierSearch();
    private void OnSupplierSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Tab)
            ViewModel.CloseSupplierSuggestions();
    }
    private void OnSupplierSearchLostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is null) return;
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            var suggestion = FindSuggestion(focusedElement);
            var isSupplierSuggestion = suggestion is not null
                && ViewModel.SupplierSuggestions.Any(item => ReferenceEquals(item, suggestion));
            if (!isSupplierSuggestion) ViewModel.CloseSupplierSuggestions();
        });
    }
    private void OnSupplierSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs e)
    {
        if (e.SelectedItem is not SearchSuggestionItem item) return;
        ViewModel.SelectSupplierSuggestion(item);
        sender.Text = item.Title;
        // AutoSuggestBox re-opens its own list when the bound ItemsSource changes while it still has
        // focus, so the close has to be re-asserted after the control finishes handling the choice.
        DispatcherQueue.TryEnqueue(() => ViewModel.CloseSupplierSuggestions());
    }
    private void OnLineChanged(object sender, TextChangedEventArgs e) { if ((sender as TextBox)?.Tag is PurchaseOrderLineViewModel) ViewModel.Recalculate(); }
    private void OnRemoveClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PurchaseOrderLineViewModel line) ViewModel.Remove(line); }
    private void OnCancelClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PurchaseOrdersPage));
    private async void OnSaveDraftClick(object sender, RoutedEventArgs e) { if (await ViewModel.SaveAsync(false)) Frame.Navigate(typeof(PurchaseOrdersPage)); }
    private async void OnSubmitClick(object sender, RoutedEventArgs e) { if (await ViewModel.SaveAsync(true)) Frame.Navigate(typeof(PurchaseOrdersPage)); }
}
