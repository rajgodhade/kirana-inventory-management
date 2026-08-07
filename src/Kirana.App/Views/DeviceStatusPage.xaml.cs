using Kirana.App.ViewModels;
using Kirana.Application.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class DeviceStatusPage : Page
{
    public DeviceStatusViewModel ViewModel { get; }
    public DeviceStatusPage()
    {
        ViewModel = new DeviceStatusViewModel(
            App.Services.GetRequiredService<IHardwareMonitor>(),
            App.Services.GetRequiredService<IHardwareSettingsService>());
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
}
