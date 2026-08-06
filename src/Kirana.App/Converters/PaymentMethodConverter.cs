using Kirana.Application.Reports;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml.Data;

namespace Kirana.App.Converters;

/// <summary>Displays a <see cref="PaymentMethod"/> the same way reports/receipts already do
/// ("Udhaar (Credit)" rather than the raw enum name "CustomerCredit") — the Payment dialog's method
/// picker previously showed the bare enum name, which visually clipped to just "Customer" in its
/// narrow column and read like a customer field, not a payment method.</summary>
public sealed class PaymentMethodConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is PaymentMethod method ? ReportFormatting.FormatPaymentMethod(method) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
