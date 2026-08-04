using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class CustomerPickerDialog : ContentDialog
{
    public CustomerPickerViewModel ViewModel { get; }

    public bool Confirmed { get; private set; }

    public Customer? SelectedCustomer { get; private set; }

    public CustomerPickerDialog(PosShellViewModel owner)
    {
        ViewModel = new CustomerPickerViewModel(owner);
        InitializeComponent();
        PrimaryButtonClick += OnPrimaryButtonClick;
        SecondaryButtonClick += OnSecondaryButtonClick;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Confirmed = true;
        SelectedCustomer = ViewModel.SelectedCustomer;
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.PickWalkIn();
        Confirmed = true;
        SelectedCustomer = null;
    }

    private async void OnSearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await ViewModel.SearchAsync();
        }
    }

    private void OnResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Customer customer)
        {
            ViewModel.Pick(customer);
        }
    }

    private void OnCloseIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Hide();
}
