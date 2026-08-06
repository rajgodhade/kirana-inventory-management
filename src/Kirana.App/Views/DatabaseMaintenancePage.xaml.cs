using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Kirana.App.Views;

public sealed partial class DatabaseMaintenancePage : Page
{
    public DatabaseMaintenanceViewModel ViewModel { get; }

    public DatabaseMaintenancePage()
    {
        var services = App.Services;
        ViewModel = new DatabaseMaintenanceViewModel(
            services.GetRequiredService<IDatabaseMaintenanceService>(),
            services.GetRequiredService<IBackupService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnVacuumClick(object sender, RoutedEventArgs e)
    {
        // A vacuum rewrites the whole file. It is safe, but on a large database it is not instant
        // and it holds a lock while it runs, so it should never start from a stray click.
        var confirm = new ContentDialog
        {
            Title = "Vacuum the database?",
            Content = "This rebuilds the database file to reclaim unused space. Your records are not changed, "
                + "but the database is locked while it runs — do this when nobody is billing.",
            PrimaryButtonText = "Vacuum",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        }.Themed(XamlRoot);

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.VacuumCommand.ExecuteAsync(null);
        }
    }

    private async void OnVerifyBackupClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".kbak");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.VerifyBackupAsync(file.Path);
        }
    }
}
