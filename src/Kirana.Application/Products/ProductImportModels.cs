using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>What committing a given import row would do.</summary>
public enum ProductImportRowStatus
{
    /// <summary>Row is valid and matches no existing product — a new product will be created.</summary>
    New,

    /// <summary>Row is valid and matched an existing product by SKU or barcode — it will be updated.</summary>
    Update,

    /// <summary>Row failed validation and will be skipped. <see cref="ProductImportRow.Errors"/> says why.</summary>
    Error,
}

/// <summary>
/// One parsed spreadsheet row, carrying both what the file said and what the import decided about
/// it. Rows are validated in bulk <em>before</em> anything is written, so the operator sees every
/// problem at once instead of discovering them one failed row at a time.
/// </summary>
public sealed class ProductImportRow
{
    /// <summary>1-based row number as it appears in the source file (header is row 1), so an error
    /// message can point the operator at the exact line to fix.</summary>
    public required int RowNumber { get; init; }

    public required ProductImportRowStatus Status { get; init; }

    /// <summary>Empty unless <see cref="Status"/> is <see cref="ProductImportRowStatus.Error"/>.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public string Name { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public string? Barcode { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string BrandName { get; init; } = string.Empty;
    public UnitOfMeasure Unit { get; init; } = UnitOfMeasure.Piece;

    /// <summary>Optional purchase pack (Phase 13A) — null unless both the pack unit and size
    /// columns were provided and validated.</summary>
    public UnitOfMeasure? PurchasePackUnit { get; init; }
    public decimal? PurchasePackSize { get; init; }
    public string? UnitDisplayText { get; init; }

    public decimal SellingPrice { get; init; }
    public decimal OpeningStock { get; init; }

    /// <summary>Set when <see cref="Status"/> is <see cref="ProductImportRowStatus.Update"/> — the
    /// existing product this row will overwrite.</summary>
    public int? MatchedProductId { get; init; }

    /// <summary>The fully-built create request, held so the commit step does not have to re-parse
    /// or re-validate. Null for error rows.</summary>
    internal CreateProductRequest? Request { get; init; }

    /// <summary>The row's original cell values, keyed by normalized header
    /// (<see cref="ProductImportParser.NormalizeHeader"/>). Kept so <see cref="IProductImportService.ReviseRowAsync"/>
    /// can let the operator correct just this row — fixing a typo in the app instead of editing the
    /// source file and re-uploading — without needing to re-parse anything.</summary>
    public required IReadOnlyDictionary<string, string> RawFields { get; init; }

    public string ErrorSummary => string.Join("; ", Errors);
}

/// <summary>
/// The result of parsing and validating an import file — everything the operator needs to decide
/// whether to commit, shown before a single row is written.
/// </summary>
public sealed class ProductImportPreview
{
    public IReadOnlyList<ProductImportRow> Rows { get; init; } = [];

    /// <summary>A file-level problem (unreadable file, missing Name column) that stopped parsing
    /// entirely. When set, <see cref="Rows"/> is empty and nothing can be imported.</summary>
    public string? FatalError { get; init; }

    /// <summary>Categories named in the file that do not exist yet and will be created on commit.
    /// Surfaced explicitly so bulk-importing a typo'd category name is visible, not silent.</summary>
    public IReadOnlyList<string> NewCategoryNames { get; init; } = [];

    public IReadOnlyList<string> NewBrandNames { get; init; } = [];

    public int NewCount => Rows.Count(r => r.Status == ProductImportRowStatus.New);
    public int UpdateCount => Rows.Count(r => r.Status == ProductImportRowStatus.Update);
    public int ErrorCount => Rows.Count(r => r.Status == ProductImportRowStatus.Error);

    public bool HasFatalError => FatalError is not null;
    public bool CanCommit => !HasFatalError && (NewCount + UpdateCount) > 0;
}

/// <summary>One editable column in the import template, for building a correction form around a
/// single <see cref="ProductImportRow.RawFields"/> entry without the UI needing to know the
/// service's internal alias list.</summary>
public sealed record ProductImportColumn(string CanonicalName, string FieldKey);

/// <summary>Outcome of actually writing an import to the database.</summary>
public sealed class ProductImportResult
{
    public int CreatedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int SkippedErrorCount { get; init; }
    public int CategoriesCreated { get; init; }
    public int BrandsCreated { get; init; }
}
