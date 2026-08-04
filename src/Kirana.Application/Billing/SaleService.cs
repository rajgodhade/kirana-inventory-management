using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Billing;

public sealed class SaleService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : ISaleService
{
    /// <summary>Item/bill discounts at or below this don't need manager authorization (PRD §10).</summary>
    public const decimal MaxUnauthorizedDiscountPercent = 10m;

    private const decimal PaymentAmountTolerance = 0.02m;

    public async Task<Sale> CompleteSaleAsync(CompleteSaleRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("Cart cannot be empty.", nameof(request));
        }

        if (request.Payments.Count == 0)
        {
            throw new ArgumentException("At least one payment is required.", nameof(request));
        }

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products
            .Include(p => p.Inventory)
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
                throw new InvalidOperationException($"'{product.Name}' is inactive and cannot be sold.");
            }

            if (!product.Unit.SupportsDecimalQuantity() && line.Quantity != Math.Floor(line.Quantity))
            {
                throw new ArgumentException($"'{product.Name}' is sold in whole {product.Unit} units — {line.Quantity} is not valid.");
            }

            if (line.DiscountPercent is < 0 or > 100)
            {
                throw new ArgumentException($"Discount for '{product.Name}' must be between 0 and 100 percent.");
            }

            var availableStock = product.Inventory?.QuantityOnHand ?? 0;
            if (availableStock < line.Quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for '{product.Name}'. Available: {availableStock:0.###}, requested: {line.Quantity:0.###}.");
            }
        }

        var maxDiscountRequested = Math.Max(
            request.BillDiscountPercent,
            request.Lines.Count == 0 ? 0 : request.Lines.Max(l => l.DiscountPercent));

        if (maxDiscountRequested > MaxUnauthorizedDiscountPercent)
        {
            await EnsureDiscountAuthorizedAsync(request.DiscountAuthorizedByUserId, cancellationToken);
        }

        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken);
        var isGstEnabled = store?.IsGstEnabled ?? false;

        var cartLines = request.Lines.Select(line =>
        {
            var product = products[line.ProductId];
            return new CartLine
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = product.SellingPrice,
                IsTaxInclusive = product.IsTaxInclusive,
                GstRatePercent = product.GstRatePercent ?? 0,
                DiscountPercent = line.DiscountPercent,
            };
        }).ToList();

        var totals = CartPricingCalculator.Calculate(cartLines, request.BillDiscountPercent, isGstEnabled);

        var paymentsTotal = request.Payments.Sum(p => p.Amount);
        if (Math.Abs(paymentsTotal - totals.GrandTotal) > PaymentAmountTolerance)
        {
            throw new InvalidOperationException(
                $"Payment total ₹{paymentsTotal:0.00} does not match the bill total ₹{totals.GrandTotal:0.00}.");
        }

        foreach (var payment in request.Payments)
        {
            if (payment.Amount <= 0)
            {
                throw new ArgumentException("Each payment amount must be positive.");
            }

            if (payment.Method == PaymentMethod.CustomerCredit && request.CustomerId is null)
            {
                throw new InvalidOperationException("A customer must be selected to use Customer Credit / Udhaar.");
            }
        }

        Customer? customer = null;
        if (request.CustomerId is { } customerId)
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                ?? throw new InvalidOperationException("Selected customer was not found.");
        }

        var year = DateTime.UtcNow.Year;
        var invoiceNumber = await sequenceGenerator.NextAsync($"Invoice-{year}", $"INV-{year}", 6, cancellationToken);

        var sale = new Sale
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = customer?.Id,
            CashierUserId = request.CashierUserId,
            SubTotal = totals.SubTotal,
            ItemDiscountTotal = totals.ItemDiscountTotal,
            BillDiscountPercent = totals.BillDiscountPercent,
            BillDiscountAmount = totals.BillDiscountAmount,
            TaxableTotal = totals.TaxableTotal,
            TaxTotal = totals.GstTotal,
            RoundOffAmount = totals.RoundOffAmount,
            GrandTotal = totals.GrandTotal,
            Status = SaleStatus.Completed,
            DiscountAuthorizedByUserId = maxDiscountRequested > MaxUnauthorizedDiscountPercent ? request.DiscountAuthorizedByUserId : null,
        };

        foreach (var lineResult in totals.Lines)
        {
            var product = products[lineResult.Line.ProductId];

            sale.Items.Add(new SaleItem
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
                UnitPriceSnapshot = lineResult.Line.UnitPrice,
                DiscountPercent = lineResult.Line.DiscountPercent,
                DiscountAmount = lineResult.DiscountAmount,
                TaxableAmount = lineResult.TaxableAmount,
                GstAmount = lineResult.GstAmount,
                LineTotal = lineResult.LineTotal,
            });

            var inventory = product.Inventory!;
            var previousQuantity = inventory.QuantityOnHand;
            inventory.QuantityOnHand -= lineResult.Line.Quantity;
            inventory.UpdatedAtUtc = DateTime.UtcNow;

            db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                MovementType = StockMovementType.Sale,
                QuantityChange = -lineResult.Line.Quantity,
                PreviousQuantity = previousQuantity,
                NewQuantity = inventory.QuantityOnHand,
                UserId = request.CashierUserId,
                ReferenceType = "Sale",
                ReferenceId = invoiceNumber,
            });
        }

        foreach (var payment in request.Payments)
        {
            decimal? changeGiven = payment.Method == PaymentMethod.Cash && payment.AmountTendered is { } tendered
                ? tendered - payment.Amount
                : null;

            sale.Payments.Add(new Payment
            {
                Method = payment.Method,
                Amount = payment.Amount,
                ReferenceNumber = payment.ReferenceNumber,
                AmountTendered = payment.Method == PaymentMethod.Cash ? payment.AmountTendered : null,
                ChangeGiven = changeGiven,
            });

            if (payment.Method == PaymentMethod.CustomerCredit)
            {
                customer!.CreditBalance += payment.Amount;

                var credit = new CustomerCredit
                {
                    Customer = customer,
                    Sale = sale,
                    Amount = payment.Amount,
                    RemainingAmount = payment.Amount,
                };
                db.CustomerCredits.Add(credit);
            }
        }

        db.Sales.Add(sale);

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.CashierUserId, "SaleCompleted", nameof(Sale), sale.Id.ToString(),
            newValue: $"{invoiceNumber} — ₹{totals.GrandTotal:0.00}", cancellationToken: cancellationToken);

        if (sale.DiscountAuthorizedByUserId is { } authorizerId)
        {
            await auditLogger.RecordAsync(
                authorizerId, "DiscountAuthorized", nameof(Sale), sale.Id.ToString(),
                reason: $"Discount of {maxDiscountRequested:0.##}% on invoice {invoiceNumber}", cancellationToken: cancellationToken);
        }

        return sale;
    }

    public Task<Sale?> GetByIdAsync(int saleId, CancellationToken cancellationToken = default) =>
        db.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .Include(s => s.CashierUser)
            .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);

    public Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default) =>
        db.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .Include(s => s.CashierUser)
            .FirstOrDefaultAsync(s => s.InvoiceNumber == invoiceNumber, cancellationToken);

    private async Task EnsureDiscountAuthorizedAsync(int? authorizedByUserId, CancellationToken cancellationToken)
    {
        if (authorizedByUserId is null)
        {
            throw new InvalidOperationException(
                $"A discount above {MaxUnauthorizedDiscountPercent:0.##}% requires manager authorization.");
        }

        if (!await permissionEnforcer.HasPermissionAsync(authorizedByUserId, PermissionKeys.BillingApproveLargeDiscount, cancellationToken))
        {
            throw new InvalidOperationException("The authorizing user is not permitted to approve large discounts.");
        }
    }
}
