using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.Application.Barcodes;

/// <summary>
/// Barcode assignment, format validation, and duplicate prevention (PRD §16-17). The single
/// source of truth for barcode rules — <c>ProductService</c> and <c>ProductImportService</c>
/// delegate here instead of duplicating format/uniqueness checks.
/// <para>Phase 13B: a product owns many <see cref="ProductBarcode"/> rows rather than one string,
/// so every mutation below goes through this service to keep the "exactly one active primary" and
/// "globally unique normalized value" invariants in one place.</para>
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

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if another product already uses this barcode.
    /// Compares on the normalized value, so casing differences still collide, and deliberately
    /// ignores <see cref="ProductBarcode.IsActive"/> — a retired code keeps ownership of its value.
    /// </summary>
    Task EnsureAvailableAsync(string barcode, int? excludingProductId, CancellationToken cancellationToken = default);

    /// <summary>Id of the product currently owning this barcode (active or retired), or null when
    /// it's free — lets the UI say "used by Tata Salt 1kg" rather than a bare "already exists".</summary>
    Task<int?> FindOwningProductIdAsync(string barcode, CancellationToken cancellationToken = default);

    /// <summary>Generates a unique internal EAN-13 barcode (PRD §16 "Internal Barcode") — does not persist it.</summary>
    Task<string> GenerateInternalBarcodeAsync(CancellationToken cancellationToken = default);

    /// <summary>All barcodes for a product — active before retired, primary first — for the
    /// management grid.</summary>
    Task<IReadOnlyList<ProductBarcode>> GetForProductAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a barcode to a product. When <paramref name="value"/> is null/blank an internal EAN-13
    /// is generated. The first barcode a product ever receives becomes its primary automatically;
    /// <paramref name="makePrimary"/> promotes a later one, demoting the previous primary.
    /// </summary>
    Task<ProductBarcode> AddBarcodeAsync(
        int productId, string? value, bool makePrimary, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Corrects an existing barcode's value in place, revalidating format and global
    /// uniqueness and recomputing the symbology and normalized value.</summary>
    Task<ProductBarcode> UpdateBarcodeValueAsync(
        int barcodeId, string newValue, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Makes this barcode the product's primary, demoting the previous one. The barcode
    /// must be active — a retired code can't be what label printing defaults to.</summary>
    Task SetPrimaryAsync(int barcodeId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires or restores a barcode without deleting it, so an old shelf label stays diagnosable.
    /// Retiring the primary auto-promotes the oldest remaining active barcode; when nothing else is
    /// active the retired row keeps the primary flag, so the product never ends up with barcodes
    /// but no primary. Reactivation re-checks uniqueness.
    /// </summary>
    Task SetBarcodeActiveAsync(
        int barcodeId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a barcode — for codes entered by mistake that never reached a
    /// shelf. Prefer <see cref="SetBarcodeActiveAsync"/> for anything that was ever printed.</summary>
    Task RemoveBarcodeAsync(int barcodeId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a new PRIMARY barcode to a product: uses <paramref name="explicitBarcode"/> if given
    /// (validated for format + uniqueness), otherwise generates a new internal one. Kept as the
    /// convenience entry point the barcode-label dialog uses — "give this product a printable code
    /// right now" — layered over <see cref="AddBarcodeAsync"/>.
    /// </summary>
    Task<ProductBarcode> AssignBarcodeAsync(int productId, string? explicitBarcode, int? performedByUserId, CancellationToken cancellationToken = default);
}
