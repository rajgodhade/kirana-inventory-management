using System.Drawing;
using System.Drawing.Printing;
using Kirana.Application.Hardware;
using Kirana.Domain.Entities;

namespace Kirana.App.Hardware;

public sealed class WindowsPrinterService : IPrinterService
{
    public Task<IReadOnlyList<HardwareDevice>> GetInstalledPrintersAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<HardwareDevice>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var defaultName = ReadDefaultPrinterName();
            return PrinterSettings.InstalledPrinters.Cast<string>()
                .Select(name => BuildDevice(name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }, cancellationToken);

    public async Task<HardwareDevice?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
    {
        var printers = await GetInstalledPrintersAsync(cancellationToken);
        return printers.FirstOrDefault(x => x.IsDefault);
    }

    public async Task<bool> IsPrinterAvailableAsync(string? printerName, CancellationToken cancellationToken = default)
    {
        var printers = await GetInstalledPrintersAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return printers.Any(x => x.IsDefault
                && x.Status == HardwareStatus.Connected
                && x.Capabilities.HasFlag(HardwareCapability.PrintReceipt));
        }

        return printers.Any(x => string.Equals(x.FriendlyName, printerName, StringComparison.OrdinalIgnoreCase)
            && x.Status == HardwareStatus.Connected
            && x.Capabilities.HasFlag(HardwareCapability.PrintReceipt));
    }

    public Task<HardwareOperationResult> PrintTestPageAsync(
        string? printerName,
        PrinterPaperSize paperSize,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = string.IsNullOrWhiteSpace(printerName) ? ReadDefaultPrinterName() : printerName;
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return HardwareOperationResult.Failure("No physical receipt printer is configured.");
            }
            if (IsVirtualPrinter(resolved))
            {
                return HardwareOperationResult.Failure($"'{resolved}' is a virtual document printer, not a receipt printer.");
            }

            using var document = new PrintDocument { DocumentName = "Kirana hardware test" };
            document.PrinterSettings.PrinterName = resolved;
            if (!document.PrinterSettings.IsValid)
            {
                return HardwareOperationResult.Failure($"Printer '{resolved}' is unavailable.");
            }

            document.PrintController = new StandardPrintController();
            document.PrintPage += (_, e) => DrawTestPage(e, resolved, paperSize);
            document.Print();
            return HardwareOperationResult.Success($"Test page sent to {resolved}.");
        }
        catch (Exception ex)
        {
            return HardwareOperationResult.Failure($"Test print failed: {ex.Message}", ex);
        }
    }, cancellationToken);

    private static HardwareDevice BuildDevice(string name, bool isDefault)
    {
        var settings = new PrinterSettings { PrinterName = name };
        var isThermal = name.Contains("thermal", StringComparison.OrdinalIgnoreCase)
            || name.Contains("receipt", StringComparison.OrdinalIgnoreCase)
            || name.Contains("pos", StringComparison.OrdinalIgnoreCase);
        var isVirtual = IsVirtualPrinter(name);
        return new HardwareDevice
        {
            DeviceId = $"printer:{name}",
            Type = isVirtual ? HardwareType.A4Printer : isThermal ? HardwareType.ThermalPrinter : HardwareType.Printer,
            FriendlyName = name,
            Manufacturer = "Windows print queue",
            Model = settings.IsValid ? settings.DefaultPageSettings.PaperSize.PaperName : null,
            ConnectionType = isVirtual ? HardwareConnectionType.Virtual : HardwareConnectionType.WindowsPrintQueue,
            Status = settings.IsValid ? HardwareStatus.Connected : HardwareStatus.Offline,
            Capabilities = isVirtual
                ? HardwareCapability.PrintA4
                : HardwareCapability.PrintReceipt | HardwareCapability.PrintA4,
            LastSeen = settings.IsValid ? DateTimeOffset.UtcNow : null,
            IsDefault = isDefault,
        };
    }

    private static string? ReadDefaultPrinterName()
    {
        var settings = new PrinterSettings();
        return settings.IsValid && !string.IsNullOrWhiteSpace(settings.PrinterName) ? settings.PrinterName : null;
    }

    private static bool IsVirtualPrinter(string name) =>
        name.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Microsoft XPS", StringComparison.OrdinalIgnoreCase)
        || name.Contains("OneNote", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Fax", StringComparison.OrdinalIgnoreCase)
        || name.Contains("PDF", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Document Writer", StringComparison.OrdinalIgnoreCase);

    private static void DrawTestPage(PrintPageEventArgs e, string printerName, PrinterPaperSize paperSize)
    {
        using var title = new Font("Segoe UI", 16, FontStyle.Bold);
        using var body = new Font("Segoe UI", 10);
        using var brush = new SolidBrush(Color.Black);
        var x = e.MarginBounds.Left;
        var y = e.MarginBounds.Top;
        var graphics = e.Graphics ?? throw new InvalidOperationException("The printer did not provide a drawing surface.");
        graphics.DrawString("Kirana POS", title, brush, x, y);
        y += 34;
        graphics.DrawString("Hardware test page", body, brush, x, y);
        y += 24;
        graphics.DrawString($"Printer: {printerName}", body, brush, x, y);
        y += 20;
        graphics.DrawString($"Paper: {paperSize}", body, brush, x, y);
        y += 20;
        graphics.DrawString($"Printed: {DateTime.Now:dd MMM yyyy, hh:mm:ss tt}", body, brush, x, y);
        y += 32;
        graphics.DrawString("If this text is clear, the printer is ready.", body, brush, x, y);
    }
}
