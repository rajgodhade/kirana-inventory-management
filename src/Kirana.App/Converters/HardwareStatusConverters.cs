using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kirana.App.Converters;

public sealed class HardwareStatusForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value is HardwareStatus.Connected
            ? Color.FromArgb(255, 22, 101, 52)
            : value is HardwareStatus.Error or HardwareStatus.Offline or HardwareStatus.Disconnected
                ? Color.FromArgb(255, 185, 28, 28)
                : Color.FromArgb(255, 161, 98, 7));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class HardwareStatusBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value is HardwareStatus.Connected
            ? Color.FromArgb(255, 220, 252, 231)
            : value is HardwareStatus.Error or HardwareStatus.Offline or HardwareStatus.Disconnected
                ? Color.FromArgb(255, 254, 226, 226)
                : Color.FromArgb(255, 254, 243, 199));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
