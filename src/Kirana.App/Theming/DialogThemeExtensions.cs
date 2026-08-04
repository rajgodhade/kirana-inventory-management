using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Theming;

public static class DialogThemeExtensions
{
    /// <summary>
    /// Attaches a dialog to the given XamlRoot <em>and</em> applies the current app theme.
    ///
    /// Both are required. A <see cref="ContentDialog"/> is hosted in a popup root outside the
    /// window's element tree, so it does not inherit the <c>RequestedTheme</c> the theme service
    /// sets on the window root — without this, every dialog renders light while the app is dark.
    /// Use this in place of setting <c>XamlRoot</c> by hand.
    /// </summary>
    public static TDialog Themed<TDialog>(this TDialog dialog, XamlRoot xamlRoot)
        where TDialog : ContentDialog
    {
        dialog.XamlRoot = xamlRoot;
        dialog.RequestedTheme = App.Services.GetRequiredService<ThemeService>().EffectiveTheme;
        return dialog;
    }
}
