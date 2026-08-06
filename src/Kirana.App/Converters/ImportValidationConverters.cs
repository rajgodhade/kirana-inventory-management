using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Converters;

/// <summary>Maps preview rows to an explicitly light table palette, independent of the app theme.</summary>
public sealed class ImportRowBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush((value as string) switch
        {
            "Error" => Windows.UI.Color.FromArgb(255, 254, 242, 242),
            "Stripe" => Windows.UI.Color.FromArgb(255, 249, 250, 251),
            _ => Windows.UI.Color.FromArgb(255, 255, 255, 255),
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Uses semantic error text for invalid rows and the normal secondary text otherwise.</summary>
public sealed class ImportStatusForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? Microsoft.UI.Xaml.Application.Current.Resources["DangerBrush"]
            : Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"];

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Adds a slim left accent only to invalid preview rows.</summary>
public sealed class ImportRowBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? new Thickness(4, 0, 0, 1) : new Thickness(0, 0, 0, 1);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
