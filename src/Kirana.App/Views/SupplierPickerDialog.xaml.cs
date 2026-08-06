using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class SupplierPickerDialog : ContentDialog
{
    // Same live-as-you-type search this app uses everywhere else — see the identical comment in
    // CustomerPickerDialog, its sibling dialog.
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public SupplierPickerViewModel ViewModel { get; }

    public bool Confirmed { get; private set; }

    public Supplier? SelectedSupplier { get; private set; }

    public SupplierPickerDialog(PurchaseEntryViewModel owner)
    {
        ViewModel = new SupplierPickerViewModel(owner);
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        };
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.SelectedSupplier is null)
        {
            // Keep the dialog open and say why, rather than closing with nothing selected and
            // leaving the caller silently unchanged.
            ViewModel.ErrorMessage = "Search for and select a supplier first.";
            args.Cancel = true;
            return;
        }

        Confirmed = true;
        SelectedSupplier = ViewModel.SelectedSupplier;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        _searchDebounce.Stop();
        await ViewModel.SearchAsync();
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            // Without this, Enter both searches AND propagates to the dialog's own DefaultButton.
            e.Handled = true;
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    /// <summary>Uses real single-selection rather than <c>ItemClick</c> so the chosen supplier
    /// stays visibly highlighted and the list is arrow-key navigable — important for the
    /// keyboard-first purchase entry flow.</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is Supplier supplier)
        {
            ViewModel.Pick(supplier);
        }
    }

    private void OnCloseIconClick(object sender, RoutedEventArgs e) => Hide();
}
