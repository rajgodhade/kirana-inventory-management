using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Hardware;
using Kirana.Application.CloudBackup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Kirana.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ThemeService _themeService;

    /// <summary>Guards against the initial <c>SelectedIndex</c> assignment firing SelectionChanged
    /// and re-saving the theme the page just read.</summary>
    private bool _isLoadingThemeChoice;

    public SettingsViewModel ViewModel { get; }
    public DeviceStatusViewModel DeviceStatus { get; }

    public SettingsPage()
    {
        var services = App.Services;
        ViewModel = new SettingsViewModel(
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<IAppPaths>(),
            services.GetRequiredService<ManagementSession>(),
            services.GetRequiredService<ICloudBackupService>(),
            services.GetRequiredService<Kirana.Application.Taxation.IStoreTaxIdentityService>());
        DeviceStatus = new DeviceStatusViewModel(
            services.GetRequiredService<IHardwareMonitor>(),
            services.GetRequiredService<IHardwareSettingsService>());
        _themeService = services.GetRequiredService<ThemeService>();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoadingThemeChoice = true;
        ThemeChoice.SelectedIndex = _themeService.CurrentMode switch
        {
            AppThemeMode.Dark => 1,
            AppThemeMode.System => 2,
            _ => 0,
        };
        _isLoadingThemeChoice = false;

        await ViewModel.InitializeAsync();
        await DeviceStatus.RefreshAsync();
    }

    private async void OnThemeChoiceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingThemeChoice)
        {
            return;
        }

        var mode = ThemeChoice.SelectedIndex switch
        {
            1 => AppThemeMode.Dark,
            2 => AppThemeMode.System,
            _ => AppThemeMode.Light,
        };

        await _themeService.SetModeAsync(mode);
    }

    private async void OnChooseBackupFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await ViewModel.SaveBackupDirectoryAsync(folder.Path);
        }
    }
}
