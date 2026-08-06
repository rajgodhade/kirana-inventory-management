using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Products;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the bulk product import dialog. Mirrors the service's two-step shape: pick a file to get a
/// preview (which writes nothing), review what would happen, then explicitly commit.
/// </summary>
public sealed partial class ProductImportViewModel(IProductImportService importService, int? currentUserId) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyPropertyChangedFor(nameof(ImportReadinessText))]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileSizeText = string.Empty;

    [ObservableProperty]
    private int _rowCount;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyPropertyChangedFor(nameof(ImportReadinessText))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyPropertyChangedFor(nameof(ImportReadinessText))]
    private bool _hasPreview;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _newReferenceDataText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewReferenceData))]
    private bool _showNewReferenceData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyPropertyChangedFor(nameof(ImportReadinessText))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    private bool _isCommitted;

    [ObservableProperty]
    private int _createdCount;

    [ObservableProperty]
    private int _updatedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private int _categoriesCreated;

    [ObservableProperty]
    private int _brandsCreated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportedAtText))]
    private DateTimeOffset? _importedAt;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    public bool HasResults => IsCommitted;
    public bool HasSkippedRows => SkippedCount > 0;
    public int RowsProcessed => CreatedCount + UpdatedCount + SkippedCount;
    public string PrimaryButtonText => IsCommitted ? "✓ Import Complete" : "Import";
    public string ImportedAtText => ImportedAt?.ToString("dd MMM yyyy h:mm tt") ?? string.Empty;
    public int NewCount => _preview?.NewCount ?? 0;
    public int UpdateCount => _preview?.UpdateCount ?? 0;
    public int ErrorCount => _preview?.ErrorCount ?? 0;
    public string ImportReadinessText =>
        !HasFile ? "Choose a CSV or XLSX file to begin."
        : IsBusy ? "Checking your file…"
        : !HasPreview ? "File needs attention before it can be imported."
        : CanCommit ? "Ready to import valid rows. Rows with errors will be skipped."
        : CommitDisabledReason;

    public string CommitDisabledReason =>
        !HasFile ? "Choose a file before importing."
        : IsBusy ? "Please wait for validation to finish."
        : IsCommitted ? "Successfully imported. Choose another file to start a new import."
        : _preview?.HasFatalError == true ? "Fix the file-level validation error before importing."
        : _preview is null ? "Preview the file before importing."
        : "There are no valid rows to import. Fix or remove invalid rows first.";

    public ObservableCollection<ProductImportRowViewModel> Rows { get; } = [];

    public bool HasFile => FileName.Length > 0;
    public bool HasNewReferenceData => ShowNewReferenceData;

    /// <summary>Import is only offered once the preview has at least one non-removed, non-error row,
    /// and only once — committing twice would double-create every row.</summary>
    public bool CanCommit =>
        HasPreview && !IsBusy && !IsCommitted && _preview is { HasFatalError: false }
        && _preview.Rows.Any(r => r.Status != ProductImportRowStatus.Error && !_removedRowNumbers.Contains(r.RowNumber));

    private ProductImportPreview? _preview;

    /// <summary>Rows the operator has explicitly excluded via "Remove" — kept separately from
    /// <see cref="_preview"/> because every revision (Fix/Import-as-New) rebuilds the preview and
    /// its row view-models from scratch, and an exclusion is a UI-only decision the service never
    /// needs to know about.</summary>
    private readonly HashSet<int> _removedRowNumbers = [];

    /// <summary>The editable columns a "Fix row" form should offer, in template order.</summary>
    public IReadOnlyList<ProductImportColumn> Columns => importService.Columns;

    public string BuildTemplate() => importService.BuildCsvTemplate();

    public void ShowFileTypeError() => ErrorMessage = "Choose a CSV or XLSX product file.";

    public async Task LoadPreviewAsync(Stream stream, string fileName, long? fileSizeBytes = null)
    {
        ErrorMessage = null;
        SuccessMessage = null;
        IsBusy = true;
        try
        {
            FileName = fileName;
            FileSizeText = fileSizeBytes is { } size ? FormatFileSize(size) : string.Empty;
            _removedRowNumbers.Clear();
            ApplyPreview(await importService.BuildPreviewAsync(stream, fileName, currentUserId));
            IsCommitted = false;
            CreatedCount = 0;
            UpdatedCount = 0;
            SkippedCount = 0;
            CategoriesCreated = 0;
            BrandsCreated = 0;
            ImportedAt = null;
            ElapsedText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasPreview = false;
            RowCount = 0;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCommit));
        }
    }

    /// <summary>Corrects one row in place — the "fix the CSV without leaving the app and
    /// re-uploading" path. Re-validates the whole preview, since one row's fix can affect others
    /// (e.g. resolving a duplicate SKU) or the new-category/brand lists.</summary>
    public Task<bool> ReviseRowAsync(int rowNumber, IReadOnlyDictionary<string, string> updatedFields) =>
        ApplyRevisionAsync(rowNumber, updatedFields);

    /// <summary>Forces a row that matched an existing product (by SKU/Barcode) to import as a
    /// separate new product instead of updating the match — the "upload that product again" action
    /// next to a detected duplicate. Clears both SKU and Barcode on this row only, since either one
    /// alone could have caused the match, and a value that's already in use elsewhere can't be
    /// reused for a second, distinct product without violating uniqueness.</summary>
    public Task<bool> ImportAsNewAsync(int rowNumber)
    {
        var row = _preview?.Rows.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (row is null)
        {
            return Task.FromResult(false);
        }

        var skuKey = Columns.First(c => c.CanonicalName == "SKU").FieldKey;
        var barcodeKey = Columns.First(c => c.CanonicalName == "Barcode").FieldKey;
        var updatedFields = new Dictionary<string, string>(row.RawFields)
        {
            [skuKey] = string.Empty,
            [barcodeKey] = string.Empty,
        };

        return ApplyRevisionAsync(rowNumber, updatedFields);
    }

    private async Task<bool> ApplyRevisionAsync(int rowNumber, IReadOnlyDictionary<string, string> updatedFields)
    {
        if (_preview is null || IsCommitted)
        {
            return false;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            ApplyPreview(await importService.ReviseRowAsync(_preview, rowNumber, updatedFields, currentUserId));
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCommit));
        }
    }

    /// <summary>Excludes a row from the import without touching the file or re-validating —
    /// "remove" is purely "don't commit this one," so nothing else about the preview changes.</summary>
    public void RemoveRow(int rowNumber)
    {
        _removedRowNumbers.Add(rowNumber);
        var row = Rows.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (row is not null)
        {
            row.IsRemoved = true;
        }

        RefreshSummaryText();
        OnPropertyChanged(nameof(CanCommit));
    }

    public void UndoRemoveRow(int rowNumber)
    {
        _removedRowNumbers.Remove(rowNumber);
        var row = Rows.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (row is not null)
        {
            row.IsRemoved = false;
        }

        RefreshSummaryText();
        OnPropertyChanged(nameof(CanCommit));
    }

    private void ApplyPreview(ProductImportPreview preview)
    {
        _preview = preview;

        Rows.Clear();
        foreach (var row in preview.Rows)
        {
            Rows.Add(new ProductImportRowViewModel(row, Columns) { IsRemoved = _removedRowNumbers.Contains(row.RowNumber) });
        }

        if (preview.HasFatalError)
        {
            ErrorMessage = preview.FatalError;
            SummaryText = string.Empty;
            ShowNewReferenceData = false;
            HasPreview = false;
            RowCount = 0;
            return;
        }

        RefreshSummaryText();

        var parts = new List<string>();
        if (preview.NewCategoryNames.Count > 0)
        {
            parts.Add($"New categories: {string.Join(", ", preview.NewCategoryNames)}");
        }

        if (preview.NewBrandNames.Count > 0)
        {
            parts.Add($"New brands: {string.Join(", ", preview.NewBrandNames)}");
        }

        NewReferenceDataText = string.Join("   •   ", parts);
        ShowNewReferenceData = parts.Count > 0;
        HasPreview = true;
        RowCount = preview.Rows.Count;
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(ErrorCount));
    }

    private void RefreshSummaryText()
    {
        if (_preview is null || _preview.HasFatalError)
        {
            return;
        }

        var newCount = _preview.Rows.Count(r => r.Status == ProductImportRowStatus.New && !_removedRowNumbers.Contains(r.RowNumber));
        var updateCount = _preview.Rows.Count(r => r.Status == ProductImportRowStatus.Update && !_removedRowNumbers.Contains(r.RowNumber));
        var errorCount = _preview.Rows.Count(r => r.Status == ProductImportRowStatus.Error && !_removedRowNumbers.Contains(r.RowNumber));

        SummaryText = $"{newCount} new · {updateCount} to update · {errorCount} with errors (skipped)"
            + (_removedRowNumbers.Count > 0 ? $" · {_removedRowNumbers.Count} removed" : "");
    }

    public async Task CommitAsync()
    {
        if (_preview is null)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // "Remove" is a UI-only exclusion the service never sees — build a preview covering
            // only the surviving rows before handing it off to commit.
            var commitPreview = _removedRowNumbers.Count == 0
                ? _preview
                : new ProductImportPreview
                {
                    Rows = _preview.Rows.Where(r => !_removedRowNumbers.Contains(r.RowNumber)).ToList(),
                    FatalError = _preview.FatalError,
                    NewCategoryNames = _preview.NewCategoryNames,
                    NewBrandNames = _preview.NewBrandNames,
                };

            var result = await importService.CommitAsync(commitPreview, currentUserId);
            IsCommitted = true;
            CreatedCount = result.CreatedCount;
            UpdatedCount = result.UpdatedCount;
            SkippedCount = result.SkippedErrorCount;
            CategoriesCreated = result.CategoriesCreated;
            BrandsCreated = result.BrandsCreated;
            ImportedAt = DateTimeOffset.Now;
            stopwatch.Stop();
            ElapsedText = $"{stopwatch.Elapsed.TotalSeconds:0.0} seconds";
            SuccessMessage =
                $"Imported successfully — {result.CreatedCount} created, {result.UpdatedCount} updated"
                + (result.SkippedErrorCount > 0 ? $", {result.SkippedErrorCount} skipped" : "")
                + ".";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(ImportReadinessText));
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasSkippedRows));
            OnPropertyChanged(nameof(RowsProcessed));
        }
    }

    /// <summary>Clears presentation/session state only. The importer and stored data are untouched.</summary>
    public void ResetForAnotherFile()
    {
        if (IsBusy)
        {
            return;
        }

        _preview = null;
        _removedRowNumbers.Clear();
        Rows.Clear();
        FileName = string.Empty;
        FileSizeText = string.Empty;
        RowCount = 0;
        ErrorMessage = null;
        SuccessMessage = null;
        SummaryText = string.Empty;
        NewReferenceDataText = string.Empty;
        ShowNewReferenceData = false;
        HasPreview = false;
        IsCommitted = false;
        CreatedCount = 0;
        UpdatedCount = 0;
        SkippedCount = 0;
        CategoriesCreated = 0;
        BrandsCreated = 0;
        ImportedAt = null;
        ElapsedText = string.Empty;
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(HasSkippedRows));
        OnPropertyChanged(nameof(RowsProcessed));
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes / (1024d * 1024d):0.#} MB",
    };
}

/// <summary>
/// One preview row, flattened for display. Also owns the row's inline "Fix" editor state —
/// deliberately an in-place expand/collapse within the same ListView item rather than a second
/// <c>ContentDialog</c>: WinUI 3 only supports one open ContentDialog per XamlRoot, and opening a
/// second one from a control inside an already-open dialog silently kills the app (hit and fixed
/// once before, in the Phase 5 reprint flow).
/// </summary>
public sealed partial class ProductImportRowViewModel(ProductImportRow row, IReadOnlyList<ProductImportColumn> columns) : ObservableObject
{
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNewBadge))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBadge))]
    [NotifyPropertyChangedFor(nameof(ShowErrorBadge))]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    [NotifyPropertyChangedFor(nameof(RowOpacity))]
    private bool _isRemoved;

    public ObservableCollection<ProductImportFieldViewModel> EditFields { get; } = [];

    public int RowNumber => row.RowNumber;
    public string RowNumberText => $"Row {row.RowNumber}";
    public string FixButtonAutomationName => $"Fix row {row.RowNumber}";
    public string SaveFixButtonAutomationName => $"Save fix for row {row.RowNumber}";
    public string ImportAsNewButtonAutomationName => $"Import row {row.RowNumber} as new product";
    public string RemoveButtonAutomationName => $"Remove row {row.RowNumber}";
    public string UndoRemoveButtonAutomationName => $"Undo remove for row {row.RowNumber}";
    public string Name => row.Name.Length > 0 ? row.Name : "(no name)";
    public string ProductCode => row.Sku ?? "—";
    public string Barcode => row.Barcode ?? "—";
    public string SellingPrice => row.SellingPrice.ToString("N2");
    public string ValidationStatus => IsError ? "Needs attention" : IsUpdate ? "Will update" : "Ready";
    public string Details => string.Join("  ·  ", new[]
        {
            row.Sku is null ? null : $"SKU {row.Sku}",
            row.Barcode is null ? null : $"Barcode {row.Barcode}",
            row.CategoryName.Length > 0 ? row.CategoryName : null,
            $"{row.Unit}",
        }.Where(p => p is not null));

    public bool IsError => row.Status == ProductImportRowStatus.Error;
    public bool IsNew => row.Status == ProductImportRowStatus.New;
    public bool IsUpdate => row.Status == ProductImportRowStatus.Update;
    /// <summary>Presentation-only table state; it does not influence import validation or commit behavior.</summary>
    public string TableSurface => IsError ? "Error" : RowNumber % 2 == 0 ? "White" : "Stripe";
    public string ErrorSummary => row.ErrorSummary;

    /// <summary>An Update row means this CSV line matched an existing product by SKU or Barcode —
    /// the "duplicate of a product you already have" case that gets the Import-as-New action.</summary>
    public bool IsDuplicateOfExisting => IsUpdate;

    public bool ShowNewBadge => IsNew && !IsRemoved;
    public bool ShowUpdateBadge => IsUpdate && !IsRemoved;
    public bool ShowErrorBadge => IsError && !IsRemoved;
    public bool ShowActions => !IsRemoved;
    public double RowOpacity => IsRemoved ? 0.5 : 1.0;

    /// <summary>Populates the editor from this row's original cell values and expands it.</summary>
    public void BeginEdit()
    {
        EditFields.Clear();
        foreach (var column in columns)
        {
            row.RawFields.TryGetValue(column.FieldKey, out var value);
            EditFields.Add(new ProductImportFieldViewModel(column.FieldKey) { Label = column.CanonicalName, Value = value ?? string.Empty });
        }

        IsEditing = true;
    }

    public IReadOnlyDictionary<string, string> BuildUpdatedFields() =>
        EditFields.ToDictionary(f => f.Key, f => f.Value);
}

/// <summary>One editable cell in a row's inline "Fix" form.</summary>
public sealed partial class ProductImportFieldViewModel(string key) : ObservableObject
{
    public string Key { get; } = key;

    public required string Label { get; init; }

    [ObservableProperty]
    private string _value = string.Empty;
}
