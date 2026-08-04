namespace Kirana.Application.Printing;

/// <summary>
/// Enumerates native Windows printers so a settings screen can let the store pick/store a
/// default (PRD §23). The actual print dialog (see Kirana.App's <c>InvoicePrintHelper</c>) always
/// shows the OS printer picker regardless of this preference — the WinUI/Windows App SDK print
/// pipeline has no supported way to print to a named printer without it — so this service exists
/// purely for printer discovery and for remembering/display a store's preferred printer name.
/// </summary>
public interface IPrinterDiscoveryService
{
    IReadOnlyList<string> GetInstalledPrinterNames();

    string? GetDefaultPrinterName();
}
