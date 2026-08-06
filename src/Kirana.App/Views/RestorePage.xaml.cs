using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Restore;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Kirana.App.Views;

/// <summary>
/// Drives the restore workflow. The dialog steps here are shown strictly one after another —
/// WinUI 3 permits only one open <c>ContentDialog</c> per XamlRoot, and opening a second from
/// inside the first's handler kills the process silently.
/// </summary>
public sealed partial class RestorePage : Page
{
    public RestoreViewModel ViewModel { get; }

    public RestorePage()
    {
        var services = App.Services;
        ViewModel = new RestoreViewModel(
            services.GetRequiredService<IRestoreService>(),
            services.GetRequiredService<IBackupService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnSelectBackupClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BackupHistoryItemViewModel item)
        {
            await ViewModel.InspectAsync(item.FilePath);
        }
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".kbak");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.InspectAsync(file.Path);
        }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        // Step-up authorization even though the page itself is already Owner-gated: this is the one
        // action in Kirana that discards live data wholesale, and the same pattern already guards
        // invoice reprints.
        var authDialog = new ManagerAuthorizationDialog(
            App.Services.GetRequiredService<IAuthenticationService>(), PermissionKeys.BackupRestore).Themed(XamlRoot);

        if (await authDialog.ShowAsync() != ContentDialogResult.Primary
            || authDialog.AuthorizedUserId is not { } authorizedUserId)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Replace all current data?",
            Content = "Everything currently in Kirana will be replaced by this backup's contents.\n\n"
                + "A safety backup of your current data is taken first, so this can be undone by restoring that safety backup.\n\n"
                + "Kirana will restart once the restore finishes.",
            PrimaryButtonText = "Restore and replace",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        }.Themed(XamlRoot);

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.RestoreAsync(authorizedUserId);
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => AppRestartHelper.Restart();
}
