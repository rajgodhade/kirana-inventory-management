using Kirana.Application;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Expenses;
using Kirana.Application.Printing;
using Kirana.Application.Hardware;
using Kirana.App.Hardware;
using Kirana.Application.Setup;
using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.Services;
using Kirana.Infrastructure;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using System.Runtime.InteropServices;

namespace Kirana.App;

/// <summary>
/// Composition root (PRD §3-4). Wires DI, applies pending EF Core migrations, then decides
/// whether to show the first-time Setup Wizard or launch straight into POS Billing based on
/// <see cref="IFirstTimeSetupService.IsSetupCompletedAsync"/>.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>The single desktop window, exposed for interop calls (e.g. printing) that need
    /// a window handle — see <see cref="Kirana.App.Printing.BarcodeLabelPrintHelper"/>.</summary>
    public static Window MainWindow => _mainWindow ?? throw new InvalidOperationException("Window not yet created.");

    /// <summary>The window's top-level navigation frame. Pages hosted inside the management shell
    /// need this to navigate the whole window (e.g. back to Billing) rather than the shell's inner
    /// content frame, which is what their own <c>Frame</c> property refers to.</summary>
    public static Frame? RootFrame => _mainWindow?.NavigationFrame;

    private static MainWindow? _mainWindow;
    private static nint _largeWindowIcon;
    private static nint _smallWindowIcon;

    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const uint WmSetIcon = 0x0080;
    private const nuint IconSmall = 0;
    private const nuint IconBig = 1;
    private const int SmCxIcon = 11;
    private const int SmCyIcon = 12;
    private const int SmCxSmallIcon = 49;
    private const int SmCySmallIcon = 50;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public App()
    {
        // Give this unpackaged desktop process a stable Shell identity. Without an explicit AUMID,
        // Windows can group it under the generic WinUI host and keep showing that host's cached
        // taskbar icon even though the HWND and executable both contain the Kirana icon.
        var appIdResult = SetCurrentProcessExplicitAppUserModelID("Kirana.InventoryManagement.Desktop");
        if (appIdResult != 0)
            Log.Warning("Could not set the Kirana AppUserModelID. HRESULT: {HResult}", appIdResult);

        InitializeComponent();

        // Serilog must be configured before any DI service logs; resolve paths up front.
        var paths = new AppPaths();
        Infrastructure.DependencyInjection.ConfigureSerilog(paths);

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddApplication();
                services.AddInfrastructure();
                services.AddSingleton<IPrinterDiscoveryService, WindowsPrinterDiscoveryService>();
                services.AddSingleton<IPrinterService, WindowsPrinterService>();
                services.AddSingleton<IScannerService, WindowsScannerService>();
                services.AddSingleton<IDeviceDiscoveryService, WindowsDeviceDiscovery>();
                services.AddSingleton<IDeviceManager, WindowsDeviceManager>();
                services.AddSingleton<IHardwareMonitor, HardwareMonitor>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<InvoiceRefreshNotifier>();
            })
            .UseSerilog()
            .Build();

        Services = host.Services;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Information("Startup: applying database migrations.");
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KiranaDbContext>();
            await db.Database.MigrateAsync();
        }
        Log.Information("Startup: database migrations complete.");

        bool setupCompleted;
        using (var scope = Services.CreateScope())
        {
            var setupService = scope.ServiceProvider.GetRequiredService<IFirstTimeSetupService>();
            setupCompleted = await setupService.IsSetupCompletedAsync();
        }

        if (setupCompleted)
        {
            using var scope = Services.CreateScope();

            // Idempotent — safe on every launch. Ensures a store that was set up on an older
            // version of the app receives any permissions introduced since (PRD §9).
            var permissionSeeding = scope.ServiceProvider.GetRequiredService<IPermissionSeedingService>();
            await permissionSeeding.SyncPermissionsAsync();

            // Same idempotent-upgrade idea as permissions: a store set up before Phase 9 picks up
            // the default expense headings here rather than only on a fresh install.
            var expenseCategories = scope.ServiceProvider.GetRequiredService<IExpenseCategoryService>();
            await expenseCategories.SeedDefaultsAsync();

            var db = scope.ServiceProvider.GetRequiredService<KiranaDbContext>();
            var appSettings = await db.AppSettings.FirstOrDefaultAsync();
            if (appSettings is not null)
            {
                var session = Services.GetRequiredService<ManagementSession>();
                session.AutoLockMinutes = appSettings.AutoLockMinutes;
                session.RequirePinForPriceOverride = appSettings.RequirePinForPriceOverride;
                session.RequirePinForLargeDiscount = appSettings.RequirePinForLargeDiscount;
                session.RequirePinForReprint = appSettings.RequirePinForReprint;
            }
        }

        Log.Information("Startup: creating MainWindow.");
        _mainWindow = new MainWindow();
        Log.Information("Startup: MainWindow created; activating.");

        // Activate first so WinUI has created the native HWND/AppWindow before ThemeService touches
        // AppWindow.TitleBar. Accessing the title bar on an unactivated unpackaged WinUI window can
        // leave CoreMessaging spinning with no visible window on some Windows/runtime builds.
        _mainWindow.Activate();
        Log.Information("Startup: MainWindow activated.");

        // Applied before the window is shown so the app never flashes the wrong theme on launch.
        // Appearance is never worth failing a launch over: OnLaunched is async void, so an escaping
        // exception here would kill the process before the window is ever shown, leaving a running
        // app with no UI. Fall back to the default theme instead.
        try
        {
            Log.Information("Startup: applying theme.");
            await Services.GetRequiredService<ThemeService>().InitializeAsync(_mainWindow.RootElement, _mainWindow);
            Log.Information("Startup: theme applied.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply the saved theme; continuing with the default appearance.");
        }

        Log.Information("Startup: navigating to initial page.");
        _mainWindow.NavigateToInitialPage(setupCompleted);
        Log.Information("Startup: initial page navigation complete.");
        ApplyMinimumWindowSize(_mainWindow);
        ApplyApplicationIcon(_mainWindow);

        try
        {
            await Services.GetRequiredService<IHardwareMonitor>().StartAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Hardware monitoring could not be started; billing remains available.");
        }

        if (setupCompleted)
        {
            await RunScheduledBackupIfDueAsync();
        }
    }

    /// <summary>
    /// Floors the window at a size the densest admin screens (the 8-tab Reports Hub, its 4-column
    /// KPI grid) still fit without clipping — nothing in this app's layout scrolls or reflows
    /// horizontally, so below this size content silently runs off the right edge with no way to
    /// reach it. Wrapped in try/catch like the theme init above: window chrome is never worth
    /// failing launch over.
    /// </summary>
    private static void ApplyMinimumWindowSize(Window window)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 1180;
                presenter.PreferredMinimumHeight = 700;
            }
        }
        catch
        {
            // Best-effort only — an unset minimum just means very small windows can clip content.
        }
    }

    /// <summary>
    /// Sets the window's own icon (title bar + taskbar button) at runtime. The <c>ApplicationIcon</c>
    /// MSBuild property in the csproj embeds the same .ico as the .exe's Win32 resource, which is
    /// what Explorer/shortcuts/Start Menu show before the app is even running — but for an
    /// unpackaged WinUI 3 app (no Package.appxmanifest here, see <c>WindowsPackageType=None</c>),
    /// the running window's own taskbar/title-bar icon has to be set explicitly via
    /// <see cref="Microsoft.UI.Windowing.AppWindow.SetIcon"/> too, or it falls back to a generic
    /// icon while the app is open. Reads the .ico from the build output next to the .exe (marked
    /// CopyToOutputDirectory in the csproj) rather than a packaged URI, since this deployment model
    /// has no package to resolve ms-appx:// against.
    /// </summary>
    private static void ApplyApplicationIcon(Window window)
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon", "AppIcon.ico");
            if (!System.IO.File.Exists(iconPath))
            {
                return;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(iconPath);

            // AppWindow.SetIcon is not consistently reflected by the taskbar for unpackaged
            // WinUI 3 apps. WM_SETICON updates the native HWND used by the taskbar and Alt+Tab.
            _largeWindowIcon = LoadImageW(0, iconPath, ImageIcon,
                GetSystemMetrics(SmCxIcon), GetSystemMetrics(SmCyIcon), LoadFromFile);
            _smallWindowIcon = LoadImageW(0, iconPath, ImageIcon,
                GetSystemMetrics(SmCxSmallIcon), GetSystemMetrics(SmCySmallIcon), LoadFromFile);

            if (_largeWindowIcon != 0)
                SendMessageW(hwnd, WmSetIcon, IconBig, _largeWindowIcon);
            if (_smallWindowIcon != 0)
                SendMessageW(hwnd, WmSetIcon, IconSmall, _smallWindowIcon);
        }
        catch (Exception ex)
        {
            // Best-effort only — the .exe's embedded Win32 resource icon (ApplicationIcon in the
            // csproj) still covers Explorer/shortcuts/Start Menu even if this runtime call fails.
            Log.Warning(ex, "Could not apply the Kirana application icon to the running window.");
        }
    }

    /// <summary>
    /// Runs the automatic backup if one is due. Deliberately after the window is shown and wrapped
    /// in a catch-all: a store must be able to start billing whether or not a backup can be written
    /// (a full disk, a backup folder on a disconnected drive), so this can never block or fail
    /// launch. <see cref="MainWindow"/> polls this again periodically for shops that never restart.
    /// </summary>
    internal static async Task RunScheduledBackupIfDueAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IAutomaticBackupScheduler>();
            var outcome = await scheduler.RunIfDueAsync();

            if (outcome.WasDue && outcome.Result is { } result)
            {
                if (result.Succeeded)
                {
                    Log.Information("Automatic backup written to {Path}.", result.FilePath);
                }
                else
                {
                    Log.Warning("Automatic backup failed: {Error}", result.ErrorMessage);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Automatic backup check failed.");
        }
    }
}
