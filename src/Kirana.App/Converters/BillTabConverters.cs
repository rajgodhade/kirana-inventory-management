using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>Bold title on the active billing tab, normal weight elsewhere. The active tab's pastel
/// fill and underline are plain <c>ThemeResource</c>-bound Borders in the template rather than
/// converters, so they re-resolve automatically when the app switches between light and dark.</summary>
public sealed class BillTabFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
