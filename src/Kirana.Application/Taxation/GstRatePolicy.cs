namespace Kirana.Application.Taxation;

/// <summary>Indian GST slabs supported by product master data. A null product rate is treated as
/// exempt and is snapshotted as 0% when a transaction is completed. CESS is intentionally kept
/// out of the active UI/calculation contract until the store enables that future capability.</summary>
public static class GstRatePolicy
{
    public static IReadOnlyList<decimal> SupportedRates { get; } = [0m, 5m, 12m, 18m, 28m];

    public static bool IsSupported(decimal rate) => SupportedRates.Contains(rate);

    public static void EnsureSupported(decimal rate, string? parameterName = null)
    {
        if (!IsSupported(rate))
        {
            throw new ArgumentException("GST rate must be one of 0%, 5%, 12%, 18%, or 28%.", parameterName);
        }
    }
}
