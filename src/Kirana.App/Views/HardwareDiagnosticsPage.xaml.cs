using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Kirana.App.Views;

public sealed partial class HardwareDiagnosticsPage : Page
{
    public HardwareDiagnosticsViewModel ViewModel { get; }
    public HardwareDiagnosticsPage()
    {
        var services = App.Services;
        ViewModel = new HardwareDiagnosticsViewModel(
            services.GetRequiredService<IHardwareMonitor>(),
            services.GetRequiredService<IHardwareSettingsService>(),
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<IBackupService>(),
            services.GetRequiredService<ManagementSession>());
        InitializeComponent();
        Loaded += async (_, _) => { if (ViewModel.CanManage) await ViewModel.RunCommand.ExecuteAsync(null); };
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"kirana-hardware-{DateTime.Now:yyyyMMdd-HHmm}" };
        picker.FileTypeChoices.Add("Text report", [".txt"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is not null) await FileIO.WriteTextAsync(file, ViewModel.BuildReport());
    }
}
