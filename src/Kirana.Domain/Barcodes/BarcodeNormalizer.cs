namespace Kirana.Domain.Barcodes;

/// <summary>
/// The single definition of "the same barcode" store-wide (Phase 13B). Before this existed,
/// <c>BarcodeService</c> compared barcodes case-sensitively in SQL while <c>ProductImportService</c>
/// deduped them with OrdinalIgnoreCase dictionaries — so "abc123" and "ABC123" were the same value
/// to the importer and two different values to the uniqueness check. Every layer now normalizes
/// through here: the EF configuration, the services, the importer, and the backfill migration.
/// </summary>
public static class BarcodeNormalizer
{
    /// <summary>Longest storable barcode. Matches the rule <c>BarcodeService.ValidateFormat</c>
    /// actually enforces; the EF column is sized to this so the two cannot drift apart again.</summary>
    public const int MaxBarcodeLength = 48;

    /// <summary>Trim + upper-invariant. Deliberately NOT culture-sensitive: <c>ToUpper()</c> under a
    /// Turkish locale maps 'i' to 'İ', which would make the stored uniqueness key depend on the
    /// machine's regional settings. Deliberately does NOT strip internal spaces or punctuation
    /// either — <c>ValidateFormat</c> permits any printable ASCII, and silently collapsing
    /// characters would make two visibly different codes collide.</summary>
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
