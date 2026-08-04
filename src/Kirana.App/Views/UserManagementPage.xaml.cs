using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class UserManagementPage : Page
{
    public UserManagementViewModel ViewModel { get; }

    public UserManagementPage()
    {
        var services = App.Services;
        ViewModel = new UserManagementViewModel(
            services.GetRequiredService<IUserManagementService>(),
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

    private async void OnAddUserClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageUsers)
        {
            return;
        }

        var editViewModel = new UserEditViewModel(ViewModel, App.Services.GetRequiredService<IUserManagementService>(), existingUser: null);
        var dialog = new UserEditDialog(editViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.LoadUsersAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageUsers || (sender as Button)?.Tag is not UserRowViewModel row)
        {
            return;
        }

        var editViewModel = new UserEditViewModel(ViewModel, App.Services.GetRequiredService<IUserManagementService>(), row);
        var dialog = new UserEditDialog(editViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.LoadUsersAsync();
    }

    private async void OnResetPasswordClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageUsers || (sender as Button)?.Tag is not UserRowViewModel row)
        {
            return;
        }

        var dialog = new ResetPasswordDialog(ViewModel, row.Id) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnSetPinClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageUsers || (sender as Button)?.Tag is not UserRowViewModel row)
        {
            return;
        }

        var dialog = new SetPinDialog(ViewModel, row.Id) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageUsers || (sender as Button)?.Tag is not UserRowViewModel row)
        {
            return;
        }

        await ViewModel.UnlockAccountAsync(row.Id);
    }

    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageUsers || (sender as Button)?.Tag is not UserRowViewModel row)
        {
            return;
        }

        await ViewModel.SetActiveAsync(row.Id, !row.IsActive);
    }
}
