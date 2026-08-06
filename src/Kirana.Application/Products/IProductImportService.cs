namespace Kirana.Application.Products;

/// <summary>
/// Bulk product import from a .csv/.xlsx file (PRD §51 data tools). Deliberately a two-step
/// validate-then-commit flow: <see cref="BuildPreviewAsync"/> never writes anything, so the
/// operator always sees exactly what would happen — including every bad row — before any of it
/// touches the catalogue. Gated by <see cref="Domain.Entities.PermissionKeys.ProductsEdit"/>, the
/// same permission that guards creating a product by hand.
/// </summary>
public interface IProductImportService
{
    /// <summary>Parses and validates the file without writing. Safe to call repeatedly.</summary>
    Task<ProductImportPreview> BuildPreviewAsync(
        Stream fileStream, string fileName, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Writes the importable rows from a preview in a single transaction — either every
    /// valid row lands or none does, so a failure part-way through can never leave the catalogue
    /// half-imported. Error rows are skipped, never partially applied.</summary>
    Task<ProductImportResult> CommitAsync(
        ProductImportPreview preview, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>A ready-to-fill CSV template with the expected header row and one example line.</summary>
    string BuildCsvTemplate();

    /// <summary>The editable columns, in template order — lets a correction form label its fields
    /// without duplicating the service's internal header-alias list.</summary>
    IReadOnlyList<ProductImportColumn> Columns { get; }

    /// <summary>Re-validates <paramref name="preview"/> with row <paramref name="rowNumber"/>'s
    /// values replaced by <paramref name="updatedFields"/> — the "fix this row without re-uploading
    /// the file" path. Re-runs full validation over every row (not just the edited one), because
    /// changing one row's SKU/barcode can resolve — or create — a duplicate-within-file conflict
    /// against any other row, and can change which categories/brands are newly needed. Writes
    /// nothing; still just a preview.</summary>
    Task<ProductImportPreview> ReviseRowAsync(
        ProductImportPreview preview, int rowNumber, IReadOnlyDictionary<string, string> updatedFields,
        int? performedByUserId, CancellationToken cancellationToken = default);
}
