using Kirana.Domain.Entities;

namespace Kirana.Application.Barcodes;

/// <summary>
/// Fast exact-barcode → product lookup (PRD §13, §17). Deliberately a thin, standalone
/// service (not routed through <c>ProductService.SearchAsync</c>'s multi-bucket search) so the
/// future POS scan-to-cart flow gets a single indexed-equality query with no ordering/paging
/// overhead — the same lookup this phase's scan-test screen uses.
/// </summary>
public interface IBarcodeLookupService
{
    Task<Product?> LookupAsync(string barcode, CancellationToken cancellationToken = default);
}
