using Kirana.Domain.Entities;

namespace Kirana.Application.Reports;

/// <summary>Shared display-label formatting so every report and chart calls the payment method by
/// the same name rather than each screen inventing its own.</summary>
public static class ReportFormatting
{
    public static string FormatPaymentMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Upi => "UPI",
        PaymentMethod.Card => "Card",
        PaymentMethod.CustomerCredit => "Udhaar (Credit)",
        _ => "Cash",
    };
}
