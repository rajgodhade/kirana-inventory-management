using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

public sealed class PurchaseService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
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

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        if (!supplier.IsActive)
        {
            throw new InvalidOperationException($"'{supplier.Name}' is inactive and cannot receive new purchases.");
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
                IsTaxInclusive = product.IsTaxInclusive,
                GstRatePercent = product.GstRatePercent ?? 0,
                DiscountPercent = line.DiscountPercent,
            };
        }).ToList();

        var totals = PurchasePricingCalculator.Calculate(purchaseLines);

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
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber) ? null : request.SupplierInvoiceNumber.Trim(),
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
                IsTaxInclusiveSnapshot = product.IsTaxInclusive,
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
