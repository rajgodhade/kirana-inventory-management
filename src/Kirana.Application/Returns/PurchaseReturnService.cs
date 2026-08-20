using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Returns;

public sealed class PurchaseReturnService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : IPurchaseReturnService
{
    private const string ReturnSequenceKey = "PurchaseReturn";
    private const string ReturnPrefix = "PRN";
    private const int ReturnPadding = 6;
    private const decimal QuantityTolerance = 0.0005m;

    // ---------------------------------------------------------------- lookup

    public async Task<IReadOnlyList<ReturnablePurchase>> FindReturnablePurchasesAsync(
        string? searchText, int? performedByUserId, int maxResults = 25, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        IQueryable<Purchase> purchases = db.Purchases.AsNoTracking();

        var text = searchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var like = $"%{text}%";
            purchases = purchases.Where(p =>
                EF.Functions.Like(p.PurchaseNumber, like)
                || (p.SupplierInvoiceNumber != null && EF.Functions.Like(p.SupplierInvoiceNumber, like))
                || EF.Functions.Like(p.Supplier.Name, like)
                || EF.Functions.Like(p.Supplier.SupplierCode, like)
                || p.Items.Any(i =>
                    EF.Functions.Like(i.ProductNameSnapshot, like)
                    || EF.Functions.Like(i.ProductCodeSnapshot, like)));
        }

        var ids = await purchases
            .OrderByDescending(p => p.PurchaseDateUtc)
            .Take(maxResults)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var results = new List<ReturnablePurchase>();
        foreach (var id in ids)
        {
            if (await BuildReturnablePurchaseAsync(id, cancellationToken) is { } returnable)
            {
                results.Add(returnable);
            }
        }

        return results;
    }

    public async Task<ReturnablePurchase?> GetReturnablePurchaseAsync(
        int purchaseId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);
        return await BuildReturnablePurchaseAsync(purchaseId, cancellationToken);
    }

    private async Task<ReturnablePurchase?> BuildReturnablePurchaseAsync(int purchaseId, CancellationToken cancellationToken)
    {
        var purchase = await db.Purchases.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product).ThenInclude(pr => pr.Inventory)
            .FirstOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);

        if (purchase is null)
        {
            return null;
        }

        var returned = await GetReturnedQuantitiesAsync(purchase.Items.Select(i => i.Id).ToList(), cancellationToken);

        return new ReturnablePurchase
        {
            PurchaseId = purchase.Id,
            PurchaseNumber = purchase.PurchaseNumber,
            SupplierInvoiceNumber = purchase.SupplierInvoiceNumber,
            PurchaseDateUtc = purchase.PurchaseDateUtc,
            SupplierId = purchase.SupplierId,
            SupplierName = purchase.GstIdentitySnapshotCapturedAtUtc is not null
                ? purchase.SupplierNameSnapshot ?? "Supplier"
                : purchase.Supplier.Name,
            SupplierCode = purchase.GstIdentitySnapshotCapturedAtUtc is not null
                ? purchase.SupplierCodeSnapshot ?? string.Empty
                : purchase.Supplier.SupplierCode,
            GrandTotal = purchase.GrandTotal,
            Lines = purchase.Items.Select(i => new ReturnablePurchaseLine
            {
                PurchaseItemId = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                ProductCode = i.ProductCodeSnapshot,
                Unit = i.UnitSnapshot,
                TracksBatches = i.Product.TracksBatches,
                BatchNumber = i.BatchNumber,
                ReceivedQuantity = i.Quantity,
                AlreadyReturnedQuantity = returned.GetValueOrDefault(i.Id),
                StockOnHand = i.Product.Inventory?.QuantityOnHand ?? 0m,
                PurchasePrice = i.PurchasePriceSnapshot,
                LineTotal = i.LineTotal,
            }).ToList(),
        };
    }

    private async Task<Dictionary<int, decimal>> GetReturnedQuantitiesAsync(
        List<int> purchaseItemIds, CancellationToken cancellationToken) =>
        await db.PurchaseReturnItems.AsNoTracking()
            .Where(ri => purchaseItemIds.Contains(ri.PurchaseItemId))
            .GroupBy(ri => ri.PurchaseItemId)
            .Select(g => new { PurchaseItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.PurchaseItemId, x => x.Quantity, cancellationToken);

    // ---------------------------------------------------------------- process

    public async Task<PurchaseReturn> ProcessReturnAsync(CreatePurchaseReturnRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.ProcessedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A purchase return must have at least one line.", nameof(request));
        }

        var purchase = await db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product).ThenInclude(pr => pr.Inventory)
            .Include(p => p.Items).ThenInclude(i => i.Product).ThenInclude(pr => pr.Batches)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase not found.");

        var alreadyReturned = await GetReturnedQuantitiesAsync(purchase.Items.Select(i => i.Id).ToList(), cancellationToken);

        // Validate the whole request first — including that the shop still physically has the
        // stock. Returning goods it has already sold on would drive inventory negative.
        var plannedByLine = new Dictionary<int, decimal>();
        var plannedByProduct = new Dictionary<int, decimal>();

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Return quantity must be greater than zero.", nameof(request));
            }

            var item = purchase.Items.FirstOrDefault(i => i.Id == line.PurchaseItemId)
                ?? throw new InvalidOperationException($"Line #{line.PurchaseItemId} does not belong to purchase {purchase.PurchaseNumber}.");

            if (!item.Product.Unit.SupportsDecimalQuantity() && line.Quantity != Math.Floor(line.Quantity))
            {
                throw new ArgumentException(
                    $"'{item.ProductNameSnapshot}' is stocked in whole {item.UnitSnapshot} units — {line.Quantity} is not a valid return quantity.");
            }

            plannedByLine.TryGetValue(line.PurchaseItemId, out var plannedSoFar);
            var totalPlanned = plannedSoFar + line.Quantity;

            var remaining = item.Quantity - alreadyReturned.GetValueOrDefault(item.Id);
            if (totalPlanned > remaining + QuantityTolerance)
            {
                throw new InvalidOperationException(
                    $"Cannot return {totalPlanned:0.###} of '{item.ProductNameSnapshot}' — only {remaining:0.###} of {item.Quantity:0.###} received remains returnable on {purchase.PurchaseNumber}.");
            }

            plannedByLine[line.PurchaseItemId] = totalPlanned;

            plannedByProduct.TryGetValue(item.ProductId, out var productPlanned);
            plannedByProduct[item.ProductId] = productPlanned + line.Quantity;
        }

        foreach (var (productId, quantity) in plannedByProduct)
        {
            var product = purchase.Items.First(i => i.ProductId == productId).Product;
            var onHand = product.Inventory?.QuantityOnHand ?? 0m;
            if (quantity > onHand + QuantityTolerance)
            {
                throw new InvalidOperationException(
                    $"Cannot return {quantity:0.###} of '{product.Name}' — only {onHand:0.###} is in stock. Returning it would drive inventory negative.");
            }
        }

        var returnNumber = await sequenceGenerator.NextAsync(ReturnSequenceKey, ReturnPrefix, ReturnPadding, cancellationToken);

        var purchaseReturn = new PurchaseReturn
        {
            ReturnNumber = returnNumber,
            Purchase = purchase,
            PurchaseNumberSnapshot = purchase.PurchaseNumber,
            Supplier = purchase.Supplier,
            Reason = request.Reason,
            Notes = request.Notes,
            ProcessedByUserId = request.ProcessedByUserId,
        };

        var totalReturnAmount = 0m;

        foreach (var line in request.Lines)
        {
            var item = purchase.Items.First(i => i.Id == line.PurchaseItemId);
            var product = item.Product;

            // Prorated from the original line total so the supplier is credited exactly what was
            // charged, discount and GST included — never the product's current purchase price.
            var lineAmount = item.Quantity == 0
                ? 0m
                : Math.Round(item.LineTotal * (line.Quantity / item.Quantity), 2, MidpointRounding.AwayFromZero);

            totalReturnAmount += lineAmount;

            // Fall back to the batch the goods were received into, which is what a shopkeeper
            // means by "send this batch back".
            var batchNumber = string.IsNullOrWhiteSpace(line.BatchNumber) ? item.BatchNumber : line.BatchNumber.Trim();

            purchaseReturn.Items.Add(new PurchaseReturnItem
            {
                PurchaseReturn = purchaseReturn,
                PurchaseItem = item,
                Product = product,
                ProductNameSnapshot = item.ProductNameSnapshot,
                ProductCodeSnapshot = item.ProductCodeSnapshot,
                SkuSnapshot = item.SkuSnapshot,
                UnitSnapshot = item.UnitSnapshot,
                PurchasePriceSnapshot = item.PurchasePriceSnapshot,
                GstRatePercentSnapshot = item.GstRatePercentSnapshot,
                Quantity = line.Quantity,
                LineReturnAmount = lineAmount,
                BatchNumber = batchNumber,
                Reason = line.Reason,
            });

            var inventory = product.Inventory
                ?? throw new InvalidOperationException($"'{product.Name}' has no inventory record to return from.");

            var previousQuantity = inventory.QuantityOnHand;
            inventory.QuantityOnHand -= line.Quantity;
            inventory.UpdatedAtUtc = DateTime.UtcNow;

            db.StockMovements.Add(new StockMovement
            {
                Product = product,
                MovementType = StockMovementType.PurchaseReturn,
                QuantityChange = -line.Quantity,
                PreviousQuantity = previousQuantity,
                NewQuantity = inventory.QuantityOnHand,
                UserId = request.ProcessedByUserId,
                ReferenceType = nameof(PurchaseReturn),
                ReferenceId = returnNumber,
                Reason = line.Reason,
            });

            if (product.TracksBatches && !string.IsNullOrWhiteSpace(batchNumber))
            {
                var batch = product.Batches.FirstOrDefault(b => b.BatchNumber == batchNumber);
                if (batch is not null)
                {
                    // Floored at zero: the batch record must never go negative even if the goods
                    // were partly consumed from a different batch.
                    batch.Quantity = Math.Max(0m, batch.Quantity - line.Quantity);
                    batch.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
        }

        purchaseReturn.TotalReturnAmount = totalReturnAmount;

        // The supplier owes less now. Floored at zero so a return against an already-settled
        // purchase cannot push the balance negative and misreport what is payable.
        var supplier = purchase.Supplier;
        supplier.OutstandingBalance = Math.Max(0m, supplier.OutstandingBalance - totalReturnAmount);
        supplier.UpdatedAtUtc = DateTime.UtcNow;

        db.PurchaseReturns.Add(purchaseReturn);

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.ProcessedByUserId, "PurchaseReturnProcessed", nameof(PurchaseReturn), purchaseReturn.Id.ToString(),
            newValue: $"{returnNumber} — ₹{totalReturnAmount:0.00} to {supplier.SupplierCode} against {purchase.PurchaseNumber}",
            reason: request.Reason,
            cancellationToken: cancellationToken);

        return purchaseReturn;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<PurchaseReturn>> SearchAsync(
        PurchaseReturnSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        IQueryable<PurchaseReturn> returns = db.PurchaseReturns.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Purchase)
            .Include(r => r.Items);

        var text = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var like = $"%{text}%";
            returns = returns.Where(r =>
                EF.Functions.Like(r.ReturnNumber, like)
                || EF.Functions.Like(r.PurchaseNumberSnapshot, like)
                || (r.Purchase.SupplierNameSnapshot != null && EF.Functions.Like(r.Purchase.SupplierNameSnapshot, like))
                || EF.Functions.Like(r.Supplier.Name, like));
        }

        if (query.SupplierId is { } supplierId)
        {
            returns = returns.Where(r => r.SupplierId == supplierId);
        }

        if (query.FromDateUtc is { } from)
        {
            returns = returns.Where(r => r.ReturnDateUtc >= from);
        }

        if (query.ToDateUtc is { } to)
        {
            returns = returns.Where(r => r.ReturnDateUtc <= to);
        }

        return await returns
            .OrderByDescending(r => r.ReturnDateUtc)
            .Take(query.MaxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<PurchaseReturn?> GetByIdAsync(int purchaseReturnId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        return await db.PurchaseReturns.AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Purchase)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == purchaseReturnId, cancellationToken);
    }
}
