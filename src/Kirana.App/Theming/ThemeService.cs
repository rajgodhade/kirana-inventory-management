using Kirana.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kirana.App.Theming;

public enum AppThemeMode
{
    Light,
    Dark,
    System,
}

/// <summary>
/// Owns the app's light/dark appearance.
///
/// The theme is applied by setting <see cref="FrameworkElement.RequestedTheme"/> on the window's
/// root element, not <see cref="Application.RequestedTheme"/> — the latter is only settable during
/// startup and cannot be changed at runtime, so it can't back a live toggle. The consequence worth
/// remembering: <c>{ThemeResource}</c> lookups inside that subtree follow the element theme, but
/// anything resolved outside it does not. That covers two cases handled explicitly here:
/// <see cref="ContentDialog"/>s and flyouts render in a separate popup root, so
/// <see cref="ApplyToPopupRoot"/> must be called for them; and resource lookups from C# against
/// <c>Application.Current.Resources</c> would resolve against the wrong theme, so screens use
/// XAML <c>{ThemeResource}</c> instead.
/// </summary>
/// <remarks>Registered as a singleton because it owns the window's root element for the life of the
/// app, so it resolves the scoped <see cref="IKiranaDbContext"/> per operation rather than holding
/// one.</remarks>
public sealed class ThemeService(IServiceScopeFactory scopeFactory)
{
    private FrameworkElement? _root;
    private Window? _window;

    public AppThemeMode CurrentMode { get; private set; } = AppThemeMode.Light;

    /// <summary>Raised after the theme changes so open screens can re-apply it to popups.</summary>
    public event Action<ElementTheme>? ThemeChanged;

    /// <summary>The concrete theme in effect, with <see cref="AppThemeMode.System"/> resolved.</summary>
    public ElementTheme EffectiveTheme => CurrentMode switch
    {
        AppThemeMode.Dark => ElementTheme.Dark,
        AppThemeMode.Light => ElementTheme.Light,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// Reads the persisted preference and applies it to <paramref name="root"/>. Called once during
    /// startup, before the window is shown, so the app never flashes the wrong theme.
    /// </summary>
    public async Task InitializeAsync(FrameworkElement root, Window window, CancellationToken cancellationToken = default)
    {
        _root = root;
        _window = window;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IKiranaDbContext>();
        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        CurrentMode = Parse(settings?.ThemeMode);
        Apply();
    }

    /// <summary>Switches theme and persists the choice. Takes effect immediately.</summary>
    public async Task SetModeAsync(AppThemeMode mode, CancellationToken cancellationToken = default)
    {
        CurrentMode = mode;
        Apply();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IKiranaDbContext>();
        var settings = await db.AppSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            settings.ThemeMode = mode.ToString();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Flips between light and dark, used by the one-click toggle in the shell header.
    /// From <see cref="AppThemeMode.System"/> it moves to whichever theme is not currently showing.</summary>
    public Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        var next = CurrentMode switch
        {
            AppThemeMode.Light => AppThemeMode.Dark,
            AppThemeMode.Dark => AppThemeMode.Light,
            _ => IsSystemDark() ? AppThemeMode.Light : AppThemeMode.Dark,
        };

        return SetModeAsync(next, cancellationToken);
    }

    /// <summary>
    /// Applies the current theme to a ContentDialog, flyout or other popup-hosted element. Popups
    /// live outside the window's element tree and therefore do not inherit <c>RequestedTheme</c> —
    /// without this a dialog opened in dark mode renders light.
    /// </summary>
    public void ApplyToPopupRoot(FrameworkElement popupContent) =>
        popupContent.RequestedTheme = EffectiveTheme;

    public bool IsEffectivelyDark => CurrentMode switch
    {
        AppThemeMode.Dark => true,
        AppThemeMode.Light => false,
        _ => IsSystemDark(),
    };

    private void Apply()
    {
        if (_root is null)
        {
            return;
        }

        _root.RequestedTheme = EffectiveTheme;
        ApplyTitleBar();
        ThemeChanged?.Invoke(EffectiveTheme);
    }

    /// <summary>
    /// Colours the native caption bar to match. The title bar is drawn by the system, outside the
    /// XAML tree, so it keeps the default light appearance unless it is set explicitly — leaving a
    /// bright strip above a dark app.
    /// </summary>
    private void ApplyTitleBar()
    {
        // Colour customisation is not available on every Windows build, and setting these
        // properties where it is unsupported throws — which, from OnLaunched, silently kills the
        // launch before the window is ever shown. Always gate on IsCustomizationSupported.
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        if (_window?.AppWindow?.TitleBar is not { } titleBar)
        {
            return;
        }

        var dark = IsEffectivelyDark;
        var background = dark ? Windows.UI.Color.FromArgb(255, 26, 31, 37) : Windows.UI.Color.FromArgb(255, 255, 255, 255);
        var foreground = dark ? Windows.UI.Color.FromArgb(255, 242, 244, 247) : Windows.UI.Color.FromArgb(255, 18, 22, 28);
        var hover = dark ? Windows.UI.Color.FromArgb(255, 46, 53, 62) : Windows.UI.Color.FromArgb(255, 229, 232, 236);

        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 120, 130, 142)
            : Windows.UI.Color.FromArgb(255, 139, 149, 161);

        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private static bool IsSystemDark()
    {
        // With ElementTheme.Default the platform decides; this only needs to be good enough to
        // pick a sensible "opposite" for the toggle.
        var uiSettings = new Windows.UI.ViewManagement.UISettings();
        var background = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        return background.R + background.G + background.B < 384;
    }

    public static AppThemeMode Parse(string? value) =>
        Enum.TryParse<AppThemeMode>(value, ignoreCase: true, out var mode) ? mode : AppThemeMode.Light;
}
