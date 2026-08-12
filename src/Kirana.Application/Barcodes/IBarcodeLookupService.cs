using Kirana.Domain.Entities;

namespace Kirana.Application.Barcodes;

/// <summary>
/// Fast exact-barcode → product lookup (PRD §13, §17). Deliberately a thin, standalone
/// service (not routed through <c>ProductService.SearchAsync</c>'s multi-bucket search) so the
/// POS scan-to-cart flow gets a single indexed-equality query with no ordering/paging
/// overhead — the same lookup this phase's scan-test screen uses.
/// </summary>
public interface IBarcodeLookupService
{
    /// <summary>
    /// The POS path. Resolves any ACTIVE barcode on an ACTIVE product; returns null for anything
    /// else, so a retired code or a discontinued product can never enter a cart. Requires no
    /// permission — billing users scan constantly.
    /// </summary>
    Task<Product?> LookupAsync(string barcode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Diagnostic lookup for the scan-test and product-management screens: finds a barcode even
    /// when it (or its product) is inactive, so the operator sees "known but retired" rather than a
    /// bare "not found". Never use this on the POS path — <see cref="LookupAsync"/> is the one that
    /// enforces the active-only rule keeping retired codes out of a cart.
    /// </summary>
    Task<BarcodeLookupDiagnostic?> LookupDiagnosticAsync(string barcode, CancellationToken cancellationToken = default);
}
