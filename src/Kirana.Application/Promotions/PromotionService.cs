using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Promotions;

public sealed class PromotionService(IKiranaDbContext db, IAuditLogger auditLogger, IPermissionEnforcer permissions) : IPromotionService
{
    public PromotionPreviewResult Preview(SavePromotionRequest request, decimal currentPrice, decimal quantity = 1)
    {
        if (currentPrice < 0 || quantity <= 0) throw new ArgumentException("Preview price and quantity must be valid.");
        var promotion = new Promotion
        {
            PromotionType = request.PromotionType, Percentage = request.Percentage,
            FlatAmount = request.FlatAmount, FixedPrice = request.FixedPrice, MaximumDiscount = request.MaximumDiscount,
        };
        var savings = PromotionValueCalculator.CalculateDiscount(promotion, currentPrice, quantity);
        return new PromotionPreviewResult
        {
            CurrentPrice = currentPrice * quantity,
            Savings = savings,
            FinalPrice = Math.Max(0, currentPrice * quantity - savings),
        };
    }

    public async Task<Promotion> CreateAsync(SavePromotionRequest request, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.PromotionsManage, cancellationToken);
        await ValidateAsync(request, null, cancellationToken);

        var promotion = new Promotion { CreatedByUserId = request.PerformedByUserId };
        Apply(promotion, request);
        db.Promotions.Add(promotion);
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "PromotionCreated", nameof(Promotion), promotion.Id.ToString(),
            newValue: $"{promotion.PromotionCode} - {promotion.PromotionName}", cancellationToken: cancellationToken);
        return promotion;
    }

    public async Task<Promotion> UpdateAsync(int promotionId, SavePromotionRequest request, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.PromotionsManage, cancellationToken);
        var promotion = await QueryTracked().FirstOrDefaultAsync(x => x.Id == promotionId, cancellationToken)
            ?? throw new InvalidOperationException("Promotion not found.");
        await ValidateAsync(request, promotionId, cancellationToken);
        var previous = $"{promotion.PromotionCode} - {promotion.PromotionName}";
        db.PromotionTargets.RemoveRange(promotion.Scope?.Targets ?? []);
        Apply(promotion, request);
        promotion.UpdatedByUserId = request.PerformedByUserId;
        promotion.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "PromotionUpdated", nameof(Promotion), promotion.Id.ToString(),
            previousValue: previous, newValue: $"{promotion.PromotionCode} - {promotion.PromotionName}", cancellationToken: cancellationToken);
        return promotion;
    }

    public async Task SetActiveAsync(int promotionId, bool active, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PromotionsManage, cancellationToken);
        var promotion = await QueryTracked().FirstOrDefaultAsync(x => x.Id == promotionId, cancellationToken)
            ?? throw new InvalidOperationException("Promotion not found.");
        if (active)
        {
            await ValidateAsync(ToRequest(promotion, performedByUserId, activate: true), promotionId, cancellationToken);
        }
        promotion.IsActive = active;
        promotion.Status = active ? PromotionStatusCalculator.Calculate(promotion, DateTime.UtcNow) : PromotionStatus.Disabled;
        promotion.UpdatedByUserId = performedByUserId;
        promotion.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(performedByUserId, active ? "PromotionActivated" : "PromotionDeactivated",
            nameof(Promotion), promotion.Id.ToString(), newValue: promotion.PromotionCode, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(int promotionId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PromotionsManage, cancellationToken);
        if (await db.SaleItemPromotions.AnyAsync(x => x.PromotionId == promotionId, cancellationToken))
            throw new InvalidOperationException("A promotion already used on a sale cannot be deleted. Disable it to preserve receipt history.");
        var promotion = await db.Promotions.FirstOrDefaultAsync(x => x.Id == promotionId, cancellationToken)
            ?? throw new InvalidOperationException("Promotion not found.");
        var previous = $"{promotion.PromotionCode} - {promotion.PromotionName}";
        db.Promotions.Remove(promotion);
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(performedByUserId, "PromotionDeleted", nameof(Promotion), promotionId.ToString(),
            previousValue: previous, cancellationToken: cancellationToken);
    }

    public async Task<Promotion?> GetByIdAsync(int promotionId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PromotionsView, cancellationToken);
        var promotion = await QueryReadOnly().FirstOrDefaultAsync(x => x.Id == promotionId, cancellationToken);
        if (promotion is not null) promotion.Status = PromotionStatusCalculator.Calculate(promotion, DateTime.UtcNow);
        return promotion;
    }

    public async Task<IReadOnlyList<Promotion>> SearchAsync(PromotionSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PromotionsView, cancellationToken);
        var promotions = await QueryReadOnly().ToListAsync(cancellationToken);
        var now = query.ActiveOnUtc ?? DateTime.UtcNow;
        foreach (var promotion in promotions) promotion.Status = PromotionStatusCalculator.Calculate(promotion, now);

        IEnumerable<Promotion> filtered = promotions;
        var text = query.SearchText?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            filtered = filtered.Where(p => p.PromotionName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.PromotionCode.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (p.Description?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.Scope!.Targets.Any(t => (t.Category?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (t.Brand?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (t.Product?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)));
        }
        if (query.ActiveOnUtc is { } activeOn) filtered = filtered.Where(p => p.Schedule is not null && p.Schedule.StartAtUtc <= activeOn && p.Schedule.EndAtUtc > activeOn);
        if (query.Status is { } status) filtered = filtered.Where(p => p.Status == status);
        if (query.ScopeType is { } scope) filtered = filtered.Where(p => p.Scope!.ScopeType == scope);
        if (query.PromotionType is { } type) filtered = filtered.Where(p => p.PromotionType == type);
        if (query.RunningOnly) filtered = filtered.Where(p => p.Status == PromotionStatus.Running);
        if (query.ExpiredOnly) filtered = filtered.Where(p => p.Status == PromotionStatus.Expired);
        if (query.UpcomingOnly) filtered = filtered.Where(p => p.Status == PromotionStatus.Scheduled);
        return filtered.OrderByDescending(p => p.Status == PromotionStatus.Running).ThenByDescending(p => p.Priority)
            .ThenBy(p => p.PromotionName).Take(Math.Clamp(query.MaxResults, 1, 2000)).ToList();
    }

    public async Task<PromotionSummary> GetSummaryAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var all = await SearchAsync(new PromotionSearchQuery { MaxResults = 2000 }, performedByUserId, cancellationToken);
        return new PromotionSummary
        {
            Total = all.Count,
            Running = all.Count(x => x.Status == PromotionStatus.Running),
            Upcoming = all.Count(x => x.Status == PromotionStatus.Scheduled),
            Expired = all.Count(x => x.Status == PromotionStatus.Expired),
            Disabled = all.Count(x => x.Status == PromotionStatus.Disabled),
        };
    }

    public async Task<IReadOnlyList<PromotionPerformanceRow>> GetPerformanceAsync(DateTime fromUtc, DateTime toUtc, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);
        return await db.SaleItemPromotions.AsNoTracking()
            .Where(x => x.SaleItem.Sale.SaleDateUtc >= fromUtc && x.SaleItem.Sale.SaleDateUtc < toUtc)
            .GroupBy(x => new { x.PromotionId, x.PromotionCodeSnapshot, x.PromotionNameSnapshot })
            .Select(g => new PromotionPerformanceRow
            {
                PromotionId = g.Key.PromotionId,
                PromotionCode = g.Key.PromotionCodeSnapshot,
                PromotionName = g.Key.PromotionNameSnapshot,
                Revenue = g.Sum(x => x.SaleItem.LineTotal),
                DiscountGiven = g.Sum(x => x.DiscountAmount),
                ProductsSold = g.Sum(x => x.SaleItem.Quantity),
                SalesGenerated = g.Select(x => x.SaleItem.SaleId).Distinct().Count(),
            }).OrderByDescending(x => x.Revenue).ToListAsync(cancellationToken);
    }

    private async Task ValidateAsync(SavePromotionRequest request, int? existingId, CancellationToken cancellationToken)
    {
        var code = request.PromotionCode.Trim().ToUpperInvariant();
        if (code.Length == 0) throw new ArgumentException("Promotion code is required.");
        if (string.IsNullOrWhiteSpace(request.PromotionName)) throw new ArgumentException("Promotion name is required.");
        if (request.EndAtUtc <= request.StartAtUtc) throw new ArgumentException("Promotion end must be after its start.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
        catch { throw new ArgumentException("The selected timezone is not available on this computer."); }
        if (request.Priority < 0) throw new ArgumentException("Priority cannot be negative.");
        if (request.MinimumBillAmount is < 0 || request.MinimumQuantity is < 0 || request.MaximumDiscount is < 0)
            throw new ArgumentException("Promotion conditions cannot be negative.");
        if (request.MaximumUsage is <= 0) throw new ArgumentException("Maximum usage must be greater than zero.");
        if (request.ScopeType != PromotionScopeType.EntireStore && request.TargetIds.Count == 0)
            throw new ArgumentException("Select at least one promotion target.");
        if (request.ScopeType == PromotionScopeType.EntireStore && request.TargetIds.Count != 0)
            throw new ArgumentException("An entire-store promotion cannot have individual targets.");
        if (await db.Promotions.AnyAsync(x => x.PromotionCode == code && x.Id != existingId, cancellationToken))
            throw new InvalidOperationException($"Promotion code '{code}' already exists.");

        switch (request.PromotionType)
        {
            case PromotionType.Percentage when request.Percentage is null or <= 0 or >= 100:
                throw new ArgumentException("Percentage discount must be greater than 0 and less than 100.");
            case PromotionType.FlatAmount when request.FlatAmount is null or <= 0:
                throw new ArgumentException("Flat discount must be greater than zero.");
            case PromotionType.FixedSellingPrice when request.FixedPrice is null or <= 0:
                throw new ArgumentException("Fixed selling price must be greater than zero.");
        }

        await ValidateTargetIdsAsync(request, cancellationToken);
        var targetProducts = await ResolveTargetProductsAsync(request, cancellationToken);
        if (request.PromotionType == PromotionType.FlatAmount && targetProducts.Count > 0
            && targetProducts.Any(x => request.FlatAmount >= x.SellingPrice))
            throw new ArgumentException("Flat discount must be smaller than every targeted product's selling price.");
        if (request.PromotionType == PromotionType.FixedSellingPrice && targetProducts.Count > 0
            && targetProducts.Any(x => request.FixedPrice >= x.SellingPrice))
            throw new ArgumentException("Fixed price must be lower than every targeted product's selling price.");

        if (!request.ActivateImmediately) return;
        var overlapping = await QueryReadOnly().Where(x => x.Id != existingId && x.IsActive
            && x.Schedule!.StartAtUtc < request.EndAtUtc && request.StartAtUtc < x.Schedule.EndAtUtc).ToListAsync(cancellationToken);
        foreach (var existing in overlapping.Where(x => !request.AllowStacking || !x.AllowStacking))
        {
            if (await ScopesConflictAsync(request, existing, targetProducts, cancellationToken))
                throw new InvalidOperationException("This active promotion conflicts with a non-stacking promotion in the same schedule and scope.");
        }
    }

    private async Task ValidateTargetIdsAsync(SavePromotionRequest request, CancellationToken cancellationToken)
    {
        var ids = request.TargetIds.Distinct().ToList();
        var existingCount = request.ScopeType switch
        {
            PromotionScopeType.Category => await db.Categories.CountAsync(x => ids.Contains(x.Id), cancellationToken),
            PromotionScopeType.Brand => await db.Brands.CountAsync(x => ids.Contains(x.Id), cancellationToken),
            PromotionScopeType.Product => await db.Products.CountAsync(x => ids.Contains(x.Id), cancellationToken),
            _ => 0,
        };
        if (request.ScopeType != PromotionScopeType.EntireStore && existingCount != ids.Count)
            throw new InvalidOperationException("One or more selected promotion targets no longer exist.");
    }

    private async Task<List<Product>> ResolveTargetProductsAsync(SavePromotionRequest request, CancellationToken cancellationToken)
    {
        var ids = request.TargetIds.Distinct().ToList();
        return request.ScopeType switch
        {
            PromotionScopeType.Product => await db.Products.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken),
            PromotionScopeType.Category => await db.Products.Where(x => x.CategoryId != null && ids.Contains(x.CategoryId.Value)).ToListAsync(cancellationToken),
            PromotionScopeType.Brand => await db.Products.Where(x => x.BrandId != null && ids.Contains(x.BrandId.Value)).ToListAsync(cancellationToken),
            PromotionScopeType.EntireStore => await db.Products.Where(x => x.IsActive).ToListAsync(cancellationToken),
            _ => [],
        };
    }

    private async Task<bool> ScopesConflictAsync(SavePromotionRequest request, Promotion existing, IReadOnlyList<Product> requestedProducts, CancellationToken cancellationToken)
    {
        if (request.ScopeType == PromotionScopeType.EntireStore || existing.Scope!.ScopeType == PromotionScopeType.EntireStore) return true;
        var existingTargetIds = existing.Scope.Targets.Select(t => t.CategoryId ?? t.BrandId ?? t.ProductId ?? 0).Where(x => x > 0).ToList();
        var existingProductIds = existing.Scope.ScopeType switch
        {
            PromotionScopeType.Category => await db.Products.Where(x => x.CategoryId != null && existingTargetIds.Contains(x.CategoryId.Value)).Select(x => x.Id).ToListAsync(cancellationToken),
            PromotionScopeType.Brand => await db.Products.Where(x => x.BrandId != null && existingTargetIds.Contains(x.BrandId.Value)).Select(x => x.Id).ToListAsync(cancellationToken),
            PromotionScopeType.Product => existingTargetIds,
            _ => [],
        };
        return requestedProducts.Select(x => x.Id).Intersect(existingProductIds).Any();
    }

    private static void Apply(Promotion promotion, SavePromotionRequest request)
    {
        promotion.PromotionCode = request.PromotionCode.Trim().ToUpperInvariant();
        promotion.PromotionName = request.PromotionName.Trim();
        promotion.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        promotion.PromotionType = request.PromotionType;
        promotion.Percentage = request.PromotionType == PromotionType.Percentage ? request.Percentage : null;
        promotion.FlatAmount = request.PromotionType == PromotionType.FlatAmount ? request.FlatAmount : null;
        promotion.FixedPrice = request.PromotionType == PromotionType.FixedSellingPrice ? request.FixedPrice : null;
        promotion.Priority = request.Priority;
        promotion.PriorityMode = request.PriorityMode;
        promotion.CalculationMode = request.CalculationMode;
        promotion.AllowStacking = request.AllowStacking;
        promotion.MaximumDiscount = request.MaximumDiscount;
        promotion.MinimumBillAmount = request.MinimumBillAmount;
        promotion.MinimumQuantity = request.MinimumQuantity;
        promotion.MaximumUsage = request.MaximumUsage;
        promotion.IsActive = request.ActivateImmediately;
        promotion.Schedule ??= new PromotionSchedule();
        promotion.Schedule.StartAtUtc = DateTime.SpecifyKind(request.StartAtUtc, DateTimeKind.Utc);
        promotion.Schedule.EndAtUtc = DateTime.SpecifyKind(request.EndAtUtc, DateTimeKind.Utc);
        promotion.Schedule.TimeZoneId = request.TimeZoneId;
        promotion.Status = request.ActivateImmediately ? PromotionStatusCalculator.Calculate(promotion, DateTime.UtcNow) : PromotionStatus.Draft;
        promotion.Scope ??= new PromotionScope();
        promotion.Scope.ScopeType = request.ScopeType;
        promotion.Scope.Targets.Clear();
        foreach (var id in request.TargetIds.Distinct())
        {
            promotion.Scope.Targets.Add(new PromotionTarget
            {
                CategoryId = request.ScopeType == PromotionScopeType.Category ? id : null,
                BrandId = request.ScopeType == PromotionScopeType.Brand ? id : null,
                ProductId = request.ScopeType == PromotionScopeType.Product ? id : null,
            });
        }
    }

    private static SavePromotionRequest ToRequest(Promotion p, int? userId, bool activate) => new()
    {
        PromotionCode = p.PromotionCode, PromotionName = p.PromotionName, Description = p.Description,
        PromotionType = p.PromotionType, Percentage = p.Percentage, FlatAmount = p.FlatAmount, FixedPrice = p.FixedPrice,
        Priority = p.Priority, PriorityMode = p.PriorityMode, CalculationMode = p.CalculationMode, AllowStacking = p.AllowStacking,
        MaximumDiscount = p.MaximumDiscount, MinimumBillAmount = p.MinimumBillAmount, MinimumQuantity = p.MinimumQuantity,
        MaximumUsage = p.MaximumUsage, StartAtUtc = p.Schedule!.StartAtUtc, EndAtUtc = p.Schedule.EndAtUtc,
        TimeZoneId = p.Schedule.TimeZoneId, ScopeType = p.Scope!.ScopeType,
        TargetIds = p.Scope.Targets.Select(t => t.CategoryId ?? t.BrandId ?? t.ProductId ?? 0).Where(x => x != 0).ToList(),
        ActivateImmediately = activate, PerformedByUserId = userId,
    };

    private IQueryable<Promotion> QueryTracked() => db.Promotions
        .Include(x => x.Schedule).Include(x => x.Scope).ThenInclude(x => x!.Targets);
    private IQueryable<Promotion> QueryReadOnly() => QueryTracked().AsNoTracking()
        .Include(x => x.Scope!).ThenInclude(x => x.Targets).ThenInclude(x => x.Category)
        .Include(x => x.Scope!).ThenInclude(x => x.Targets).ThenInclude(x => x.Brand)
        .Include(x => x.Scope!).ThenInclude(x => x.Targets).ThenInclude(x => x.Product);
}
