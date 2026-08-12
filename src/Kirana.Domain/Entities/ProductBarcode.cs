using Kirana.Domain.Barcodes;
using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One scannable code for a product (Phase 13B). A product can carry many — the manufacturer's
/// EAN-13, a second EAN after a supplier repack, and an internally generated code for loose goods
/// — and every active one must resolve to the same product at the till.
/// <para><see cref="NormalizedValue"/> is the uniqueness key, not <see cref="Value"/>: barcodes are
/// compared case-insensitively store-wide (see <see cref="BarcodeNormalizer"/>) and SQLite's
/// default collation is case-sensitive, so the comparison key is stored rather than computed per
/// query. That also keeps the POS lookup a single indexed equality probe with no function call
/// wrapped around the column.</para>
/// <para><see cref="IsActive"/> retires a code without deleting it, so a label still stuck on a
/// shelf can be diagnosed as "known but retired" instead of silently reading as unknown — and so a
/// retired value can never be reassigned to a different product.</para>
/// </summary>
public class ProductBarcode : Entity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>The code exactly as entered or scanned (trimmed), used for display and printing.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Upper-invariant form of <see cref="Value"/>; the store-wide uniqueness key and the
    /// column every lookup queries. Always written via <see cref="BarcodeNormalizer.Normalize"/>.</summary>
    public string NormalizedValue { get; set; } = string.Empty;

    /// <summary>Derived from <see cref="Value"/> at write time (EAN-13 when it's 13 digits with a
    /// valid check digit, otherwise CODE128). Stored rather than recomputed so label printing and
    /// the management grid don't have to re-derive it for every row.</summary>
    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Code128;

    /// <summary>Exactly one active barcode per product is primary — the one label printing and
    /// exports default to. Enforced in <c>BarcodeService</c> and guarded by a filtered unique
    /// index, so it's a database invariant rather than a convention.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>True when this code came from <c>GenerateInternalBarcodeAsync</c> rather than off a
    /// manufacturer's pack. Provenance only — never affects lookup.</summary>
    public bool IsInternal { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional operator note, e.g. "old supplier pack, pre-2025".</summary>
    public string? Notes { get; set; }
}
