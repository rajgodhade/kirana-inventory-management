using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Promotions;

/// <summary>Single reusable promotion matcher and calculator. It has no UI or Sale dependencies;
/// both POS preview and SaleService call this same implementation.</summary>
public sealed class PromotionEngine(IKiranaDbContext db) : IPromotionEngine
{
    public async Task<IReadOnlyList<PromotionLineResult>> EvaluateCartAsync(PromotionCartContext context, CancellationToken cancellationToken = default)
    {
        if (context.Lines.Count == 0) return [];
        var productIds = context.Lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var promotions = await db.Promotions.AsNoTracking()
            .Include(x => x.Schedule).Include(x => x.Scope).ThenInclude(x => x!.Targets)
            .Where(x => x.IsActive).ToListAsync(cancellationToken);

        var results = new List<PromotionLineResult>(context.Lines.Count);
        foreach (var line in context.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || line.Quantity <= 0 || line.UnitPrice <= 0)
            {
                results.Add(new PromotionLineResult { ProductId = line.ProductId, OriginalUnitPrice = line.UnitPrice, FinalUnitPrice = line.UnitPrice });
                continue;
            }

            var eligible = promotions.Where(p => IsEligible(p, product, line, context)).Select(p => new Candidate
            {
                Promotion = p,
                Discount = PromotionValueCalculator.CalculateDiscount(p, line.UnitPrice, line.Quantity),
            }).Where(x => x.Discount > 0).ToList();

            var preferPriority = eligible.Any(x => x.Promotion.PriorityMode == PromotionPriorityMode.HighestPriority);
            var ordered = preferPriority
                ? eligible.OrderByDescending(x => x.Promotion.Priority).ThenByDescending(x => x.Discount).ThenBy(x => x.Promotion.Id).ToList()
                : eligible.OrderByDescending(x => x.Discount).ThenByDescending(x => x.Promotion.Priority).ThenBy(x => x.Promotion.Id).ToList();

            var selected = ordered.Count == 0 ? []
                : ordered[0].Promotion.AllowStacking ? ordered.Where(x => x.Promotion.AllowStacking).ToList()
                : [ordered[0]];
            decimal remainingTotal = Round(line.UnitPrice * line.Quantity);
            var applied = new List<AppliedPromotionResult>();
            foreach (var candidate in selected)
            {
                var discount = PromotionValueCalculator.CalculateDiscount(candidate.Promotion, remainingTotal / line.Quantity, line.Quantity);
                discount = Math.Min(discount, remainingTotal);
                if (discount <= 0) continue;
                remainingTotal = Round(remainingTotal - discount);
                applied.Add(new AppliedPromotionResult
                {
                    PromotionId = candidate.Promotion.Id,
                    PromotionCode = candidate.Promotion.PromotionCode,
                    PromotionName = candidate.Promotion.PromotionName,
                    PromotionType = candidate.Promotion.PromotionType,
                    CalculationMode = candidate.Promotion.CalculationMode,
                    DiscountAmount = discount,
                });
            }

            var originalTotal = Round(line.UnitPrice * line.Quantity);
            results.Add(new PromotionLineResult
            {
                ProductId = line.ProductId,
                OriginalUnitPrice = line.UnitPrice,
                FinalUnitPrice = line.Quantity == 0 ? line.UnitPrice : Round(remainingTotal / line.Quantity),
                DiscountAmount = Round(originalTotal - remainingTotal),
                AppliedPromotions = applied,
            });
        }
        return results;
    }

    private static bool IsEligible(Promotion promotion, Product product, PromotionLineContext line, PromotionCartContext context)
    {
        if (PromotionStatusCalculator.Calculate(promotion, context.AtUtc) != PromotionStatus.Running) return false;
        if (promotion.MinimumBillAmount is { } minimumBill && context.BillAmount < minimumBill) return false;
        if (promotion.MinimumQuantity is { } minimumQuantity && line.Quantity < minimumQuantity) return false;
        var scope = promotion.Scope;
        if (scope is null) return false;
        return scope.ScopeType switch
        {
            PromotionScopeType.EntireStore => true,
            PromotionScopeType.Category => product.CategoryId is { } id && scope.Targets.Any(x => x.CategoryId == id),
            PromotionScopeType.Brand => product.BrandId is { } id && scope.Targets.Any(x => x.BrandId == id),
            PromotionScopeType.Product => scope.Targets.Any(x => x.ProductId == product.Id),
            _ => false,
        };
    }

    private static decimal Round(decimal value) => PromotionValueCalculator.Round(value);
    private sealed class Candidate
    {
        public required Promotion Promotion { get; init; }
        public decimal Discount { get; init; }
    }
}

public static class PromotionValueCalculator
{
    public static decimal CalculateDiscount(Promotion promotion, decimal unitPrice, decimal quantity)
    {
        var total = Round(unitPrice * quantity);
        var discount = promotion.PromotionType switch
        {
            PromotionType.Percentage => Round(total * (promotion.Percentage ?? 0) / 100m),
            PromotionType.FlatAmount => Round((promotion.FlatAmount ?? 0) * quantity),
            PromotionType.FixedSellingPrice => Round(Math.Max(0, unitPrice - (promotion.FixedPrice ?? unitPrice)) * quantity),
            _ => 0,
        };
        if (promotion.MaximumDiscount is { } maximum) discount = Math.Min(discount, maximum);
        return Math.Min(discount, total);
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
