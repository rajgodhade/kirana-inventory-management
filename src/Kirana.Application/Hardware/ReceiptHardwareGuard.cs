namespace Kirana.Application.Hardware;

/// <summary>Fault boundary used after a sale is committed. Device/settings failures are converted
/// to a warning result and can never escape into billing or roll back business data.</summary>
public sealed class ReceiptHardwareGuard(
    IHardwareSettingsService settingsService,
    IPrinterService printerService) : IReceiptHardwareGuard
{
    public async Task<HardwareOperationResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            return await printerService.IsPrinterAvailableAsync(settings.ReceiptPrinterName, cancellationToken)
                ? HardwareOperationResult.Success("Receipt printer is available.")
                : HardwareOperationResult.Failure("Receipt printer unavailable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HardwareOperationResult.Failure($"Printer status unavailable: {ex.Message}", ex);
        }
    }
}
