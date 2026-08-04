using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>True when the bound string is non-null/non-empty — used to drive an InfoBar's IsOpen from an error message.</summary>
public sealed class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
