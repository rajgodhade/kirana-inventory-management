using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>Indents a Payment Summary sub-row ("Cash Received", "Change Returned") under the
/// payment line it belongs to — the on-screen equivalent of the two-space prefix the printed
/// receipt uses for the same rows.</summary>
public sealed class DetailIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? new Thickness(16, 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
