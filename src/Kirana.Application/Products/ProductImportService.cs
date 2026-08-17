using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Kirana.Application.Taxation;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Products;

public sealed class ProductImportService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger,
    IBarcodeService barcodeService, IPermissionEnforcer permissionEnforcer,
    IProductPricingService pricingService)
    : IProductImportService
{
    private const string ProductSequenceKey = "Product";
    private const string ProductCodePrefix = "PRD";
    private const int ProductCodePadding = 6;

    /// <summary>Separates multiple barcodes in one cell (Phase 13B). A pipe never appears in a
    /// retail EAN/UPC/Code128 value and needs no CSV quoting, unlike a comma — and it matches what
    /// <c>DataExportService</c> emits, so an export round-trips back through this importer.</summary>
    private const char BarcodeSeparator = '|';

    /// <summary>Prefix marking which barcode in the cell is the primary, e.g. <c>123|*456</c>.
    /// Stripped before validation and storage. Without it the first value wins.</summary>
    private const char PrimaryMarker = '*';

    /// <summary>Accepted spelling(s) per column, already header-normalized (lowercase alphanumerics
    /// only). The first entry is the canonical name written into the template and the field key
    /// used in <see cref="ProductImportRow.RawFields"/> — by construction it always equals
    /// <see cref="ProductImportParser.NormalizeHeader"/> of the canonical name.</summary>
    private static readonly (string Canonical, string[] Aliases)[] ColumnAliases =
    [
        ("Name", ["name", "productname", "product", "itemname"]),
        ("SKU", ["sku", "skucode"]),
        // Phase 13B: one cell can now carry several codes separated by BarcodeSeparator, optionally
        // marking one primary with a leading PrimaryMarker. The legacy "barcode"/"ean"/"upc"
        // spellings stay as aliases, so a file written before 13B (single value, no separator)
        // parses through this same path and simply yields a one-element list.
        ("Barcodes", ["barcodes", "barcode", "ean", "upc", "eans", "barcodelist"]),
        ("Description", ["description", "desc"]),
        ("Category", ["category", "categoryname"]),
        ("Brand", ["brand", "brandname"]),
        ("Unit", ["unit", "uom", "unitofmeasure"]),
        ("Purchase Pack Unit", ["purchasepackunit", "packunit", "uompack", "packunittype"]),
        ("Purchase Pack Size", ["purchasepacksize", "packsize"]),
        ("Unit Display Text", ["unitdisplaytext", "unitdisplay", "displayunit"]),
        ("Purchase Price", ["purchaseprice", "costprice", "cost", "purchase"]),
        ("MRP", ["mrp", "maximumretailprice"]),
        ("Selling Price", ["sellingprice", "saleprice", "rate", "price", "selling"]),
        ("Wholesale Price", ["wholesaleprice", "wholesale"]),
        ("Default Discount %", ["defaultdiscount", "defaultdiscountpercent", "discount", "discountpercent"]),
        ("GST %", ["gst", "gstpercent", "gstrate", "gstratepercent", "tax", "taxpercent"]),
        ("HSN Code", ["hsncode", "hsn"]),
        ("Tax Inclusive", ["taxinclusive", "istaxinclusive", "priceistaxinclusive", "inclusive"]),
        ("Pricing Type", ["pricingtype", "gstpricingtype", "taxpricingtype"]),
        ("Track Batches", ["trackbatches", "tracksbatches", "batch", "batches", "trackexpiry"]),
        ("Minimum Stock", ["minimumstock", "minstock", "minimum"]),
        ("Reorder Quantity", ["reorderquantity", "reorderqty", "reorder"]),
        ("Opening Stock", ["openingstock", "opening", "stock", "quantity", "qty"]),
    ];

    private static readonly (string Canonical, string[] Aliases)[] TemplateColumns =
        ColumnAliases.Where(c => c.Canonical != "Tax Inclusive").ToArray();

    public IReadOnlyList<ProductImportColumn> Columns { get; } =
        TemplateColumns.Select(c => new ProductImportColumn(c.Canonical, c.Aliases[0])).ToList();

    public async Task<ProductImportPreview> BuildPreviewAsync(
        Stream fileStream, string fileName, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var parsed = ProductImportParser.Parse(fileStream, fileName);
        if (parsed.FatalError is not null)
        {
            return new ProductImportPreview { FatalError = parsed.FatalError };
        }

        if (parsed.Rows.Count == 0)
        {
            return new ProductImportPreview { FatalError = "The file has a header row but no data rows." };
        }

        if (!parsed.Rows[0].Keys.Any(k => AliasesFor("Name").Contains(k)))
        {
            return new ProductImportPreview
            {
                FatalError = "No 'Name' column found. The first row must name each column — download the template to see the expected format.",
            };
        }

        return await BuildPreviewFromRawRowsAsync(parsed.Rows, cancellationToken);
    }

    public async Task<ProductImportPreview> ReviseRowAsync(
        ProductImportPreview preview, int rowNumber, IReadOnlyDictionary<string, string> updatedFields,
        int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        // Re-validate the whole row set, not just the edited row: fixing this row's SKU could newly
        // collide with (or newly free up) another row's SKU, and the category/brand "will be
        // created" lists depend on every row, not just this one.
        var rawRows = preview.Rows
            .OrderBy(r => r.RowNumber)
            .Select(r => r.RowNumber == rowNumber ? updatedFields : r.RawFields)
            .ToList();

        return await BuildPreviewFromRawRowsAsync(rawRows, cancellationToken);
    }

    private async Task<ProductImportPreview> BuildPreviewFromRawRowsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rawRows, CancellationToken cancellationToken)
    {
        // Snapshot the catalogue once rather than querying per row: an import is a few hundred rows
        // at most, and this keeps validation a pure in-memory pass.
        var existingProducts = await db.Products
            .Select(p => new { p.Id, p.Sku })
            .ToListAsync(cancellationToken);

        var bySku = existingProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
            .GroupBy(p => p.Sku!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Keyed on the already-normalized value, so an Ordinal comparer is correct here — the
        // normalization IS the case-insensitivity now (Phase 13B).
        var byBarcode = (await db.ProductBarcodes
                .Select(b => new { b.ProductId, b.NormalizedValue })
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.NormalizedValue, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ProductId, StringComparer.Ordinal);

        var categories = await db.Categories.Select(c => c.Name).ToListAsync(cancellationToken);
        var brands = await db.Brands.Select(b => b.Name).ToListAsync(cancellationToken);
        var knownCategories = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
        var knownBrands = new HashSet<string>(brands, StringComparer.OrdinalIgnoreCase);

        var newCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tracks values claimed by earlier rows in this same file, so two rows fighting over one SKU
        // is caught here rather than blowing up as a unique-constraint violation mid-commit.
        var seenSkus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Keyed on normalized barcode values (see byBarcode above).
        var seenBarcodes = new Dictionary<string, int>(StringComparer.Ordinal);

        var rows = new List<ProductImportRow>();

        for (var i = 0; i < rawRows.Count; i++)
        {
            var rowNumber = ProductImportParser.FirstDataRowNumber + i;
            rows.Add(BuildRow(
                rawRows[i], rowNumber, bySku, byBarcode, knownCategories, knownBrands,
                newCategories, newBrands, seenSkus, seenBarcodes));
        }

        return new ProductImportPreview
        {
            Rows = rows,
            NewCategoryNames = newCategories.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            NewBrandNames = newBrands.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private ProductImportRow BuildRow(
        IReadOnlyDictionary<string, string> raw, int rowNumber,
        Dictionary<string, int> bySku, Dictionary<string, int> byBarcode,
        HashSet<string> knownCategories, HashSet<string> knownBrands,
        HashSet<string> newCategories, HashSet<string> newBrands,
        Dictionary<string, int> seenSkus, Dictionary<string, int> seenBarcodes)
    {
        var errors = new List<string>();

        var name = Get(raw, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Name is required.");
        }

        var sku = NullIfBlank(Get(raw, "SKU"));
        var (barcodes, primaryBarcodeIndex) = ParseBarcodeCell(Get(raw, "Barcodes"), errors);

        var unit = UnitOfMeasure.Piece;
        var unitText = Get(raw, "Unit");
        if (!string.IsNullOrWhiteSpace(unitText) && !TryParseUnit(unitText, out unit))
        {
            errors.Add($"Unit '{unitText}' is not one of: {string.Join(", ", Enum.GetNames<UnitOfMeasure>())}.");
        }

        // Optional purchase pack (Phase 13A) — a blank cell means "not provided" (not an error),
        // matching how every other optional column in this import behaves.
        UnitOfMeasure? purchasePackUnit = null;
        var purchasePackUnitText = Get(raw, "Purchase Pack Unit");
        if (!string.IsNullOrWhiteSpace(purchasePackUnitText))
        {
            if (TryParseUnit(purchasePackUnitText, out var parsedPackUnit))
            {
                purchasePackUnit = parsedPackUnit;
            }
            else
            {
                errors.Add($"Purchase Pack Unit '{purchasePackUnitText}' is not one of: {string.Join(", ", Enum.GetNames<UnitOfMeasure>())}.");
            }
        }

        var purchasePackSize = ParseOptionalDecimal(raw, "Purchase Pack Size", errors);
        var unitDisplayText = NullIfBlank(Get(raw, "Unit Display Text"));

        if (errors.Count == 0 && !UnitConversion.IsValidPackConfiguration(purchasePackSize, purchasePackUnit, unit))
        {
            errors.Add(
                "Purchase Pack Unit and Purchase Pack Size must both be provided (or both left blank), the pack " +
                "size must be greater than zero, and the pack unit must differ from Unit.");
        }

        var purchasePrice = ParseDecimal(raw, "Purchase Price", errors, out _);
        var mrp = ParseDecimal(raw, "MRP", errors, out _);
        var sellingPrice = ParseDecimal(raw, "Selling Price", errors, out var sellingProvided);
        var wholesale = ParseOptionalDecimal(raw, "Wholesale Price", errors);
        var defaultDiscount = ParseOptionalDecimal(raw, "Default Discount %", errors);
        var gst = ParseOptionalDecimal(raw, "GST %", errors);
        var pricingType = ParsePricingType(Get(raw, "Pricing Type"), Get(raw, "Tax Inclusive"), errors);
        var minimumStock = ParseDecimal(raw, "Minimum Stock", errors, out _);
        var reorderQuantity = ParseDecimal(raw, "Reorder Quantity", errors, out _);
        var openingStock = ParseDecimal(raw, "Opening Stock", errors, out _);

        if (!sellingProvided)
        {
            errors.Add("Selling Price is required.");
        }

        if (purchasePrice < 0 || mrp < 0 || sellingPrice < 0)
        {
            errors.Add("Prices cannot be negative.");
        }

        if (gst is { } gstRate && !GstRatePolicy.IsSupported(gstRate))
            errors.Add("GST % must be one of 0, 5, 12, 18, or 28.");

        if (defaultDiscount is < 0 or > 100)
        {
            errors.Add("Default Discount % must be between 0 and 100.");
        }

        if (minimumStock < 0 || reorderQuantity < 0 || openingStock < 0)
        {
            errors.Add("Stock quantities cannot be negative.");
        }

        // Same whole-unit rule the Add/Edit Product form and SaleService enforce: a product sold in
        // whole Pieces can never actually reach a fractional stock level.
        if (!unit.SupportsDecimalQuantity()
            && (HasFraction(minimumStock) || HasFraction(reorderQuantity) || HasFraction(openingStock)))
        {
            errors.Add($"'{unit}' is a whole-unit measure — stock quantities must be whole numbers.");
        }

        foreach (var value in barcodes)
        {
            try
            {
                barcodeService.ValidateFormat(value);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Barcode '{value}': {ex.Message}");
            }
        }

        if (sku is not null && seenSkus.TryGetValue(sku, out var skuRow))
        {
            errors.Add($"SKU '{sku}' is already used by row {skuRow} in this file.");
        }

        foreach (var value in barcodes)
        {
            if (seenBarcodes.TryGetValue(BarcodeNormalizer.Normalize(value), out var barcodeRow))
            {
                errors.Add($"Barcode '{value}' is already used by row {barcodeRow} in this file.");
            }
        }

        // A row whose barcodes point at two different existing products is genuinely ambiguous —
        // report it rather than silently picking one and quietly re-pointing a code.
        var matchedByBarcode = barcodes
            .Select(v => byBarcode.TryGetValue(BarcodeNormalizer.Normalize(v), out var id) ? id : (int?)null)
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        if (matchedByBarcode.Count > 1)
        {
            errors.Add("The barcodes on this row belong to two different existing products.");
        }

        // SKU wins over barcode when both match (see matchedId below), so a row can name one product
        // by SKU while carrying a barcode that belongs to a different one. Caught here rather than
        // left to the database: the whole import shares a single SaveChangesAsync, so hitting the
        // unique index would roll back every other valid row in the file instead of flagging one.
        if (matchedByBarcode.Count == 1 && sku is not null
            && bySku.TryGetValue(sku, out var skuOwnerId) && skuOwnerId != matchedByBarcode[0])
        {
            errors.Add("A barcode on this row already belongs to a different product.");
        }

        if (errors.Count > 0)
        {
            return new ProductImportRow
            {
                RowNumber = rowNumber,
                Status = ProductImportRowStatus.Error,
                Errors = errors,
                Name = name,
                Sku = sku,
                Barcodes = barcodes,
                RawFields = raw,
            };
        }

        // Only claim SKU/barcode once the row is known good, so an error row can't block a later
        // valid one from using the same value.
        if (sku is not null)
        {
            seenSkus[sku] = rowNumber;
        }

        foreach (var value in barcodes)
        {
            seenBarcodes[BarcodeNormalizer.Normalize(value)] = rowNumber;
        }

        // Match an existing product so re-importing a corrected file updates in place instead of
        // failing on duplicate SKU/barcode. SKU wins over barcode when both are present.
        int? matchedId = null;
        if (sku is not null && bySku.TryGetValue(sku, out var idBySku))
        {
            matchedId = idBySku;
        }
        else if (matchedByBarcode.Count == 1)
        {
            matchedId = matchedByBarcode[0];
        }

        var categoryName = Get(raw, "Category").Trim();
        var brandName = Get(raw, "Brand").Trim();

        if (categoryName.Length > 0 && !knownCategories.Contains(categoryName))
        {
            newCategories.Add(categoryName);
        }

        if (brandName.Length > 0 && !knownBrands.Contains(brandName))
        {
            newBrands.Add(brandName);
        }

        return new ProductImportRow
        {
            RowNumber = rowNumber,
            Status = matchedId is null ? ProductImportRowStatus.New : ProductImportRowStatus.Update,
            Name = name.Trim(),
            Sku = sku,
            Barcodes = barcodes,
            CategoryName = categoryName,
            BrandName = brandName,
            Unit = unit,
            PurchasePackUnit = purchasePackUnit,
            PurchasePackSize = purchasePackSize,
            UnitDisplayText = unitDisplayText,
            SellingPrice = sellingPrice,
            OpeningStock = openingStock,
            MatchedProductId = matchedId,
            RawFields = raw,
            Request = new CreateProductRequest
            {
                Name = name.Trim(),
                Sku = sku,
                Barcodes = barcodes,
                PrimaryBarcodeIndex = primaryBarcodeIndex,
                Description = NullIfBlank(Get(raw, "Description")),
                Unit = unit,
                PurchasePackUnit = purchasePackUnit,
                PurchasePackSize = purchasePackSize,
                UnitDisplayText = unitDisplayText,
                PurchasePrice = purchasePrice,
                Mrp = mrp,
                SellingPrice = sellingPrice,
                WholesalePrice = wholesale,
                DefaultDiscountPercent = defaultDiscount,
                GstRatePercent = gst,
                HsnCode = NullIfBlank(Get(raw, "HSN Code")),
                PricingType = pricingType,
                TracksBatches = ParseBool(Get(raw, "Track Batches")),
                MinimumStock = minimumStock,
                ReorderQuantity = reorderQuantity,
                OpeningStock = openingStock,
            },
        };
    }

    public async Task<ProductImportResult> CommitAsync(
        ProductImportPreview preview, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var importable = preview.Rows.Where(r => r.Status != ProductImportRowStatus.Error && r.Request is not null).ToList();
        if (importable.Count == 0)
        {
            return new ProductImportResult { SkippedErrorCount = preview.ErrorCount };
        }

        // Product codes must be reserved up front: the sequence generator saves on every call, so
        // interleaving it with staged product entities would flush a partial import to disk and
        // defeat the single-SaveChanges atomicity everything below relies on.
        var newRowCount = importable.Count(r => r.Status == ProductImportRowStatus.New);
        var reservedCodes = new Queue<string>();
        for (var i = 0; i < newRowCount; i++)
        {
            reservedCodes.Enqueue(await sequenceGenerator.NextAsync(
                ProductSequenceKey, ProductCodePrefix, ProductCodePadding, cancellationToken));
        }

        var categoryIds = await ResolveLookupAsync(preview.NewCategoryNames, isCategory: true, cancellationToken);
        var brandIds = await ResolveLookupAsync(preview.NewBrandNames, isCategory: false, cancellationToken);

        var created = 0;
        var updated = 0;

        foreach (var row in importable)
        {
            var request = row.Request!;
            int? categoryId = row.CategoryName.Length > 0 && categoryIds.TryGetValue(row.CategoryName, out var cid) ? cid : null;
            int? brandId = row.BrandName.Length > 0 && brandIds.TryGetValue(row.BrandName, out var bid) ? bid : null;

            if (row.MatchedProductId is { } productId)
            {
                var product = await db.Products
                    .Include(p => p.Barcodes)
                    .Include(p => p.Prices)
                    .FirstAsync(p => p.Id == productId, cancellationToken);
                ApplyTo(product, request, categoryId, brandId);
                ReconcileBarcodes(product, request);
                StagePrices(product, request);
                product.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
                continue;
            }

            var newProduct = new Product { ProductCode = reservedCodes.Dequeue(), IsActive = true };
            ApplyTo(newProduct, request, categoryId, brandId);
            ReconcileBarcodes(newProduct, request);
            db.Products.Add(newProduct);
            StagePrices(newProduct, request);
            db.Inventories.Add(new Inventory { Product = newProduct, QuantityOnHand = request.OpeningStock });

            if (request.OpeningStock != 0)
            {
                db.StockMovements.Add(new StockMovement
                {
                    Product = newProduct,
                    MovementType = StockMovementType.OpeningStock,
                    QuantityChange = request.OpeningStock,
                    PreviousQuantity = 0,
                    NewQuantity = request.OpeningStock,
                    UserId = performedByUserId,
                    Reason = "Opening stock from bulk import",
                });
            }

            created++;
        }

        // One save for every product, inventory row and stock movement in the import.
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "ProductsImported", nameof(Product), entityId: null,
            newValue: $"{created} created, {updated} updated, {preview.ErrorCount} skipped",
            cancellationToken: cancellationToken);

        return new ProductImportResult
        {
            CreatedCount = created,
            UpdatedCount = updated,
            SkippedErrorCount = preview.ErrorCount,
            CategoriesCreated = preview.NewCategoryNames.Count,
            BrandsCreated = preview.NewBrandNames.Count,
        };
    }

    /// <summary>Returns a name -> id map covering both existing rows and the ones this import needs
    /// to create. New rows are staged, not saved — they commit with everything else.</summary>
    private async Task<Dictionary<string, int>> ResolveLookupAsync(
        IReadOnlyList<string> namesToCreate, bool isCategory, CancellationToken cancellationToken)
    {
        var map = isCategory
            ? (await db.Categories.Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken))
                .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase)
            : (await db.Brands.Select(b => new { b.Id, b.Name }).ToListAsync(cancellationToken))
                .ToDictionary(b => b.Name, b => b.Id, StringComparer.OrdinalIgnoreCase);

        if (namesToCreate.Count == 0)
        {
            return map;
        }

        // Ids are database-generated, so they aren't known until SaveChanges. Save the new
        // categories/brands first — they are additive reference data, harmless on their own if the
        // product write below were to fail.
        var pending = new List<(string Name, object Entity)>();
        foreach (var name in namesToCreate)
        {
            if (map.ContainsKey(name))
            {
                continue;
            }

            if (isCategory)
            {
                var category = new Category { Name = name };
                db.Categories.Add(category);
                pending.Add((name, category));
            }
            else
            {
                var brand = new Brand { Name = name };
                db.Brands.Add(brand);
                pending.Add((name, brand));
            }
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            foreach (var (name, entity) in pending)
            {
                map[name] = entity is Category c ? c.Id : ((Brand)entity).Id;
            }
        }

        return map;
    }

    /// <summary>
    /// Stages the row's barcodes onto a product (Phase 13B). Deliberately ADDITIVE and never
    /// destructive: codes already on the product but missing from the file are left alone, because
    /// an import file is rarely the complete truth about a product and silently retiring a working
    /// shelf code would be real data loss. A code already present is skipped rather than duplicated.
    /// <para>Stages entities only — never saves. That keeps the whole import inside CommitAsync's
    /// single SaveChangesAsync, which is what makes a failed import all-or-nothing.</para>
    /// </summary>
    private void ReconcileBarcodes(Product product, CreateProductRequest request)
    {
        if (request.Barcodes.Count == 0)
        {
            return;
        }

        var existing = product.Barcodes
            .Select(b => b.NormalizedValue)
            .ToHashSet(StringComparer.Ordinal);

        // Only claim primary when the product has none yet — an import shouldn't silently demote a
        // primary the operator chose in the app.
        var hasPrimary = product.Barcodes.Any(b => b.IsPrimary);

        for (var index = 0; index < request.Barcodes.Count; index++)
        {
            var value = request.Barcodes[index].Trim();
            var normalized = BarcodeNormalizer.Normalize(value);

            if (!existing.Add(normalized))
            {
                continue;
            }

            var isPrimary = !hasPrimary && index == request.PrimaryBarcodeIndex;
            if (!hasPrimary && index == request.Barcodes.Count - 1 && !product.Barcodes.Any(b => b.IsPrimary))
            {
                // The designated primary was a duplicate that got skipped — fall back to this one so
                // a product never ends up with barcodes but no primary.
                isPrimary = true;
            }

            var barcode = new ProductBarcode
            {
                Product = product,
                Value = value,
                NormalizedValue = normalized,
                Symbology = barcodeService.DetermineSymbology(value),
                IsPrimary = isPrimary,
                IsActive = true,
            };

            if (isPrimary)
            {
                hasPrimary = true;
            }

            product.Barcodes.Add(barcode);
            db.ProductBarcodes.Add(barcode);
        }
    }

    /// <summary>
    /// Routes an imported row's selling prices through the pricing service, so an import writes the
    /// ProductPrice rows and the projection columns by the same implementation product creation
    /// uses — rather than being a second, weaker pricing path.
    ///
    /// <para>Stages only, never saves: the whole import shares one SaveChangesAsync, so a rejected
    /// row must not have already committed its prices. Mirrors ReconcileBarcodes.</para>
    /// </summary>
    private void StagePrices(Product product, CreateProductRequest request)
    {
        pricingService.StagePrice(product, PriceLevel.Retail, request.SellingPrice);

        // Only when the column was present and parsed: import is additive, so an omitted wholesale
        // column must not silently withdraw a price the product already has.
        if (request.WholesalePrice is { } wholesale)
        {
            pricingService.StagePrice(product, PriceLevel.Wholesale, wholesale);
        }
    }

    private static void ApplyTo(Product product, CreateProductRequest request, int? categoryId, int? brandId)
    {
        product.Name = request.Name;
        product.Sku = request.Sku;
        // Barcodes are a child collection now, so they can't be assigned here — this method is
        // static and synchronous by design. See ReconcileBarcodes, called from CommitAsync.
        product.Description = request.Description;
        product.CategoryId = categoryId;
        product.BrandId = brandId;
        product.Unit = request.Unit;
        product.PurchasePackUnit = request.PurchasePackUnit;
        product.PurchasePackSize = request.PurchasePackSize;
        product.UnitDisplayText = request.UnitDisplayText;
        product.PurchasePrice = request.PurchasePrice;
        product.Mrp = request.Mrp;
        // Selling prices are a child collection now, so they can't be assigned here — this method
        // is static and synchronous by design. See StagePrices, called from CommitAsync.
        product.DefaultDiscountPercent = request.DefaultDiscountPercent;
        product.GstRatePercent = request.GstRatePercent;
        product.HsnCode = request.HsnCode;
        product.PricingType = request.PricingType;
        product.TracksBatches = request.TracksBatches;
        product.MinimumStock = request.MinimumStock;
        product.ReorderQuantity = request.ReorderQuantity;
    }

    public string BuildCsvTemplate()
    {
        var header = string.Join(",", TemplateColumns.Select(c => Escape(c.Canonical)));
        // The Barcodes cell carries a worked example rather than a blank, so the separator and the
        // primary marker are self-documenting in the file an operator actually downloads.
        var example = string.Join(",", new[]
        {
            "Tata Salt 1kg", "TATA-SALT-1KG", "8901030811127|*8901030811134", "Iodised salt", "Grocery", "Tata", "Piece",
            "Box", "12", "",
            "18", "25", "22", "", "", "5", "25010010", "Inclusive", "No", "10", "20", "100",
        }.Select(Escape));

        return header + "\r\n" + example + "\r\n";
    }

    private static PricingType ParsePricingType(string? pricingTypeText, string? legacyTaxInclusiveText, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pricingTypeText))
        {
            // Backward compatible templates with the legacy column retain their explicit value;
            // files with neither column use the retail-safe Inclusive default.
            return string.IsNullOrWhiteSpace(legacyTaxInclusiveText) || ParseBool(legacyTaxInclusiveText)
                ? PricingType.Inclusive
                : PricingType.Exclusive;
        }

        if (pricingTypeText.Trim().Equals("Inclusive", StringComparison.OrdinalIgnoreCase)) return PricingType.Inclusive;
        if (pricingTypeText.Trim().Equals("Exclusive", StringComparison.OrdinalIgnoreCase)) return PricingType.Exclusive;
        errors.Add("Pricing Type must be Inclusive or Exclusive.");
        return PricingType.Inclusive;
    }

    // --- parsing helpers ---------------------------------------------------------------------

    private static string[] AliasesFor(string canonical) =>
        ColumnAliases.First(c => c.Canonical == canonical).Aliases;

    private static string Get(IReadOnlyDictionary<string, string> raw, string canonical)
    {
        foreach (var alias in AliasesFor(canonical))
        {
            if (raw.TryGetValue(alias, out var value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static decimal ParseDecimal(
        IReadOnlyDictionary<string, string> raw, string canonical, List<string> errors, out bool provided)
    {
        var text = Get(raw, canonical);
        provided = !string.IsNullOrWhiteSpace(text);
        if (!provided)
        {
            return 0;
        }

        if (decimal.TryParse(StripCurrency(text), out var value))
        {
            return value;
        }

        errors.Add($"{canonical} '{text}' is not a valid number.");
        provided = false;
        return 0;
    }

    private static decimal? ParseOptionalDecimal(
        IReadOnlyDictionary<string, string> raw, string canonical, List<string> errors)
    {
        var text = Get(raw, canonical);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(StripCurrency(text), out var value))
        {
            return value;
        }

        errors.Add($"{canonical} '{text}' is not a valid number.");
        return null;
    }

    /// <summary>Spreadsheets routinely carry "₹1,250.00" or "12%" in what should be a number column.</summary>
    private static string StripCurrency(string text) =>
        new(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());

    private static bool ParseBool(string text) =>
        text.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1";

    private static bool TryParseUnit(string text, out UnitOfMeasure unit) =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out unit) && Enum.IsDefined(unit);

    /// <summary>
    /// Splits a Barcodes cell into its individual codes and works out which one is primary
    /// (Phase 13B). Blank/empty segments are tolerated rather than reported, so "123||456" and a
    /// trailing "123|" both parse cleanly — a stray separator is a typo, not something worth
    /// failing an import row over. Marking two values primary IS an error, since silently picking
    /// one would quietly contradict what the file says.
    /// </summary>
    private static (IReadOnlyList<string> Barcodes, int PrimaryIndex) ParseBarcodeCell(string cell, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return ([], 0);
        }

        var values = new List<string>();
        var primaryIndex = 0;
        var markedCount = 0;

        foreach (var segment in cell.Split(BarcodeSeparator))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] == PrimaryMarker)
            {
                // Stripped before it ever reaches ValidateFormat — a starred code would otherwise
                // fail the printable-ASCII/format check on a character that isn't part of the code.
                trimmed = trimmed[1..].Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                markedCount++;
                primaryIndex = values.Count;
            }

            values.Add(trimmed);
        }

        if (markedCount > 1)
        {
            errors.Add($"Only one barcode may be marked primary with '{PrimaryMarker}'.");
        }

        return (values, primaryIndex);
    }

    private static bool HasFraction(decimal value) => value != Math.Floor(value);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
