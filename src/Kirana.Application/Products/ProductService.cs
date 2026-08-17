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
    IPermissionEnforcer permissionEnforcer, IProductPricingService pricingService)
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
            request.SellingPrice, request.WholesalePrice, request.GstRatePercent, request.MinimumStock, request.ReorderQuantity,
            request.ReplenishmentEnabled, request.PreferredSupplierId,
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
            // SellingPrice/WholesalePrice are NOT set here (Phase 15A): they are projections of the
            // ProductPrice rows, staged below through the pricing service so both stores are always
            // written together by one implementation.
            DefaultDiscountPercent = request.DefaultDiscountPercent,
            GstRatePercent = request.GstRatePercent,
            HsnCode = request.HsnCode,
            PricingType = request.PricingType,
            TracksBatches = request.TracksBatches,
            MinimumStock = request.MinimumStock,
            ReorderQuantity = request.ReorderQuantity,
            ReplenishmentEnabled = request.ReplenishmentEnabled,
            PreferredSupplierId = request.PreferredSupplierId,
            IsActive = true,
        };

        db.Products.Add(product);

        // Selling prices go through the pricing service so the ProductPrice rows and the projection
        // columns are written by one implementation. Staged (not saved) so they land in the same
        // single SaveChanges as the product itself — a product is never persisted without its
        // retail price, and a rejected price creates no product at all.
        pricingService.StagePrice(product, PriceLevel.Retail, request.SellingPrice);
        if (request.WholesalePrice is { } wholesale)
        {
            // Only when configured: a null wholesale means the level does not apply, which is not
            // the same as zero, so no row is created for it.
            pricingService.StagePrice(product, PriceLevel.Wholesale, wholesale);
        }

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

        // Prices included: they are staged below through the pricing service, which needs the
        // product's existing levels to tell a real change from a no-op.
        var product = await db.Products
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        var replenishmentEnabled = request.UpdateReplenishmentConfiguration
            ? request.ReplenishmentEnabled
            : product.ReplenishmentEnabled;
        var preferredSupplierId = request.UpdateReplenishmentConfiguration
            ? request.PreferredSupplierId
            : product.PreferredSupplierId;

        await ValidateAsync(request.Name, request.CategoryId, request.BrandId, request.PurchasePrice, request.Mrp,
            request.SellingPrice, request.WholesalePrice, request.GstRatePercent, request.MinimumStock, request.ReorderQuantity,
            replenishmentEnabled, preferredSupplierId,
            request.Sku, barcodes: [], request.Unit, request.PurchasePackUnit, request.PurchasePackSize,
            excludingProductId: productId, cancellationToken);

        // PriceModification now covers COST and MRP only. Selling levels (Retail/Wholesale) get
        // their own per-level PriceChanged event from the pricing service, so a selling-price change
        // is no longer recorded twice under two different names.
        var previousCostSummary = $"Purchase={product.PurchasePrice}, Mrp={product.Mrp}";
        var costOrMrpChanged = product.PurchasePrice != request.PurchasePrice
            || product.Mrp != request.Mrp;

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

        // Selling prices via the pricing service, which updates the ProductPrice rows and the
        // projection columns together and tells us exactly what changed. Staged, not saved, so it
        // commits in the same SaveChanges as the rest of this update — the two stores can never end
        // up out of step. A null wholesale withdraws the level rather than writing zero.
        var priceChanges = new List<PriceChange>();
        foreach (var staged in new[]
        {
            pricingService.StagePrice(product, PriceLevel.Retail, request.SellingPrice),
            pricingService.StagePrice(product, PriceLevel.Wholesale, request.WholesalePrice),
        })
        {
            if (staged is not null)
            {
                priceChanges.Add(staged);
            }
        }

        product.DefaultDiscountPercent = request.DefaultDiscountPercent;
        product.GstRatePercent = request.GstRatePercent;
        product.HsnCode = request.HsnCode;
        product.PricingType = request.PricingType;
        product.TracksBatches = request.TracksBatches;
        product.MinimumStock = request.MinimumStock;
        product.ReorderQuantity = request.ReorderQuantity;
        if (request.UpdateReplenishmentConfiguration)
        {
            product.ReplenishmentEnabled = request.ReplenishmentEnabled;
            product.PreferredSupplierId = request.PreferredSupplierId;
        }
        product.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ProductUpdated", nameof(Product), product.Id.ToString(),
            cancellationToken: cancellationToken);

        if (costOrMrpChanged)
        {
            await auditLogger.RecordAsync(
                request.PerformedByUserId, "PriceModification", nameof(Product), product.Id.ToString(),
                previousValue: previousCostSummary,
                newValue: $"Purchase={product.PurchasePrice}, Mrp={product.Mrp}",
                cancellationToken: cancellationToken);
        }

        // One entry per selling level that actually moved — never for a re-save of the same number.
        foreach (var change in priceChanges)
        {
            await pricingService.RecordPriceChangeAsync(product, change, request.PerformedByUserId, cancellationToken);
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
            .Include(p => p.PreferredSupplier)
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
        decimal? wholesalePrice, decimal? gstRatePercent, decimal minimumStock, decimal reorderQuantity,
        bool replenishmentEnabled, int? preferredSupplierId, string? sku,
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

        // Wholesale was never validated before Phase 15A — a negative wholesale price was silently
        // accepted and persisted. Checked here, before anything is created, so a rejected price
        // leaves no product, no ProductPrice row and no audit entry behind.
        if (wholesalePrice is { } wholesale && wholesale < 0)
        {
            throw new ArgumentException("Prices cannot be negative.");
        }

        if (gstRatePercent is { } gstRate && !GstRatePolicy.IsSupported(gstRate))
            throw new ArgumentException("GST rate must be one of 0%, 5%, 12%, 18%, or 28%.");


        if (minimumStock < 0 || reorderQuantity < 0)
        {
            throw new ArgumentException("Minimum stock and reorder quantity cannot be negative.");
        }

        if (replenishmentEnabled && reorderQuantity < minimumStock)
        {
            throw new ArgumentException("Target stock must be greater than or equal to reorder level.");
        }

        if (!unit.SupportsDecimalQuantity()
            && (minimumStock != decimal.Truncate(minimumStock) || reorderQuantity != decimal.Truncate(reorderQuantity)))
        {
            throw new ArgumentException($"'{unit}' replenishment quantities must be whole numbers.");
        }

        if (preferredSupplierId is { } supplierId
            && !await db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, cancellationToken))
        {
            throw new InvalidOperationException("Selected preferred supplier does not exist or is inactive.");
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
