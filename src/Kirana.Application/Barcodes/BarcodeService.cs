using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Barcodes;

public sealed class BarcodeService(
    IKiranaDbContext db,
    ISequenceGenerator sequenceGenerator,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer)
    : IBarcodeService
{
    private const string InternalBarcodeSequenceKey = "InternalBarcode";

    /// <summary>GS1 "restricted circulation" prefix range (200-299) reserved for in-store/internal
    /// use, so internally generated codes can never collide with a real manufacturer EAN-13.</summary>
    private const string InternalBarcodePrefix = "20";

    private const int MaxGenerationAttempts = 5;

    // ------------------------------------------------------------------ pure helpers
    // No permission checks on these three: they're pure functions the Add/Edit Product dialog calls
    // on every keystroke to drive the live barcode preview, where an async permission round-trip
    // per character would be both slow and pointless.

    public BarcodeSymbology DetermineSymbology(string barcode) =>
        IsValidEan13(barcode) ? BarcodeSymbology.Ean13 : BarcodeSymbology.Code128;

    public bool IsValidEan13(string barcode) => Ean13.IsValid(barcode);

    public void ValidateFormat(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new ArgumentException("Barcode cannot be empty.", nameof(barcode));
        }

        if (barcode.Length > BarcodeNormalizer.MaxBarcodeLength)
        {
            throw new ArgumentException(
                $"Barcode cannot be longer than {BarcodeNormalizer.MaxBarcodeLength} characters.", nameof(barcode));
        }

        foreach (var c in barcode)
        {
            if (c < 0x20 || c > 0x7E)
            {
                throw new ArgumentException("Barcode can only contain printable ASCII characters.", nameof(barcode));
            }
        }
    }

    // ------------------------------------------------------------------ uniqueness

    public async Task EnsureAvailableAsync(string barcode, int? excludingProductId, CancellationToken cancellationToken = default)
    {
        var normalized = BarcodeNormalizer.Normalize(barcode);

        var duplicate = await db.ProductBarcodes.AnyAsync(
            b => b.NormalizedValue == normalized && b.ProductId != (excludingProductId ?? -1), cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException($"A product with barcode '{barcode}' already exists.");
        }
    }

    public async Task<int?> FindOwningProductIdAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var normalized = BarcodeNormalizer.Normalize(barcode);
        var owner = await db.ProductBarcodes
            .Where(b => b.NormalizedValue == normalized)
            .Select(b => (int?)b.ProductId)
            .FirstOrDefaultAsync(cancellationToken);

        return owner;
    }

    public async Task<string> GenerateInternalBarcodeAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var sequence = await sequenceGenerator.NextNumericAsync(InternalBarcodeSequenceKey, cancellationToken);
            var first12 = InternalBarcodePrefix + sequence.ToString().PadLeft(10, '0');
            var candidate = Ean13.BuildWithCheckDigit(first12);
            var normalized = BarcodeNormalizer.Normalize(candidate);

            var inUse = await db.ProductBarcodes.AnyAsync(b => b.NormalizedValue == normalized, cancellationToken);
            if (!inUse)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique internal barcode. Please try again.");
    }

    // ------------------------------------------------------------------ reads

    public async Task<IReadOnlyList<ProductBarcode>> GetForProductAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.ProductBarcodes
            .Where(b => b.ProductId == productId)
            .OrderByDescending(b => b.IsActive)
            .ThenByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

    // ------------------------------------------------------------------ mutations

    public async Task<ProductBarcode> AddBarcodeAsync(
        int productId, string? value, bool makePrimary, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        var isInternal = string.IsNullOrWhiteSpace(value);
        string newValue;
        if (isInternal)
        {
            newValue = await GenerateInternalBarcodeAsync(cancellationToken);
        }
        else
        {
            newValue = value!.Trim();
            ValidateFormat(newValue);
            await EnsureAvailableAsync(newValue, productId, cancellationToken);
        }

        var normalized = BarcodeNormalizer.Normalize(newValue);
        var siblings = await db.ProductBarcodes.Where(b => b.ProductId == productId).ToListAsync(cancellationToken);

        if (siblings.Any(b => b.NormalizedValue == normalized))
        {
            throw new InvalidOperationException($"'{product.Name}' already has the barcode '{newValue}'.");
        }

        // A product's first barcode is always its primary, regardless of what the caller asked for —
        // that makes "has barcodes but no primary" unreachable through this path.
        var becomesPrimary = makePrimary || siblings.Count == 0;
        if (becomesPrimary)
        {
            foreach (var sibling in siblings)
            {
                sibling.IsPrimary = false;
            }
        }

        var barcode = new ProductBarcode
        {
            ProductId = productId,
            Value = newValue,
            NormalizedValue = normalized,
            Symbology = DetermineSymbology(newValue),
            IsPrimary = becomesPrimary,
            IsInternal = isInternal,
            IsActive = true,
        };

        db.ProductBarcodes.Add(barcode);
        product.UpdatedAtUtc = DateTime.UtcNow;

        // Safe as a single save: the promotion here is an INSERT and the demotions are UPDATEs, and
        // EF issues UPDATEs before INSERTs — so the old primary is always cleared before the new row
        // claims the flag. (Two UPDATEs would NOT be safe; see SetPrimaryAsync.)
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, isInternal ? "BarcodeGenerated" : "BarcodeAdded", nameof(Product), productId.ToString(),
            newValue: newValue, cancellationToken: cancellationToken);

        return barcode;
    }

    public async Task<ProductBarcode> UpdateBarcodeValueAsync(
        int barcodeId, string newValue, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var barcode = await db.ProductBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId, cancellationToken)
            ?? throw new InvalidOperationException("Barcode not found.");

        var trimmed = newValue.Trim();
        ValidateFormat(trimmed);
        await EnsureAvailableAsync(trimmed, barcode.ProductId, cancellationToken);

        var previousValue = barcode.Value;
        barcode.Value = trimmed;
        barcode.NormalizedValue = BarcodeNormalizer.Normalize(trimmed);
        barcode.Symbology = DetermineSymbology(trimmed);
        barcode.UpdatedAtUtc = DateTime.UtcNow;

        await TouchProductAsync(barcode.ProductId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BarcodeChanged", nameof(Product), barcode.ProductId.ToString(),
            previousValue: previousValue, newValue: trimmed, cancellationToken: cancellationToken);

        return barcode;
    }

    public async Task SetPrimaryAsync(int barcodeId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var barcode = await db.ProductBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId, cancellationToken)
            ?? throw new InvalidOperationException("Barcode not found.");

        if (!barcode.IsActive)
        {
            throw new InvalidOperationException(
                $"'{barcode.Value}' is retired and cannot be made the primary barcode. Reactivate it first.");
        }

        var siblings = await db.ProductBarcodes
            .Where(b => b.ProductId == barcode.ProductId)
            .ToListAsync(cancellationToken);

        var previousPrimary = siblings.FirstOrDefault(b => b.IsPrimary && b.Id != barcodeId);

        // Demote and promote are two separate saves, in that order. SQLite evaluates the filtered
        // unique index on IsPrimary per STATEMENT, and EF orders UPDATEs by primary key — so doing
        // both at once fails whenever the new primary has the lower Id. Splitting the save leaves a
        // brief zero-primary window instead of a hard failure; zero primaries is recoverable and
        // invisible to billing (lookup never filters on IsPrimary), two primaries is not.
        foreach (var sibling in siblings.Where(b => b.Id != barcodeId))
        {
            sibling.IsPrimary = false;
        }

        await TouchProductAsync(barcode.ProductId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        barcode.IsPrimary = true;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BarcodePrimaryChanged", nameof(Product), barcode.ProductId.ToString(),
            previousValue: previousPrimary?.Value, newValue: barcode.Value, cancellationToken: cancellationToken);
    }

    public async Task SetBarcodeActiveAsync(
        int barcodeId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var barcode = await db.ProductBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId, cancellationToken)
            ?? throw new InvalidOperationException("Barcode not found.");

        if (barcode.IsActive == isActive)
        {
            return;
        }

        if (isActive)
        {
            // Uniqueness is enforced across active AND retired rows, so a straight reactivation can
            // never collide — but re-check anyway so the rule lives in exactly one place.
            await EnsureAvailableAsync(barcode.Value, barcode.ProductId, cancellationToken);

            // A retired row KEEPS IsPrimary when it was the product's only barcode (see below), so a
            // row can come back carrying a stale primary flag that another barcode has since taken.
            // Without this, reactivation produces two primaries and the filtered unique index rejects
            // the save. Promotion stays an explicit act: reactivating restores the code, not its rank.
            if (barcode.IsPrimary && await db.ProductBarcodes.AnyAsync(
                    b => b.ProductId == barcode.ProductId && b.Id != barcodeId && b.IsPrimary, cancellationToken))
            {
                barcode.IsPrimary = false;
            }
        }

        barcode.IsActive = isActive;
        barcode.UpdatedAtUtc = DateTime.UtcNow;

        ProductBarcode? promoted = null;
        if (!isActive && barcode.IsPrimary)
        {
            // Retiring the primary auto-promotes the oldest remaining active barcode rather than
            // forcing the operator through a second "now pick a new primary" step — the common case
            // is a supplier changing a pack code. When nothing else is active the retired row keeps
            // the flag, so a product with barcodes always has exactly one primary.
            promoted = await db.ProductBarcodes
                .Where(b => b.ProductId == barcode.ProductId && b.Id != barcodeId && b.IsActive)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (promoted is not null)
            {
                barcode.IsPrimary = false;
            }
        }

        await TouchProductAsync(barcode.ProductId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (promoted is not null)
        {
            // Promoted in a SEPARATE save, after the demote has committed. SQLite evaluates the
            // filtered unique index on IsPrimary per STATEMENT, and EF orders its UPDATEs by primary
            // key — so batching both changes fails whenever the promoted row has the lower Id, which
            // is exactly the common case here (the oldest remaining barcode is the one promoted).
            promoted.IsPrimary = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditLogger.RecordAsync(
            performedByUserId, isActive ? "BarcodeReactivated" : "BarcodeDeactivated", nameof(Product),
            barcode.ProductId.ToString(),
            previousValue: isActive ? null : barcode.Value,
            newValue: isActive ? barcode.Value : null,
            cancellationToken: cancellationToken);

        if (promoted is not null)
        {
            await auditLogger.RecordAsync(
                performedByUserId, "BarcodePrimaryChanged", nameof(Product), barcode.ProductId.ToString(),
                previousValue: barcode.Value, newValue: promoted.Value, cancellationToken: cancellationToken);
        }
    }

    public async Task RemoveBarcodeAsync(int barcodeId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var barcode = await db.ProductBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId, cancellationToken)
            ?? throw new InvalidOperationException("Barcode not found.");

        var productId = barcode.ProductId;
        var removedValue = barcode.Value;
        var wasPrimary = barcode.IsPrimary;

        db.ProductBarcodes.Remove(barcode);

        ProductBarcode? promoted = null;
        if (wasPrimary)
        {
            promoted = await db.ProductBarcodes
                .Where(b => b.ProductId == productId && b.Id != barcodeId && b.IsActive)
                .OrderBy(b => b.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (promoted is not null)
            {
                promoted.IsPrimary = true;
            }
        }

        await TouchProductAsync(productId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BarcodeRemoved", nameof(Product), productId.ToString(),
            previousValue: removedValue, cancellationToken: cancellationToken);

        if (promoted is not null)
        {
            await auditLogger.RecordAsync(
                performedByUserId, "BarcodePrimaryChanged", nameof(Product), productId.ToString(),
                previousValue: removedValue, newValue: promoted.Value, cancellationToken: cancellationToken);
        }
    }

    public async Task<ProductBarcode> AssignBarcodeAsync(
        int productId, string? explicitBarcode, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var barcode = await AddBarcodeAsync(productId, explicitBarcode, makePrimary: true, performedByUserId, cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BarcodeAssigned", nameof(Product), productId.ToString(),
            newValue: barcode.Value, cancellationToken: cancellationToken);

        return barcode;
    }

    private async Task TouchProductAsync(int productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is not null)
        {
            product.UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
