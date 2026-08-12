using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.Application.Barcodes;

/// <summary>
/// What a scanned code resolves to when retired barcodes and inactive products are visible too
/// (Phase 13B) — the management/scan-test view of a lookup. Lets the Barcode Scan page explain
/// "known but retired" instead of showing a misleading "not found".
/// </summary>
public sealed record BarcodeLookupDiagnostic(
    Product Product,
    string Value,
    BarcodeSymbology Symbology,
    bool IsPrimary,
    bool IsBarcodeActive,
    bool IsProductActive)
{
    /// <summary>"Primary" / "Alternate", for the scan-test screen's Role field.</summary>
    public string RoleText => IsPrimary ? "Primary" : "Alternate";

    public string StatusText => IsBarcodeActive ? "Active" : "Retired";

    /// <summary>Why billing would refuse this code, or null when it scans normally. Product-inactive
    /// is checked first: it's the more consequential fact and the one to fix first.</summary>
    public string? BlockedReason =>
        !IsProductActive ? "This product is inactive, so the code will not scan at billing."
        : !IsBarcodeActive ? "This barcode is retired, so the code will not scan at billing."
        : null;

    public bool WouldScanAtPos => BlockedReason is null;
}
