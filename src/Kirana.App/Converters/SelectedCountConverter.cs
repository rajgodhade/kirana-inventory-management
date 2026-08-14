using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>Renders a selection count as the picker footer's status text ("2 selected").</summary>
public sealed class SelectedCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int count && count > 0 ? $"{count} selected" : "None selected";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
