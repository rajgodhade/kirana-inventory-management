using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Kirana.Application.Taxation;

namespace Kirana.Application.Products;

public sealed class ProductService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IBarcodeService barcodeService,
    IPermissionEnforcer permissionEnforcer)
    : IProductService
{
    private const string ProductSequenceKey = "Product";
    private const string ProductCodePrefix = "PRD";
    private const int ProductCodePadding = 6;

    public async Task<Product> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.ProductsEdit, cancellationToken);
        EnsurePricingType(request.PricingType);

        await ValidateAsync(request.Name, request.CategoryId, request.BrandId, request.PurchasePrice, request.Mrp,
            request.SellingPrice, request.GstRatePercent, request.MinimumStock, request.ReorderQuantity,
            request.Sku, request.Barcodes, request.Unit, request.PurchasePackUnit, request.PurchasePackSize,
            excludingProductId: null, cancellationToken);

        var productCode = await sequenceGenerator.NextAsync(ProductSequenceKey, ProductCodePrefix, ProductCodePadding, cancellationToken);

        var product = new Product
        {
            ProductCode = productCode,
            Name = request.Name.Trim(),
            Sku = Normalize(request.Sku),
            Description = request.Description,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Unit = request.Unit,
            PurchasePackUnit = request.PurchasePackUnit,
            PurchasePackSize = request.PurchasePackSize,
            UnitDisplayText = Normalize(request.UnitDisplayText),
            PurchasePrice = request.PurchasePrice,
            Mrp = request.Mrp,
            SellingPrice = request.SellingPrice,
            WholesalePrice = request.WholesalePrice,
            DefaultDiscountPercent = request.DefaultDiscountPercent,
            GstRatePercent = request.GstRatePercent,
            HsnCode = request.HsnCode,
            PricingType = request.PricingType,
            TracksBatches = request.TracksBatches,
            MinimumStock = request.MinimumStock,
            ReorderQuantity = request.ReorderQuantity,
            IsActive = true,
        };

        db.Products.Add(product);

        // Staged with the Product navigation rather than a ProductId — the identity value doesn't
        // exist until SaveChanges, same as the Inventory/StockMovement rows below. They all commit
        // in the one save at the end, so a product is never persisted without its barcodes.
        for (var index = 0; index < request.Barcodes.Count; index++)
        {
            var value = request.Barcodes[index].Trim();
            db.ProductBarcodes.Add(new ProductBarcode
            {
                Product = product,
                Value = value,
                NormalizedValue = BarcodeNormalizer.Normalize(value),
                Symbology = barcodeService.DetermineSymbology(value),
                IsPrimary = index == request.PrimaryBarcodeIndex,
                IsActive = true,
            });
        }

        var inventory = new Inventory { Product = product, QuantityOnHand = request.OpeningStock };
        db.Inventories.Add(inventory);

        if (request.OpeningStock != 0)
        {
            db.StockMovements.Add(new StockMovement
            {
                Product = product,
                MovementType = StockMovementType.OpeningStock,
                QuantityChange = request.OpeningStock,
                PreviousQuantity = 0,
                NewQuantity = request.OpeningStock,
                UserId = request.PerformedByUserId,
                Reason = "Opening stock at product creation",
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ProductCreated", nameof(Product), product.Id.ToString(),
            newValue: $"{product.ProductCode} - {product.Name}", cancellationToken: cancellationToken);

        return product;
    }

    public async Task<Product> UpdateAsync(int productId, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.ProductsEdit, cancellationToken);
        EnsurePricingType(request.PricingType);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        await ValidateAsync(request.Name, request.CategoryId, request.BrandId, request.PurchasePrice, request.Mrp,
            request.SellingPrice, request.GstRatePercent, request.MinimumStock, request.ReorderQuantity,
            request.Sku, barcodes: [], request.Unit, request.PurchasePackUnit, request.PurchasePackSize,
            excludingProductId: productId, cancellationToken);

        var previousPricingSummary = $"Purchase={product.PurchasePrice}, Mrp={product.Mrp}, Selling={product.SellingPrice}";
        var pricingChanged = product.PurchasePrice != request.PurchasePrice
            || product.Mrp != request.Mrp
            || product.SellingPrice != request.SellingPrice;

        product.Name = request.Name.Trim();
        product.Sku = Normalize(request.Sku);
        // Barcodes are deliberately not touched here — see UpdateProductRequest.
        product.Description = request.Description;
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.Unit = request.Unit;
        product.PurchasePackUnit = request.PurchasePackUnit;
        product.PurchasePackSize = request.PurchasePackSize;
        product.UnitDisplayText = Normalize(request.UnitDisplayText);
        product.PurchasePrice = request.PurchasePrice;
        product.Mrp = request.Mrp;
        product.SellingPrice = request.SellingPrice;
        product.WholesalePrice = request.WholesalePrice;
        product.DefaultDiscountPercent = request.DefaultDiscountPercent;
        product.GstRatePercent = request.GstRatePercent;
        product.HsnCode = request.HsnCode;
        product.PricingType = request.PricingType;
        product.TracksBatches = request.TracksBatches;
        product.MinimumStock = request.MinimumStock;
        product.ReorderQuantity = request.ReorderQuantity;
        product.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ProductUpdated", nameof(Product), product.Id.ToString(),
            cancellationToken: cancellationToken);

        if (pricingChanged)
        {
            await auditLogger.RecordAsync(
                request.PerformedByUserId, "PriceModification", nameof(Product), product.Id.ToString(),
                previousValue: previousPricingSummary,
                newValue: $"Purchase={product.PurchasePrice}, Mrp={product.Mrp}, Selling={product.SellingPrice}",
                cancellationToken: cancellationToken);
        }

        return product;
    }

    public async Task SetActiveAsync(int productId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        if (product.IsActive == isActive)
        {
            return;
        }

        product.IsActive = isActive;
        product.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, isActive ? "ProductReactivated" : "ProductDeactivated", nameof(Product), product.Id.ToString(),
            cancellationToken: cancellationToken);
    }

    public Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default) =>
        db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Inventory)
            .Include(p => p.Barcodes)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

    public async Task<IReadOnlyList<Product>> SearchAsync(ProductSearchQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Product> Filtered(IQueryable<Product> source)
        {
            if (!query.IncludeInactive)
            {
                source = source.Where(p => p.IsActive);
            }

            if (query.CategoryId is { } categoryId)
            {
                source = source.Where(p => p.CategoryId == categoryId);
            }

            if (query.BrandId is { } brandId)
            {
                source = source.Where(p => p.BrandId == brandId);
            }

            return source;
        }

        var baseQuery = Filtered(
            db.Products.Include(p => p.Category).Include(p => p.Brand).Include(p => p.Inventory)
                .Include(p => p.Barcodes));

        var text = query.SearchText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return await baseQuery.OrderBy(p => p.Name).Take(query.MaxResults).ToListAsync(cancellationToken);
        }

        var results = new List<Product>();
        var seenIds = new HashSet<int>();

        void AddRange(IEnumerable<Product> products)
        {
            foreach (var product in products)
            {
                if (seenIds.Add(product.Id))
                {
                    results.Add(product);
                }
            }
        }

        // 1. Exact barcode match. Compared on the normalized value so casing never changes the
        //    result, and restricted to active barcodes so a retired code can't push a product to
        //    the top of the list.
        var normalizedText = BarcodeNormalizer.Normalize(text);
        AddRange(await baseQuery
            .Where(p => p.Barcodes.Any(b => b.NormalizedValue == normalizedText && b.IsActive))
            .ToListAsync(cancellationToken));

        // 2. Exact Product ID / SKU match.
        if (results.Count < query.MaxResults)
        {
            AddRange(await baseQuery.Where(p => p.ProductCode == text || p.Sku == text).ToListAsync(cancellationToken));
        }

        // 3. Partial match on name / category / brand / SKU / barcode / code.
        if (results.Count < query.MaxResults)
        {
            var likeText = $"%{text}%";
            var partialMatches = await baseQuery
                .Where(p =>
                    EF.Functions.Like(p.Name, likeText) ||
                    (p.Sku != null && EF.Functions.Like(p.Sku, likeText)) ||
                    // Active only, like bucket 1: POS search feeds straight into the cart, so a
                    // retired code that still surfaced its product here would defeat retirement —
                    // the operator types the old code, presses Enter, and sells against it anyway.
                    p.Barcodes.Any(b => EF.Functions.Like(b.Value, likeText) && b.IsActive) ||
                    EF.Functions.Like(p.ProductCode, likeText) ||
                    (p.Category != null && EF.Functions.Like(p.Category.Name, likeText)) ||
                    (p.Brand != null && EF.Functions.Like(p.Brand.Name, likeText)))
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            AddRange(partialMatches);
        }

        return results.Take(query.MaxResults).ToList();
    }

    private async Task ValidateAsync(
        string name, int? categoryId, int? brandId, decimal purchasePrice, decimal mrp, decimal sellingPrice,
        decimal? gstRatePercent, decimal minimumStock, decimal reorderQuantity, string? sku,
        IReadOnlyList<string> barcodes,
        UnitOfMeasure unit, UnitOfMeasure? purchasePackUnit, decimal? purchasePackSize,
        int? excludingProductId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.");
        }

        if (purchasePrice < 0 || mrp < 0 || sellingPrice < 0)
        {
            throw new ArgumentException("Prices cannot be negative.");
        }

        if (gstRatePercent is { } gstRate && !GstRatePolicy.IsSupported(gstRate))
            throw new ArgumentException("GST rate must be one of 0%, 5%, 12%, 18%, or 28%.");


        if (minimumStock < 0 || reorderQuantity < 0)
        {
            throw new ArgumentException("Minimum stock and reorder quantity cannot be negative.");
        }

        if (!UnitConversion.IsValidPackConfiguration(purchasePackSize, purchasePackUnit, unit))
        {
            throw new ArgumentException(
                "Purchase pack unit and pack size must both be set (or both left empty), the pack size must be " +
                "greater than zero, and the pack unit must differ from the product's base unit.");
        }

        if (categoryId is { } catId && !await db.Categories.AnyAsync(c => c.Id == catId, cancellationToken))
        {
            throw new InvalidOperationException("Selected category does not exist.");
        }

        if (brandId is { } brId && !await db.Brands.AnyAsync(b => b.Id == brId, cancellationToken))
        {
            throw new InvalidOperationException("Selected brand does not exist.");
        }

        var normalizedSku = Normalize(sku);
        if (normalizedSku is not null)
        {
            var duplicate = await db.Products.AnyAsync(
                p => p.Sku == normalizedSku && p.Id != (excludingProductId ?? -1), cancellationToken);
            if (duplicate)
            {
                throw new InvalidOperationException($"A product with SKU '{normalizedSku}' already exists.");
            }
        }

        // Each barcode must be individually valid and globally unique, and the request must not
        // contain the same code twice — including differing only by case, since uniqueness is
        // enforced on the normalized value.
        var seenBarcodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawBarcode in barcodes)
        {
            var trimmed = Normalize(rawBarcode);
            if (trimmed is null)
            {
                continue;
            }

            barcodeService.ValidateFormat(trimmed);

            if (!seenBarcodes.Add(BarcodeNormalizer.Normalize(trimmed)))
            {
                throw new InvalidOperationException($"Barcode '{trimmed}' is listed more than once for this product.");
            }

            await barcodeService.EnsureAvailableAsync(trimmed, excludingProductId, cancellationToken);
        }
    }

    private static void EnsurePricingType(PricingType pricingType)
    {
        if (!Enum.IsDefined(pricingType))
            throw new ArgumentException("Pricing Type must be GST Inclusive or GST Exclusive.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
