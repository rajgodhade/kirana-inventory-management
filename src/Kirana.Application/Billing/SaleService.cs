using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.Promotions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Kirana.Application.Taxation;

namespace Kirana.Application.Billing;

public sealed class SaleService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer,
    IPromotionEngine? promotionEngine = null, IGstCalculationService? gstCalculationService = null,
    IProductPriceResolver? priceResolver = null)
    : ISaleService
{
    /// <summary>
    /// Phase 15B-2 selling-price source. Optional in the signature — the same convention the
    /// promotion engine and GST calculator were added under — but unlike those, omitting it does
    /// NOT disable the behaviour: it falls back to a real resolver over this same context, never to
    /// <c>Product.SellingPrice</c>. There is deliberately no way to construct a SaleService that
    /// prices from the projection column.
    /// </summary>
    private readonly IProductPriceResolver priceResolver = priceResolver ?? new ProductPriceResolver(db);

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

            if (line.UnitPriceOverride is { } overridePrice && overridePrice < 0)
            {
                throw new ArgumentException($"Overridden price for '{product.Name}' cannot be negative.");
            }

            var availableStock = product.Inventory?.QuantityOnHand ?? 0;
            if (availableStock < line.Quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient stock for '{product.Name}'. Available: {availableStock:0.###}, requested: {line.Quantity:0.###}.");
            }
        }

        // Phase 15B-2: the selling price comes from the pricing resolver, not Product.SellingPrice.
        //
        // Resolved HERE, on the server side of the till, because this method is the trust boundary:
        // a client can put any number in UnitPriceOverride, and the override check below decides
        // whether that number needed authorization by comparing it against the real price. If that
        // comparison used a client-supplied figure the check would be meaningless, so the price is
        // re-established from the authoritative store regardless of what the UI displayed.
        //
        // Resolved once per DISTINCT product rather than per line, and before any write, so a cart
        // with the same item on several lines costs one query and a failure leaves nothing behind.
        //
        // Phase 15B-3: at the level the bill is being sold at, not always Retail. The client sends
        // the LEVEL, never the amount - so choosing "Wholesale" cannot become a way to name your
        // own price.
        var pricingContext = new PricingContext(request.PriceLevel);
        var resolvedPrices = new Dictionary<int, decimal>(productIds.Count);
        foreach (var productId in productIds)
        {
            var resolution = await priceResolver.ResolveAsync(productId, pricingContext, cancellationToken);

            if (!resolution.IsResolved)
            {
                // No silent fallback - not to another level, not to the projection column, not to
                // MRP, not to zero. A product we cannot price at the requested level is a product
                // this bill must not sell.
                throw new InvalidOperationException(
                    $"'{products[productId].Name}' has no active " +
                    $"{request.PriceLevel.ToDisplayText().ToLowerInvariant()} price and cannot be sold.");
            }

            resolvedPrices[productId] = resolution.UnitPrice.Value;
        }

        // Store-level opt-out (Settings → Billing Authorization, Owner-only): a single-operator
        // store can turn this requirement off so the Owner isn't asked to authorize their own
        // action. Read fresh from AppSettings rather than trusting anything client-supplied, so the
        // opt-out is enforced the same way regardless of which UI (or lack of one) called this.
        var appSettings = await db.AppSettings.FirstOrDefaultAsync(cancellationToken);

        var maxDiscountRequested = Math.Max(
            request.BillDiscountPercent,
            request.Lines.Count == 0 ? 0 : request.Lines.Max(l => l.DiscountPercent));

        if (maxDiscountRequested > MaxUnauthorizedDiscountPercent && (appSettings?.RequirePinForLargeDiscount ?? true))
        {
            await EnsureDiscountAuthorizedAsync(request.DiscountAuthorizedByUserId, cancellationToken);
        }

        var hasPriceOverride = request.Lines.Any(l =>
            l.UnitPriceOverride is { } overridePrice && overridePrice != resolvedPrices[l.ProductId]);

        if (hasPriceOverride && (appSettings?.RequirePinForPriceOverride ?? true))
        {
            await EnsurePriceOverrideAuthorizedAsync(request.PriceOverrideAuthorizedByUserId, cancellationToken);
        }

        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken);
        var isGstEnabled = store?.IsGstEnabled ?? false;

        var promotionResults = promotionEngine is null
            ? new Dictionary<int, PromotionLineResult>()
            : (await promotionEngine.EvaluateCartAsync(new PromotionCartContext
            {
                Lines = request.Lines.Select(line => new PromotionLineContext
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPriceOverride ?? resolvedPrices[line.ProductId],
                }).ToList(),
                BillAmount = request.Lines.Sum(line =>
                    line.Quantity * (line.UnitPriceOverride ?? resolvedPrices[line.ProductId])),
                CustomerId = request.CustomerId,
                AtUtc = DateTime.UtcNow,
            }, cancellationToken)).ToDictionary(x => x.ProductId);

        var cartLines = request.Lines.Select(line =>
        {
            var product = products[line.ProductId];
            return new CartLine
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPriceOverride ?? resolvedPrices[line.ProductId],
                PricingType = product.PricingType,
                GstRatePercent = product.GstRatePercent ?? 0,
                DiscountPercent = line.DiscountPercent,
                PromotionBeforeTaxDiscountAmount = promotionResults.TryGetValue(line.ProductId, out var result)
                    ? result.AppliedPromotions.Where(x => x.CalculationMode == DiscountCalculationMode.BeforeTax).Sum(x => x.DiscountAmount)
                    : 0,
                PromotionAfterTaxDiscountAmount = promotionResults.TryGetValue(line.ProductId, out result)
                    ? result.AppliedPromotions.Where(x => x.CalculationMode == DiscountCalculationMode.AfterTax).Sum(x => x.DiscountAmount)
                    : 0,
            };
        }).ToList();

        var totals = (gstCalculationService ?? GstCalculationService.Shared)
            .CalculateSales(cartLines, request.BillDiscountPercent, isGstEnabled);

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

            // Physically impossible otherwise — cash tendered can't be less than the amount it's
            // covering. Checked server-side independent of the client, same as everything else
            // here: a caller bypassing PaymentDialog's own check must not be able to record a
            // negative "change returned" on the invoice.
            if (payment.Method == PaymentMethod.Cash && payment.AmountTendered is { } tendered && tendered < payment.Amount)
            {
                throw new ArgumentException(
                    $"Cash received (₹{tendered:0.00}) is less than the amount due (₹{payment.Amount:0.00}) for this payment.");
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
            PromotionDiscountTotal = totals.PromotionDiscountTotal,
            BillDiscountPercent = totals.BillDiscountPercent,
            BillDiscountAmount = totals.BillDiscountAmount,
            TaxableTotal = totals.TaxableTotal,
            TaxTotal = totals.GstTotal,
            RoundOffAmount = totals.RoundOffAmount,
            GrandTotal = totals.GrandTotal,
            Status = SaleStatus.Completed,
            DiscountAuthorizedByUserId = maxDiscountRequested > MaxUnauthorizedDiscountPercent ? request.DiscountAuthorizedByUserId : null,
            PriceOverrideAuthorizedByUserId = hasPriceOverride ? request.PriceOverrideAuthorizedByUserId : null,
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
                IsTaxInclusiveSnapshot = lineResult.Line.PricingType == PricingType.Inclusive,
                GstRatePercentSnapshot = product.GstRatePercent ?? 0,
                Quantity = lineResult.Line.Quantity,
                UnitPriceSnapshot = lineResult.Line.UnitPrice,
                MrpSnapshot = product.Mrp,
                DiscountPercent = lineResult.Line.DiscountPercent,
                DiscountAmount = lineResult.DiscountAmount,
                PromotionDiscountAmount = lineResult.PromotionDiscountAmount,
                TaxableAmount = lineResult.TaxableAmount,
                GstAmount = lineResult.GstAmount,
                LineTotal = lineResult.LineTotal,
            });

            var saleItem = sale.Items.Last();
            if (promotionResults.TryGetValue(product.Id, out var appliedResult))
            {
                foreach (var applied in appliedResult.AppliedPromotions)
                {
                    saleItem.Promotions.Add(new SaleItemPromotion
                    {
                        PromotionId = applied.PromotionId,
                        PromotionCodeSnapshot = applied.PromotionCode,
                        PromotionNameSnapshot = applied.PromotionName,
                        PromotionTypeSnapshot = applied.PromotionType,
                        CalculationModeSnapshot = applied.CalculationMode,
                        DiscountAmount = applied.DiscountAmount,
                    });
                }
            }

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

        var appliedPromotionIds = promotionResults.Values.SelectMany(x => x.AppliedPromotions)
            .Select(x => x.PromotionId).Distinct().ToList();
        if (appliedPromotionIds.Count > 0)
        {
            var appliedPromotions = await db.Promotions.Include(x => x.Schedule)
                .Where(x => appliedPromotionIds.Contains(x.Id)).ToListAsync(cancellationToken);
            foreach (var promotion in appliedPromotions)
            {
                promotion.CurrentUsage++;
                promotion.Status = PromotionStatusCalculator.Calculate(promotion, DateTime.UtcNow);
                promotion.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

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

        if (sale.PriceOverrideAuthorizedByUserId is { } priceAuthorizerId)
        {
            await auditLogger.RecordAsync(
                priceAuthorizerId, "PriceOverrideAuthorized", nameof(Sale), sale.Id.ToString(),
                reason: $"Selling price overridden on invoice {invoiceNumber}", cancellationToken: cancellationToken);
        }

        foreach (var applied in promotionResults.Values.SelectMany(x => x.AppliedPromotions).DistinctBy(x => x.PromotionId))
        {
            await auditLogger.RecordAsync(request.CashierUserId, "PromotionApplied", nameof(Promotion), applied.PromotionId.ToString(),
                newValue: $"{applied.PromotionCode} on {invoiceNumber} - ₹{applied.DiscountAmount:0.00}", cancellationToken: cancellationToken);
        }

        return sale;
    }

    public Task<Sale?> GetByIdAsync(int saleId, CancellationToken cancellationToken = default) =>
        db.Sales
            .Include(s => s.Items)
            .ThenInclude(i => i.Promotions)
            .Include(s => s.Payments)
            .Include(s => s.Customer)
            .Include(s => s.CashierUser)
            .FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);

    public Task<Sale?> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken cancellationToken = default) =>
        db.Sales
            .Include(s => s.Items)
            .ThenInclude(i => i.Promotions)
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

    private async Task EnsurePriceOverrideAuthorizedAsync(int? authorizedByUserId, CancellationToken cancellationToken)
    {
        if (authorizedByUserId is null)
        {
            throw new InvalidOperationException("Overriding the selling price requires manager authorization.");
        }

        if (!await permissionEnforcer.HasPermissionAsync(authorizedByUserId, PermissionKeys.PricingChangeSellingPrice, cancellationToken))
        {
            throw new InvalidOperationException("The authorizing user is not permitted to change selling prices.");
        }
    }
}
