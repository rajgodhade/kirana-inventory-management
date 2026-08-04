using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        !(value is bool b && b);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        !(value is bool b && b);
}
