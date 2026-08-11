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

    /// <summary>
    /// Widens a dialog past WinUI's default ~548x756 cap for content-heavy dialogs (reports,
    /// multi-section forms) that would otherwise clip instead of wrapping or scrolling.
    ///
    /// <see cref="ContentDialog"/>'s built-in template binds its size to the theme resources
    /// <c>ContentDialogMaxWidth</c>/<c>ContentDialogMaxHeight</c> rather than to
    /// <see cref="FrameworkElement.MaxWidth"/>/<see cref="FrameworkElement.MaxHeight"/>, so those
    /// have to be overridden per-instance via <see cref="ContentDialog.Resources"/> — setting the
    /// properties directly has no effect. This still only raises the ceiling; content taller than
    /// <paramref name="maxHeight"/> needs its own <c>ScrollViewer</c> to avoid being clipped.
    /// </summary>
    public static TDialog Large<TDialog>(this TDialog dialog, double maxWidth = 760, double maxHeight = 820)
        where TDialog : ContentDialog
    {
        dialog.Resources["ContentDialogMaxWidth"] = maxWidth;
        dialog.Resources["ContentDialogMaxHeight"] = maxHeight;
        return dialog;
    }
}
