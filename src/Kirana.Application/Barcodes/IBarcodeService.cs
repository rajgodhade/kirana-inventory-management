using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.Application.Barcodes;

/// <summary>
/// Barcode assignment, format validation, and duplicate prevention (PRD §16-17). The single
/// source of truth for barcode rules — <c>ProductService</c> delegates here instead of
/// duplicating format/uniqueness checks.
/// </summary>
public interface IBarcodeService
{
    /// <summary>Existing-manufacturer-barcode detection: EAN-13 when it's 13 digits with a
    /// valid check digit, otherwise CODE128 (still a perfectly valid barcode, just not GS1-checked).</summary>
    BarcodeSymbology DetermineSymbology(string barcode);

    /// <summary>True when the 13-digit check digit matches — used for a UI validity indicator,
    /// not to reject otherwise-storable barcodes.</summary>
    bool IsValidEan13(string barcode);

    /// <summary>Throws <see cref="ArgumentException"/> if the value can't be encoded/printed at all
    /// (empty, non-printable characters, too long) — not a GS1 checksum requirement.</summary>
    void ValidateFormat(string barcode);

    /// <summary>Throws <see cref="InvalidOperationException"/> if another product already uses this barcode.</summary>
    Task EnsureAvailableAsync(string barcode, int? excludingProductId, CancellationToken cancellationToken = default);

    /// <summary>Generates a unique internal EAN-13 barcode (PRD §16 "Internal Barcode") — does not persist it.</summary>
    Task<string> GenerateInternalBarcodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a barcode to a product: uses <paramref name="explicitBarcode"/> if given (validated
    /// for format + uniqueness), otherwise generates and assigns a new internal barcode. Persists
    /// the change and writes an audit log entry.
    /// </summary>
    Task<Product> AssignBarcodeAsync(int productId, string? explicitBarcode, int? performedByUserId, CancellationToken cancellationToken = default);
}
