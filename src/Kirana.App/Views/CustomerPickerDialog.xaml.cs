using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class CustomerPickerDialog : ContentDialog
{
    // Same live-as-you-type search this app uses everywhere else — fires ~300ms after the user
    // stops typing so the list doesn't re-query on every keystroke, and Enter/the Search button
    // still work immediately for anyone who prefers them. This dialog previously had none, so
    // nothing appeared until you explicitly searched.
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public CustomerPickerViewModel ViewModel { get; }

    public bool Confirmed { get; private set; }

    public Customer? SelectedCustomer { get; private set; }

    public CustomerPickerDialog(PosShellViewModel owner)
    {
        ViewModel = new CustomerPickerViewModel(owner);
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        };
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.SelectedCustomer is null)
        {
            // Keep the dialog open and say why, rather than silently closing as if "Walk-in
            // Customer" (the separate button right next to this one) had been clicked instead.
            ViewModel.ErrorMessage = "Search for and select a customer first, or choose \"Walk-in Customer\".";
            args.Cancel = true;
            return;
        }

        Confirmed = true;
        SelectedCustomer = ViewModel.SelectedCustomer;
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.PickWalkIn();
        Confirmed = true;
        SelectedCustomer = null;
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
            // Without this, Enter both searches AND propagates to the dialog's own DefaultButton
            // (Select) — which, before the null-guard above existed, could silently confirm the
            // sale as Walk-in Customer the moment you pressed Enter to search.
            e.Handled = true;
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    /// <summary>Uses real single-selection rather than <c>ItemClick</c> so the chosen customer
    /// stays visibly highlighted — matches <see cref="SupplierPickerDialog"/>'s equivalent list.</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is Customer customer)
        {
            ViewModel.Pick(customer);
        }
    }

    private void OnCloseIconClick(object sender, RoutedEventArgs e) => Hide();
}
