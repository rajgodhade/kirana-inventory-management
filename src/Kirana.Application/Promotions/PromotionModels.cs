using Kirana.Domain.Entities;

namespace Kirana.Application.Promotions;

public sealed class SavePromotionRequest
{
    public string PromotionCode { get; init; } = string.Empty;
    public string PromotionName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public PromotionType PromotionType { get; init; }
    public decimal? Percentage { get; init; }
    public decimal? FlatAmount { get; init; }
    public decimal? FixedPrice { get; init; }
    public int Priority { get; init; }
    public PromotionPriorityMode PriorityMode { get; init; } = PromotionPriorityMode.HighestDiscount;
    public DiscountCalculationMode CalculationMode { get; init; } = DiscountCalculationMode.BeforeTax;
    public bool AllowStacking { get; init; }
    public decimal? MaximumDiscount { get; init; }
    public decimal? MinimumBillAmount { get; init; }
    public decimal? MinimumQuantity { get; init; }
    public int? MaximumUsage { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public PromotionScopeType ScopeType { get; init; }
    public IReadOnlyList<int> TargetIds { get; init; } = [];
    public bool ActivateImmediately { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class PromotionSearchQuery
{
    public string? SearchText { get; init; }
    public PromotionStatus? Status { get; init; }
    public PromotionScopeType? ScopeType { get; init; }
    public PromotionType? PromotionType { get; init; }
    public bool RunningOnly { get; init; }
    public bool ExpiredOnly { get; init; }
    public bool UpcomingOnly { get; init; }
    public DateTime? ActiveOnUtc { get; init; }
    public int MaxResults { get; init; } = 500;
}

public sealed class PromotionSummary
{
    public int Total { get; init; }
    public int Running { get; init; }
    public int Upcoming { get; init; }
    public int Expired { get; init; }
    public int Disabled { get; init; }
}

public sealed class PromotionLineContext
{
    public int ProductId { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public sealed class PromotionCartContext
{
    public IReadOnlyList<PromotionLineContext> Lines { get; init; } = [];
    public decimal BillAmount { get; init; }
    public int? CustomerId { get; init; }
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class AppliedPromotionResult
{
    public int PromotionId { get; init; }
    public string PromotionCode { get; init; } = string.Empty;
    public string PromotionName { get; init; } = string.Empty;
    public PromotionType PromotionType { get; init; }
    public DiscountCalculationMode CalculationMode { get; init; }
    public decimal DiscountAmount { get; init; }
}

public sealed class PromotionLineResult
{
    public int ProductId { get; init; }
    public decimal OriginalUnitPrice { get; init; }
    public decimal FinalUnitPrice { get; init; }
    public decimal DiscountAmount { get; init; }
    public IReadOnlyList<AppliedPromotionResult> AppliedPromotions { get; init; } = [];
}

public sealed class PromotionPerformanceRow
{
    public int PromotionId { get; init; }
    public string PromotionCode { get; init; } = string.Empty;
    public string PromotionName { get; init; } = string.Empty;
    public decimal Revenue { get; init; }
    public decimal DiscountGiven { get; init; }
    public decimal ProductsSold { get; init; }
    public int SalesGenerated { get; init; }
}

public sealed class PromotionPreviewResult
{
    public decimal CurrentPrice { get; init; }
    public decimal FinalPrice { get; init; }
    public decimal Savings { get; init; }
}
