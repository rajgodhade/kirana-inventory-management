using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Returns;

public sealed class SalesReturnService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : ISalesReturnService
{
    private const string ReturnSequenceKey = "SalesReturn";
    private const string ReturnPrefix = "SRN";
    private const int ReturnPadding = 6;

    /// <summary>Quantities are stored to 3 decimals, so comparisons allow a sub-milli tolerance
    /// rather than failing a legitimate "return everything" on a rounding artefact.</summary>
    private const decimal QuantityTolerance = 0.0005m;

    // ---------------------------------------------------------------- lookup

    public async Task<IReadOnlyList<ReturnableSale>> FindReturnableSalesAsync(
        SaleLookupQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.SalesProcessRefund, cancellationToken);

        var text = query.SearchText?.Trim();
        IQueryable<Sale> sales = db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed);

        if (query.CustomerId is { } customerId)
        {
            sales = sales.Where(s => s.CustomerId == customerId);
        }

        if (!string.IsNullOrEmpty(text))
        {
            var like = $"%{text}%";
            var normalizedText = BarcodeNormalizer.Normalize(text);

            // One box, four ways to search (PRD §33). Barcode and product hits resolve through the
            // sale's *snapshotted* line data plus the product's live barcodes/SKU, so a scan finds
            // the sale even when the product has been renamed since.
            //
            // Retired barcodes deliberately still match here (no IsActive filter): someone
            // returning goods months later may well scan a code that has since been retired, and
            // finding the original sale is exactly the point.
            sales = sales.Where(s =>
                EF.Functions.Like(s.InvoiceNumber, like)
                || (s.Customer != null && (
                    EF.Functions.Like(s.Customer.Name, like)
                    || EF.Functions.Like(s.Customer.CustomerCode, like)
                    || (s.Customer.Phone != null && EF.Functions.Like(s.Customer.Phone, like))))
                || s.Items.Any(i =>
                    EF.Functions.Like(i.ProductNameSnapshot, like)
                    || EF.Functions.Like(i.ProductCodeSnapshot, like)
                    || (i.SkuSnapshot != null && EF.Functions.Like(i.SkuSnapshot, like))
                    || i.Product.Barcodes.Any(b => b.NormalizedValue == normalizedText)));
        }

        var matches = await sales
            .OrderByDescending(s => s.SaleDateUtc)
            .Take(query.MaxResults)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var results = new List<ReturnableSale>();
        foreach (var saleId in matches)
        {
            if (await BuildReturnableSaleAsync(saleId, cancellationToken) is { } returnable)
            {
                results.Add(returnable);
            }
        }

        return results;
    }

    public async Task<ReturnableSale?> GetReturnableSaleAsync(
        int saleId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.SalesProcessRefund, cancellationToken);
        return await BuildReturnableSaleAsync(saleId, cancellationToken);
    }

    private async Task<ReturnableSale?> BuildReturnableSaleAsync(int saleId, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);

        if (sale is null)
        {
            return null;
        }

        var returnedByLine = await GetReturnedQuantitiesAsync(sale.Items.Select(i => i.Id).ToList(), cancellationToken);

        return new ReturnableSale
        {
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            SaleDateUtc = sale.SaleDateUtc,
            CustomerId = sale.CustomerId,
            CustomerName = sale.Customer?.Name,
            CustomerCode = sale.Customer?.CustomerCode,
            GrandTotal = sale.GrandTotal,
            Lines = sale.Items.Select(i => new ReturnableLine
            {
                SaleItemId = i.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductNameSnapshot,
                ProductCode = i.ProductCodeSnapshot,
                Unit = i.UnitSnapshot,
                TracksBatches = i.Product.TracksBatches,
                SoldQuantity = i.Quantity,
                AlreadyReturnedQuantity = returnedByLine.GetValueOrDefault(i.Id),
                UnitPrice = i.UnitPriceSnapshot,
                LineTotal = i.LineTotal,
            }).ToList(),
        };
    }

    /// <summary>Total already returned per sale line, across every previous return. This is the
    /// only thing that caps a new return — never the sale line's quantity on its own.</summary>
    private async Task<Dictionary<int, decimal>> GetReturnedQuantitiesAsync(
        List<int> saleItemIds, CancellationToken cancellationToken) =>
        await db.SalesReturnItems.AsNoTracking()
            .Where(ri => saleItemIds.Contains(ri.SaleItemId))
            .GroupBy(ri => ri.SaleItemId)
            .Select(g => new { SaleItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.SaleItemId, x => x.Quantity, cancellationToken);

    // ---------------------------------------------------------------- process

    public async Task<SalesReturn> ProcessReturnAsync(CreateSalesReturnRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.ProcessedByUserId, PermissionKeys.SalesProcessRefund, cancellationToken);

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A return must have at least one line.", nameof(request));
        }

        // Cash handed back to a customer leaves the drawer (Phase 16A-2). StoreCredit and None move
        // no physical money, so they are unaffected and a refund can still be processed with the
        // register closed.
        await CashImpactPolicy.EnsureRegisterAvailableForAsync(db, request.RefundMethod, cancellationToken);

        var sale = await db.Sales
            .Include(s => s.Customer)
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Inventory)
            .Include(s => s.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Batches)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId, cancellationToken)
            ?? throw new InvalidOperationException("Sale not found.");

        if (sale.Status != SaleStatus.Completed)
        {
            throw new InvalidOperationException($"Invoice {sale.InvoiceNumber} is {sale.Status} and cannot be returned against.");
        }

        var alreadyReturned = await GetReturnedQuantitiesAsync(sale.Items.Select(i => i.Id).ToList(), cancellationToken);

        // Validate everything before mutating anything, so a bad line cannot leave a half-applied
        // return behind even before the transaction boundary is considered.
        var plannedByLine = new Dictionary<int, decimal>();
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException("Return quantity must be greater than zero.", nameof(request));
            }

            var saleItem = sale.Items.FirstOrDefault(i => i.Id == line.SaleItemId)
                ?? throw new InvalidOperationException($"Line #{line.SaleItemId} does not belong to invoice {sale.InvoiceNumber}.");

            if (!saleItem.Product.Unit.SupportsDecimalQuantity() && line.Quantity != Math.Floor(line.Quantity))
            {
                throw new ArgumentException(
                    $"'{saleItem.ProductNameSnapshot}' is sold in whole {saleItem.UnitSnapshot} units — {line.Quantity} is not a valid return quantity.");
            }

            plannedByLine.TryGetValue(line.SaleItemId, out var plannedSoFar);
            var totalPlanned = plannedSoFar + line.Quantity;

            // The cap counts previous returns AND other lines in this same request targeting the
            // same sale line — otherwise a request could split an over-return across two entries.
            var remaining = saleItem.Quantity - alreadyReturned.GetValueOrDefault(saleItem.Id);
            if (totalPlanned > remaining + QuantityTolerance)
            {
                throw new InvalidOperationException(
                    $"Cannot return {totalPlanned:0.###} of '{saleItem.ProductNameSnapshot}' — only {remaining:0.###} of {saleItem.Quantity:0.###} remains returnable on invoice {sale.InvoiceNumber}.");
            }

            plannedByLine[line.SaleItemId] = totalPlanned;
        }

        var returnNumber = await sequenceGenerator.NextAsync(ReturnSequenceKey, ReturnPrefix, ReturnPadding, cancellationToken);

        var salesReturn = new SalesReturn
        {
            ReturnNumber = returnNumber,
            Sale = sale,
            InvoiceNumberSnapshot = sale.InvoiceNumber,
            CustomerId = sale.CustomerId,
            RefundMethod = request.RefundMethod,
            ReferenceNumber = request.ReferenceNumber,
            Reason = request.Reason,
            Notes = request.Notes,
            ProcessedByUserId = request.ProcessedByUserId,
            AuthorizedByUserId = request.AuthorizedByUserId,
        };

        var totalReturnAmount = 0m;

        foreach (var line in request.Lines)
        {
            var saleItem = sale.Items.First(i => i.Id == line.SaleItemId);
            var product = saleItem.Product;

            // Prorated from the original line total, so whatever discount and GST the customer
            // actually paid is what comes back — never recomputed from current prices.
            var lineRefund = saleItem.Quantity == 0
                ? 0m
                : Math.Round(saleItem.LineTotal * (line.Quantity / saleItem.Quantity), 2, MidpointRounding.AwayFromZero);

            totalReturnAmount += lineRefund;

            salesReturn.Items.Add(new SalesReturnItem
            {
                SalesReturn = salesReturn,
                SaleItem = saleItem,
                Product = product,
                ProductNameSnapshot = saleItem.ProductNameSnapshot,
                ProductCodeSnapshot = saleItem.ProductCodeSnapshot,
                SkuSnapshot = saleItem.SkuSnapshot,
                UnitSnapshot = saleItem.UnitSnapshot,
                UnitPriceSnapshot = saleItem.UnitPriceSnapshot,
                GstRatePercentSnapshot = saleItem.GstRatePercentSnapshot,
                Quantity = line.Quantity,
                LineRefundAmount = lineRefund,
                Disposition = line.Disposition,
                BatchNumber = line.Disposition == ReturnDisposition.ReturnToStock ? line.BatchNumber : null,
                Reason = line.Reason,
            });

            ApplyStockForReturnedLine(salesReturn, product, line, returnNumber, request.ProcessedByUserId);
        }

        salesReturn.TotalReturnAmount = totalReturnAmount;
        salesReturn.RefundAmount = ResolveRefundAmount(request, totalReturnAmount, sale);

        if (request.RefundMethod == RefundMethod.StoreCredit && salesReturn.RefundAmount > 0)
        {
            // Store credit settles what the customer owes; going below zero simply means the shop
            // now owes them, which the customer ledger surfaces as a Store Credit entry.
            var customer = sale.Customer!;
            customer.CreditBalance -= salesReturn.RefundAmount;
            customer.UpdatedAtUtc = DateTime.UtcNow;
        }

        db.SalesReturns.Add(salesReturn);

        // One SaveChanges covers the return, its lines, inventory, batches and stock movements —
        // EF wraps them in a single transaction, so a failure anywhere rolls all of it back.
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.ProcessedByUserId, "SalesReturnProcessed", nameof(SalesReturn), salesReturn.Id.ToString(),
            newValue: $"{returnNumber} — ₹{totalReturnAmount:0.00} against {sale.InvoiceNumber}, refunded ₹{salesReturn.RefundAmount:0.00} by {request.RefundMethod}",
            reason: request.Reason,
            cancellationToken: cancellationToken);

        // Damaged stock is its own audited event: it is a write-off, not just a return.
        var damaged = salesReturn.Items.Where(i => i.Disposition == ReturnDisposition.Damaged).ToList();
        if (damaged.Count > 0)
        {
            await auditLogger.RecordAsync(
                request.ProcessedByUserId, "DamagedStockRecorded", nameof(SalesReturn), salesReturn.Id.ToString(),
                newValue: string.Join(", ", damaged.Select(i => $"{i.Quantity:0.###} x {i.ProductNameSnapshot}")),
                reason: request.Reason,
                cancellationToken: cancellationToken);
        }

        if (request.AuthorizedByUserId is { } authorizerId)
        {
            await auditLogger.RecordAsync(
                authorizerId, "RefundAuthorized", nameof(SalesReturn), salesReturn.Id.ToString(),
                newValue: $"₹{salesReturn.RefundAmount:0.00} on {returnNumber}",
                cancellationToken: cancellationToken);
        }

        return salesReturn;
    }

    /// <summary>
    /// Moves stock for one returned line.
    ///
    /// Return-to-stock adds the goods back and tops up the batch. Damaged goods are recorded as a
    /// return <em>and</em> an immediate write-off — two movements that net to zero on hand. That is
    /// deliberate: sellable stock must not rise, but the damaged quantity still has to be visible
    /// in the stock ledger rather than silently vanishing.
    /// </summary>
    private void ApplyStockForReturnedLine(
        SalesReturn salesReturn, Product product, SalesReturnLineInput line, string returnNumber, int? userId)
    {
        var inventory = product.Inventory;
        if (inventory is null)
        {
            inventory = new Inventory { Product = product, QuantityOnHand = 0 };
            db.Inventories.Add(inventory);
            product.Inventory = inventory;
        }

        var beforeReturn = inventory.QuantityOnHand;
        inventory.QuantityOnHand += line.Quantity;
        inventory.UpdatedAtUtc = DateTime.UtcNow;

        db.StockMovements.Add(new StockMovement
        {
            Product = product,
            MovementType = StockMovementType.SalesReturn,
            QuantityChange = line.Quantity,
            PreviousQuantity = beforeReturn,
            NewQuantity = inventory.QuantityOnHand,
            UserId = userId,
            ReferenceType = nameof(SalesReturn),
            ReferenceId = returnNumber,
            Reason = line.Reason,
        });

        if (line.Disposition == ReturnDisposition.Damaged)
        {
            var beforeWriteOff = inventory.QuantityOnHand;
            inventory.QuantityOnHand -= line.Quantity;

            db.StockMovements.Add(new StockMovement
            {
                Product = product,
                MovementType = StockMovementType.Damaged,
                QuantityChange = -line.Quantity,
                PreviousQuantity = beforeWriteOff,
                NewQuantity = inventory.QuantityOnHand,
                UserId = userId,
                ReferenceType = nameof(SalesReturn),
                ReferenceId = returnNumber,
                Reason = line.Reason ?? "Returned damaged / not resellable",
            });

            return;
        }

        if (product.TracksBatches && !string.IsNullOrWhiteSpace(line.BatchNumber))
        {
            var batchNumber = line.BatchNumber.Trim();
            var batch = product.Batches.FirstOrDefault(b => b.BatchNumber == batchNumber);
            if (batch is not null)
            {
                batch.Quantity += line.Quantity;
                batch.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                db.ProductBatches.Add(new ProductBatch
                {
                    Product = product,
                    BatchNumber = batchNumber,
                    Quantity = line.Quantity,
                });
            }
        }
    }

    private static decimal ResolveRefundAmount(CreateSalesReturnRequest request, decimal totalReturnAmount, Sale sale)
    {
        if (request.RefundMethod == RefundMethod.None)
        {
            return 0m;
        }

        var refund = request.RefundAmount ?? totalReturnAmount;

        if (refund < 0)
        {
            throw new ArgumentException("Refund amount cannot be negative.", nameof(request));
        }

        if (refund > totalReturnAmount + 0.01m)
        {
            throw new InvalidOperationException(
                $"Refund of ₹{refund:0.00} exceeds the ₹{totalReturnAmount:0.00} value of the returned goods.");
        }

        if (request.RefundMethod == RefundMethod.StoreCredit && sale.CustomerId is null)
        {
            throw new InvalidOperationException("Store credit needs a customer — this sale was to a walk-in customer.");
        }

        return refund;
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<SalesReturn>> SearchAsync(
        SalesReturnSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.SalesProcessRefund, cancellationToken);

        IQueryable<SalesReturn> returns = db.SalesReturns.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Items);

        var text = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var like = $"%{text}%";
            returns = returns.Where(r =>
                EF.Functions.Like(r.ReturnNumber, like)
                || EF.Functions.Like(r.InvoiceNumberSnapshot, like)
                || (r.Customer != null && EF.Functions.Like(r.Customer.Name, like)));
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

    public async Task<SalesReturn?> GetByIdAsync(int salesReturnId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.SalesProcessRefund, cancellationToken);

        return await db.SalesReturns.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Sale)
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == salesReturnId, cancellationToken);
    }
}
