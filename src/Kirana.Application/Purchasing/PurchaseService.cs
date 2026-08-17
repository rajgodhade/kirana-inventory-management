using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

public sealed class PurchaseService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer,
    IPurchaseGstCalculationService? gstCalculationService = null)
    : IPurchaseService
{
    private const decimal AmountTolerance = 0.02m;

    public async Task<Purchase> FinalizePurchaseAsync(CreatePurchaseRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.CreatedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A purchase must have at least one line.", nameof(request));
        }

        // A purchase can settle part of its bill on the spot, and cash paid to a supplier leaves the
        // drawer (Phase 16A-2). Only that initial payment is gated — a purchase recorded entirely on
        // credit moves no physical money and is unaffected.
        if (request.AmountPaid > 0 && request.PaymentMethod is { } initialMethod)
        {
            await CashImpactPolicy.EnsureRegisterAvailableForAsync(db, initialMethod, cancellationToken);
        }

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        if (!supplier.IsActive)
        {
            throw new InvalidOperationException($"'{supplier.Name}' is inactive and cannot receive new purchases.");
        }

        var supplierInvoiceNumber = string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber)
            ? null : request.SupplierInvoiceNumber.Trim();
        if (supplierInvoiceNumber is not null && await db.Purchases.AsNoTracking().AnyAsync(
                p => p.SupplierId == request.SupplierId && p.SupplierInvoiceNumber == supplierInvoiceNumber,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Supplier invoice '{supplierInvoiceNumber}' has already been recorded for {supplier.Name}.");
        }

        GoodsReceipt? sourceReceipt = null;
        if (request.GoodsReceiptId is { } receiptId)
        {
            sourceReceipt = await db.GoodsReceipts.Include(g => g.Items).Include(g => g.Purchase)
                .FirstOrDefaultAsync(g => g.Id == receiptId, cancellationToken)
                ?? throw new InvalidOperationException("Goods receipt not found.");
            if (sourceReceipt.Status != GoodsReceiptStatus.Completed)
                throw new InvalidOperationException("Only a completed goods receipt can create a purchase.");
            if (sourceReceipt.Purchase is not null)
                throw new InvalidOperationException($"Purchase {sourceReceipt.Purchase.PurchaseNumber} already exists for this goods receipt.");
            if (sourceReceipt.SupplierId != request.SupplierId)
                throw new InvalidOperationException("The purchase supplier must match the goods receipt supplier.");
            if (request.PurchaseOrderId != sourceReceipt.PurchaseOrderId)
                throw new InvalidOperationException("The purchase order reference does not match the goods receipt.");

            var receivedByProduct = sourceReceipt.Items.GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.ReceivedQuantity));
            var requestedByProduct = request.Lines.GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
            if (receivedByProduct.Count != requestedByProduct.Count
                || receivedByProduct.Any(x => !requestedByProduct.TryGetValue(x.Key, out var quantity) || quantity != x.Value))
                throw new InvalidOperationException("Purchase quantities must exactly match the completed goods receipt.");
        }
        else if (request.PurchaseOrderId is not null)
        {
            throw new InvalidOperationException("A purchase order reference requires a goods receipt reference.");
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products
            .Include(p => p.Inventory)
            // Batches must be loaded, not just change-tracked: receiving more of an existing batch
            // number has to top up that row rather than insert a duplicate one.
            .Include(p => p.Batches)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var line in request.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                throw new InvalidOperationException($"Product #{line.ProductId} was not found.");
            }

            if (!product.IsActive)
            {
                throw new InvalidOperationException($"'{product.Name}' is inactive and cannot be purchased.");
            }

            if (!product.Unit.SupportsDecimalQuantity() && line.Quantity != Math.Floor(line.Quantity))
            {
                throw new ArgumentException($"'{product.Name}' is stocked in whole {product.Unit} units — {line.Quantity} is not valid.");
            }

            // Phase 13A: purchasing in a configured bulk pack (e.g. 10 Box where 1 Box = 12
            // Piece). The client is expected to have already converted line.Quantity to the
            // base-unit amount — this only VALIDATES that conversion agrees with the product's
            // configured pack, rather than deriving Quantity itself, so there is never a second
            // source of truth for how much stock a line adds.
            if (line.PurchasedPackUnit is { } packUnit)
            {
                if (product.PurchasePackUnit is null || product.PurchasePackSize is null)
                {
                    throw new ArgumentException($"'{product.Name}' does not have a purchase pack configured.");
                }

                if (packUnit != product.PurchasePackUnit.Value)
                {
                    throw new ArgumentException(
                        $"'{product.Name}' pack unit is '{product.PurchasePackUnit}', not '{packUnit}'.");
                }

                if (line.PurchasedPackQuantity is not { } packQty || packQty <= 0)
                {
                    throw new ArgumentException($"'{product.Name}' pack quantity must be greater than zero.");
                }

                var expectedQuantity = UnitConversion.ToBaseQuantity(packQty, product.PurchasePackSize.Value, packUnit, product.Unit);
                if (line.Quantity != expectedQuantity)
                {
                    throw new ArgumentException(
                        $"'{product.Name}': {packQty} {packUnit} should equal {expectedQuantity} {product.Unit}, but {line.Quantity} was submitted.");
                }
            }

            if (line.DiscountPercent is < 0 or > 100)
            {
                throw new ArgumentException($"Discount for '{product.Name}' must be between 0 and 100 percent.");
            }
        }

        if (request.AmountPaid < 0)
        {
            throw new ArgumentException("Amount paid cannot be negative.", nameof(request));
        }

        if (request.AmountPaid > 0 && request.PaymentMethod is null)
        {
            throw new ArgumentException("A payment method is required when recording an amount paid.", nameof(request));
        }

        var purchaseLines = request.Lines.Select(line =>
        {
            var product = products[line.ProductId];
            return new PurchaseLine
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                PricingType = line.PricingType ?? product.PricingType,
                GstRatePercent = product.GstRatePercent ?? 0,
                DiscountPercent = line.DiscountPercent,
            };
        }).ToList();

        var totals = (gstCalculationService ?? PurchaseGstCalculationService.Shared).Calculate(purchaseLines);

        if (request.AmountPaid > totals.GrandTotal + AmountTolerance)
        {
            throw new InvalidOperationException(
                $"Amount paid ₹{request.AmountPaid:0.00} cannot exceed the purchase total ₹{totals.GrandTotal:0.00}.");
        }

        var year = (request.PurchaseDateUtc ?? DateTime.UtcNow).Year;
        var purchaseNumber = await sequenceGenerator.NextAsync($"Purchase-{year}", $"PUR-{year}", 6, cancellationToken);

        var purchase = new Purchase
        {
            PurchaseNumber = purchaseNumber,
            SupplierInvoiceNumber = supplierInvoiceNumber,
            PurchaseDateUtc = request.PurchaseDateUtc ?? DateTime.UtcNow,
            Supplier = supplier,
            CreatedByUserId = request.CreatedByUserId,
            SubTotal = totals.SubTotal,
            DiscountTotal = totals.DiscountTotal,
            TaxableTotal = totals.TaxableTotal,
            TaxTotal = totals.TaxTotal,
            RoundOffAmount = totals.RoundOffAmount,
            GrandTotal = totals.GrandTotal,
            AmountPaid = 0,
            OutstandingAmount = totals.GrandTotal,
            PaymentMethod = request.AmountPaid > 0 ? request.PaymentMethod : null,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Status = PurchaseStatus.Completed,
            GoodsReceipt = sourceReceipt,
            PurchaseOrderId = sourceReceipt?.PurchaseOrderId,
        };

        foreach (var lineResult in totals.Lines)
        {
            var product = products[lineResult.Line.ProductId];
            var lineInput = request.Lines.First(l => l.ProductId == lineResult.Line.ProductId);

            purchase.Items.Add(new PurchaseItem
            {
                Product = product,
                ProductNameSnapshot = product.Name,
                ProductCodeSnapshot = product.ProductCode,
                SkuSnapshot = product.Sku,
                HsnCodeSnapshot = product.HsnCode,
                UnitSnapshot = product.Unit.ToString(),
                PurchasedPackUnitSnapshot = lineInput.PurchasedPackUnit?.ToString(),
                PurchasedPackQuantitySnapshot = lineInput.PurchasedPackQuantity,
                IsTaxInclusiveSnapshot = lineResult.Line.PricingType == PricingType.Inclusive,
                GstRatePercentSnapshot = product.GstRatePercent ?? 0,
                Quantity = lineResult.Line.Quantity,
                PurchasePriceSnapshot = lineResult.Line.UnitPrice,
                DiscountPercent = lineResult.Line.DiscountPercent,
                DiscountAmount = lineResult.DiscountAmount,
                TaxableAmount = lineResult.TaxableAmount,
                GstAmount = lineResult.GstAmount,
                LineTotal = lineResult.LineTotal,
                BatchNumber = lineInput.BatchNumber,
                ManufacturingDate = lineInput.ManufacturingDate,
                ExpiryDate = lineInput.ExpiryDate,
            });

            var inventory = product.Inventory;
            if (inventory is null)
            {
                inventory = new Inventory { Product = product, QuantityOnHand = 0 };
                db.Inventories.Add(inventory);
            }

            var previousQuantity = inventory.QuantityOnHand;
            inventory.QuantityOnHand += lineResult.Line.Quantity;
            inventory.UpdatedAtUtc = DateTime.UtcNow;

            db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                MovementType = StockMovementType.Purchase,
                QuantityChange = lineResult.Line.Quantity,
                PreviousQuantity = previousQuantity,
                NewQuantity = inventory.QuantityOnHand,
                UserId = request.CreatedByUserId,
                ReferenceType = "Purchase",
                ReferenceId = purchaseNumber,
            });

            if (product.TracksBatches && !string.IsNullOrWhiteSpace(lineInput.BatchNumber))
            {
                var batchNumber = lineInput.BatchNumber.Trim();
                var existingBatch = product.Batches.FirstOrDefault(b => b.BatchNumber == batchNumber);
                if (existingBatch is not null)
                {
                    existingBatch.Quantity += lineResult.Line.Quantity;
                    existingBatch.PurchasePrice = lineResult.Line.UnitPrice;
                    if (lineInput.ExpiryDate is not null)
                    {
                        existingBatch.ExpiryDate = lineInput.ExpiryDate;
                    }
                }
                else
                {
                    db.ProductBatches.Add(new ProductBatch
                    {
                        Product = product,
                        BatchNumber = batchNumber,
                        ManufacturingDate = lineInput.ManufacturingDate,
                        ExpiryDate = lineInput.ExpiryDate,
                        Quantity = lineResult.Line.Quantity,
                        PurchasePrice = lineResult.Line.UnitPrice,
                    });
                }
            }
        }

        db.Purchases.Add(purchase);
        supplier.OutstandingBalance += totals.GrandTotal;

        SupplierPayment? initialPayment = null;
        if (request.AmountPaid > 0)
        {
            initialPayment = new SupplierPayment
            {
                Supplier = supplier,
                Purchase = purchase,
                Amount = request.AmountPaid,
                Method = request.PaymentMethod!.Value,
                ReferenceNumber = request.PaymentReferenceNumber,
                RecordedByUserId = request.CreatedByUserId,
            };
            db.SupplierPayments.Add(initialPayment);
            ApplyPaymentToBalances(initialPayment);
        }

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.CreatedByUserId, "PurchaseCreated", nameof(Purchase), purchase.Id.ToString(),
            newValue: $"{purchaseNumber} — ₹{totals.GrandTotal:0.00} from {supplier.SupplierCode}", cancellationToken: cancellationToken);

        if (sourceReceipt is not null)
        {
            await auditLogger.RecordAsync(
                request.CreatedByUserId, "PurchaseCreatedFromGoodsReceipt", nameof(GoodsReceipt), sourceReceipt.Id.ToString(),
                newValue: $"{sourceReceipt.GoodsReceiptNumber} -> {purchaseNumber}; PO #{sourceReceipt.PurchaseOrderId}",
                cancellationToken: cancellationToken);
        }

        // Audit the up-front payment as a payment in its own right, so every SupplierPayment row is
        // individually traceable — otherwise money taken at purchase time is only implicitly
        // covered by the PurchaseCreated entry and can't be reconciled from the audit log alone.
        if (initialPayment is not null)
        {
            await auditLogger.RecordAsync(
                request.CreatedByUserId, "SupplierPaymentRecorded", nameof(SupplierPayment), initialPayment.Id.ToString(),
                newValue: $"₹{initialPayment.Amount:0.00} to {supplier.SupplierCode} against {purchaseNumber}",
                cancellationToken: cancellationToken);
        }

        return purchase;
    }

    public async Task<SupplierPayment> RecordPaymentAsync(RecordSupplierPaymentRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.RecordedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Payment amount must be positive.", nameof(request));
        }

        // Paying a supplier in cash empties the drawer (Phase 16A-2), so it needs an open register.
        await CashImpactPolicy.EnsureRegisterAvailableForAsync(db, request.Method, cancellationToken);

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        Purchase? purchase = null;
        if (request.PurchaseId is { } purchaseId)
        {
            purchase = await db.Purchases.FirstOrDefaultAsync(p => p.Id == purchaseId && p.SupplierId == request.SupplierId, cancellationToken)
                ?? throw new InvalidOperationException("Purchase not found for this supplier.");
        }

        var payment = new SupplierPayment
        {
            Supplier = supplier,
            Purchase = purchase,
            Amount = request.Amount,
            Method = request.Method,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
            RecordedByUserId = request.RecordedByUserId,
        };
        db.SupplierPayments.Add(payment);
        ApplyPaymentToBalances(payment);

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.RecordedByUserId, "SupplierPaymentRecorded", nameof(SupplierPayment), payment.Id.ToString(),
            newValue: $"₹{request.Amount:0.00} to {supplier.SupplierCode}" + (purchase is not null ? $" against {purchase.PurchaseNumber}" : string.Empty),
            cancellationToken: cancellationToken);

        return payment;
    }

    public async Task<Purchase?> GetByIdAsync(int purchaseId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        return await db.Purchases
            .Include(p => p.Items)
            .Include(p => p.Payments)
            .Include(p => p.Supplier)
            .Include(p => p.CreatedByUser)
            .Include(p => p.GoodsReceipt)
            .Include(p => p.PurchaseOrder)
            .FirstOrDefaultAsync(p => p.Id == purchaseId, cancellationToken);
    }

    public async Task<IReadOnlyList<Purchase>> SearchAsync(
        PurchaseSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var purchases = db.Purchases.Include(p => p.Supplier).AsQueryable();

        if (query.SupplierId is { } supplierId)
        {
            purchases = purchases.Where(p => p.SupplierId == supplierId);
        }

        var text = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var likeText = $"%{text}%";
            purchases = purchases.Where(p =>
                EF.Functions.Like(p.PurchaseNumber, likeText) ||
                (p.SupplierInvoiceNumber != null && EF.Functions.Like(p.SupplierInvoiceNumber, likeText)) ||
                EF.Functions.Like(p.Supplier.Name, likeText));
        }

        if (query.FromUtc is { } fromUtc)
        {
            purchases = purchases.Where(p => p.PurchaseDateUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            purchases = purchases.Where(p => p.PurchaseDateUtc <= toUtc);
        }

        if (query.OutstandingOnly)
        {
            purchases = purchases.Where(p => p.OutstandingAmount > 0);
        }

        return await purchases.OrderByDescending(p => p.PurchaseDateUtc).Take(query.MaxResults).ToListAsync(cancellationToken);
    }

    private static void ApplyPaymentToBalances(SupplierPayment payment)
    {
        if (payment.Purchase is not null)
        {
            payment.Purchase.AmountPaid += payment.Amount;
            payment.Purchase.OutstandingAmount -= payment.Amount;
        }

        payment.Supplier.OutstandingBalance -= payment.Amount;
    }
}
