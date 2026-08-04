using Kirana.Application.Abstractions;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Barcodes;

public sealed class BarcodeService(IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger)
    : IBarcodeService
{
    private const string InternalBarcodeSequenceKey = "InternalBarcode";

    /// <summary>GS1 "restricted circulation" prefix range (200-299) reserved for in-store/internal
    /// use, so internally generated codes can never collide with a real manufacturer EAN-13.</summary>
    private const string InternalBarcodePrefix = "20";

    private const int MaxGenerationAttempts = 5;
    private const int MaxBarcodeLength = 48;

    public BarcodeSymbology DetermineSymbology(string barcode) =>
        IsValidEan13(barcode) ? BarcodeSymbology.Ean13 : BarcodeSymbology.Code128;

    public bool IsValidEan13(string barcode) => Ean13.IsValid(barcode);

    public void ValidateFormat(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new ArgumentException("Barcode cannot be empty.", nameof(barcode));
        }

        if (barcode.Length > MaxBarcodeLength)
        {
            throw new ArgumentException($"Barcode cannot be longer than {MaxBarcodeLength} characters.", nameof(barcode));
        }

        foreach (var c in barcode)
        {
            if (c < 0x20 || c > 0x7E)
            {
                throw new ArgumentException("Barcode can only contain printable ASCII characters.", nameof(barcode));
            }
        }
    }

    public async Task EnsureAvailableAsync(string barcode, int? excludingProductId, CancellationToken cancellationToken = default)
    {
        var duplicate = await db.Products.AnyAsync(
            p => p.Barcode == barcode && p.Id != (excludingProductId ?? -1), cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException($"A product with barcode '{barcode}' already exists.");
        }
    }

    public async Task<string> GenerateInternalBarcodeAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var sequence = await sequenceGenerator.NextNumericAsync(InternalBarcodeSequenceKey, cancellationToken);
            var first12 = InternalBarcodePrefix + sequence.ToString().PadLeft(10, '0');
            var candidate = Ean13.BuildWithCheckDigit(first12);

            var inUse = await db.Products.AnyAsync(p => p.Barcode == candidate, cancellationToken);
            if (!inUse)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique internal barcode. Please try again.");
    }

    public async Task<Product> AssignBarcodeAsync(int productId, string? explicitBarcode, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        string newBarcode;
        if (string.IsNullOrWhiteSpace(explicitBarcode))
        {
            newBarcode = await GenerateInternalBarcodeAsync(cancellationToken);
        }
        else
        {
            newBarcode = explicitBarcode.Trim();
            ValidateFormat(newBarcode);
            await EnsureAvailableAsync(newBarcode, productId, cancellationToken);
        }

        var previousBarcode = product.Barcode;
        product.Barcode = newBarcode;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BarcodeAssigned", nameof(Product), product.Id.ToString(),
            previousValue: previousBarcode, newValue: newBarcode, cancellationToken: cancellationToken);

        return product;
    }
}
