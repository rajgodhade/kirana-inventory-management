using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kirana.App.Converters;

/// <summary>Colors the expiry-date text on the Products list (Phase 27): red once a batch has
/// actually expired, amber when it's due within 30 days, default text color otherwise.</summary>
public sealed class ExpiryStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as string) switch
        {
            "EXPIRED" => new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
            "EXPIRING SOON" => new SolidColorBrush(Color.FromArgb(255, 202, 80, 16)),
            _ => new SolidColorBrush(Colors.Transparent),
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
