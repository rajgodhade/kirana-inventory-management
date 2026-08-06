using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>Small emoji glyph for a <see cref="PaymentMethod"/>, paired with
/// <see cref="PaymentMethodConverter"/>'s text label in payment-method pickers.</summary>
public sealed class PaymentMethodIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        PaymentMethod.Cash => "\U0001F4B5",
        PaymentMethod.Upi => "\U0001F4F1",
        PaymentMethod.Card => "\U0001F4B3",
        _ => string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
