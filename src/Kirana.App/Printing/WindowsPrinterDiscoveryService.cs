using System.Drawing.Printing;
using Kirana.Application.Printing;

namespace Kirana.App.Printing;

/// <summary>Enumerates installed Windows printers via the print spooler (PRD §23) — used by the
/// invoice preview/print settings so a store can see and remember a preferred printer name. This
/// is separate from the OS print dialog itself (see <see cref="InvoicePrintHelper"/>), which
/// always shows its own picker; there is no supported WinUI/Windows App SDK API to print to a
/// named printer while skipping that dialog.</summary>
public sealed class WindowsPrinterDiscoveryService : IPrinterDiscoveryService
{
    public IReadOnlyList<string> GetInstalledPrinterNames() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    public string? GetDefaultPrinterName()
    {
        var name = new PrinterSettings().PrinterName;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
