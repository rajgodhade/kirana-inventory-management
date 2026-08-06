using Kirana.Application;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Expenses;
using Kirana.Application.Printing;
using Kirana.Application.Setup;
using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.Infrastructure;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

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

    public App()
    {
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
                services.AddSingleton<ThemeService>();
            })
            .UseSerilog()
            .Build();

        Services = host.Services;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KiranaDbContext>();
            await db.Database.MigrateAsync();
        }

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

        _mainWindow = new MainWindow();

        // Applied before the window is shown so the app never flashes the wrong theme on launch.
        // Appearance is never worth failing a launch over: OnLaunched is async void, so an escaping
        // exception here would kill the process before the window is ever shown, leaving a running
        // app with no UI. Fall back to the default theme instead.
        try
        {
            await Services.GetRequiredService<ThemeService>().InitializeAsync(_mainWindow.RootElement, _mainWindow);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply the saved theme; continuing with the default appearance.");
        }

        _mainWindow.NavigateToInitialPage(setupCompleted);
        _mainWindow.Activate();
        ApplyMinimumWindowSize(_mainWindow);

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
