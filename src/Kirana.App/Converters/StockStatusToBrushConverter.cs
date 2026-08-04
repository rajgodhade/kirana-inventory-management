using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kirana.App.Converters;

/// <summary>Colors the stock-status badge (PRD §26): red for out of stock, amber for low stock.</summary>
public sealed class StockStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value as string) switch
        {
            "OUT OF STOCK" => new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
            "LOW STOCK" => new SolidColorBrush(Color.FromArgb(255, 202, 80, 16)),
            _ => new SolidColorBrush(Colors.Transparent),
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
