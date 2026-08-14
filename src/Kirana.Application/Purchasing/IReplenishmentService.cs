namespace Kirana.Application.Purchasing;

/// <summary>Read-only current-state procurement planning. Implementations must never persist a
/// recommendation or create a procurement document.</summary>
public interface IReplenishmentService
{
    Task<ReplenishmentSummary> GetRecommendationsAsync(
        ReplenishmentQuery query, int? performedByUserId, CancellationToken cancellationToken = default);
}

