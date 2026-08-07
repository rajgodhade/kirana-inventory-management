using Kirana.Domain.Entities;

namespace Kirana.Application.Promotions;

public interface IPromotionService
{
    Task<Promotion> CreateAsync(SavePromotionRequest request, CancellationToken cancellationToken = default);
    Task<Promotion> UpdateAsync(int promotionId, SavePromotionRequest request, CancellationToken cancellationToken = default);
    Task SetActiveAsync(int promotionId, bool active, int? performedByUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int promotionId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<Promotion?> GetByIdAsync(int promotionId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Promotion>> SearchAsync(PromotionSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<PromotionSummary> GetSummaryAsync(int? performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromotionPerformanceRow>> GetPerformanceAsync(DateTime fromUtc, DateTime toUtc, int? performedByUserId, CancellationToken cancellationToken = default);
    PromotionPreviewResult Preview(SavePromotionRequest request, decimal currentPrice, decimal quantity = 1);
}

public interface IPromotionEngine
{
    Task<IReadOnlyList<PromotionLineResult>> EvaluateCartAsync(PromotionCartContext context, CancellationToken cancellationToken = default);
}
