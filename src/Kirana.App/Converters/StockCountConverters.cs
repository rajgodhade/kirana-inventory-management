using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Converters;

/// <summary>
/// Shared lookup for the semantic brushes these converters return. Resolves from the merged
/// application resources rather than hard-coding colours, so stock-count states follow the light/
/// dark theme like the rest of the app (§29) instead of staying fixed in one palette.
/// </summary>
internal static class SemanticBrush
{
    public static Brush Resolve(string themeResourceKey, Brush fallback)
    {
        // Application.Current must be fully qualified here: Kirana.Application is also in scope
        // across this project, and the bare name binds to the namespace instead.
        var resources = Microsoft.UI.Xaml.Application.Current?.Resources;
        return resources is not null && resources.TryGetValue(themeResourceKey, out var value) && value is Brush brush
            ? brush
            : fallback;
    }

    public static Brush Default => Resolve("TextFillColorPrimaryBrush", new SolidColorBrush(Microsoft.UI.Colors.Gray));
}

/// <summary>
/// Colours a stock count's status. Deliberately paired with the status TEXT in the UI rather than
/// used alone — colour is reinforcement, never the only signal (§29).
/// </summary>
public sealed class StockCountStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as string) switch
        {
            "In Progress" => SemanticBrush.Resolve("WarningBrush", SemanticBrush.Default),
            "Completed" => SemanticBrush.Resolve("SuccessBrush", SemanticBrush.Default),
            "Cancelled" => SemanticBrush.Resolve("TextFillColorSecondaryBrush", SemanticBrush.Default),
            _ => SemanticBrush.Default,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Colours a variance: surplus positive, shortage negative, exact match neutral. The bound text
/// always carries an explicit +/- sign, so the meaning survives without colour.
/// </summary>
public sealed class StockCountVarianceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as string) switch
        {
            "Surplus" => SemanticBrush.Resolve("SuccessBrush", SemanticBrush.Default),
            "Shortage" => SemanticBrush.Resolve("DangerBrush", SemanticBrush.Resolve("ErrorBrush", SemanticBrush.Default)),
            _ => SemanticBrush.Default,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when a count is zero — for "nothing here yet" empty states.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
