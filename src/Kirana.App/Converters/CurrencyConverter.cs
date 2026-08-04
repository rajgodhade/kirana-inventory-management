using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>
/// Formats a decimal as Indian rupees — "₹1,24,500.00", using the en-IN lakh/crore digit grouping
/// rather than the western thousands grouping. Existing screens bound raw decimals straight into
/// TextBlocks, which rendered "260.0"; every money value should go through this instead.
/// </summary>
public sealed class CurrencyConverter : IValueConverter
{
    private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (!TryGetDecimal(value, out var amount))
        {
            return string.Empty;
        }

        // parameter="bare" omits the symbol, for places where a ₹ already sits alongside.
        return string.Equals(parameter as string, "bare", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("N2", IndianCulture)
            : "₹" + amount.ToString("N2", IndianCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    internal static bool TryGetDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case decimal d:
                result = d;
                return true;
            case double dbl:
                result = (decimal)dbl;
                return true;
            case int i:
                result = i;
                return true;
            default:
                result = 0m;
                return value is not null
                    && decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
    }
}

/// <summary>Formats a quantity without trailing zeros — "2", "2.5", "0.75".</summary>
public sealed class QuantityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        CurrencyConverter.TryGetDecimal(value, out var quantity) ? quantity.ToString("0.###") : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

// Note: there is deliberately no "amount -> brush" converter here. The theme is applied by setting
// RequestedTheme on the root element, so a converter reaching into Application.Current.Resources
// would resolve against the *application* theme and hand back the wrong brush. Screens that need
// to colour an amount toggle between two TextBlocks whose Foreground uses {ThemeResource} in XAML,
// which resolves against the element's theme correctly.
