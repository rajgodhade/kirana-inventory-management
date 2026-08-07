using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class HardwareSettingsPage : Page
{
    public HardwareSettingsViewModel ViewModel { get; }

    public HardwareSettingsPage()
    {
        var services = App.Services;
        ViewModel = new HardwareSettingsViewModel(
            services.GetRequiredService<IPrinterService>(),
            services.GetRequiredService<IScannerService>(),
            services.GetRequiredService<IHardwareSettingsService>(),
            services.GetRequiredService<IHardwareMonitor>(),
            services.GetRequiredService<IBarcodeLookupService>(),
            services.GetRequiredService<ManagementSession>());
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.InitializeAsync();

    private void OnScannerTestKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.CanManage)
        {
            e.Handled = true;
            _ = ViewModel.TestScannerCommand.ExecuteAsync(null);
        }
    }
}
