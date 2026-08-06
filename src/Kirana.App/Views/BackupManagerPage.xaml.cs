using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class BackupManagerPage : Page
{
    public BackupManagerViewModel ViewModel { get; }

    public BackupManagerPage()
    {
        var services = App.Services;
        ViewModel = new BackupManagerViewModel(
            services.GetRequiredService<IBackupService>(),
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<IAppPaths>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.RefreshCommand.ExecuteAsync(null);

    private async void OnValidateClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BackupHistoryItemViewModel item)
        {
            await ViewModel.ValidateCommand.ExecuteAsync(item);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BackupHistoryItemViewModel item)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Delete backup",
            Content = $"Delete {item.FileName}? The backup file will be removed from disk and cannot be recovered.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        }.Themed(XamlRoot);

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCommand.ExecuteAsync(item);
        }
    }
}
