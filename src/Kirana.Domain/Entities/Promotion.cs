using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>A reusable, scheduled price benefit. Targeting and sale-time applications are kept in
/// separate rows so a promotion can cover many products and can be reported historically.</summary>
public class Promotion : Entity
{
    public string PromotionCode { get; set; } = string.Empty;
    public string PromotionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PromotionType PromotionType { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? FlatAmount { get; set; }
    public decimal? FixedPrice { get; set; }
    public int Priority { get; set; }
    public PromotionPriorityMode PriorityMode { get; set; } = PromotionPriorityMode.HighestDiscount;
    public DiscountCalculationMode CalculationMode { get; set; } = DiscountCalculationMode.BeforeTax;
    public bool AllowStacking { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public decimal? MinimumBillAmount { get; set; }
    public decimal? MinimumQuantity { get; set; }
    public int? MaximumUsage { get; set; }
    public int CurrentUsage { get; set; }
    public bool IsActive { get; set; }
    public PromotionStatus Status { get; set; } = PromotionStatus.Draft;
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }

    public PromotionSchedule? Schedule { get; set; }
    public PromotionScope? Scope { get; set; }
    public ICollection<PromotionRule> Rules { get; set; } = new List<PromotionRule>();
    public ICollection<SaleItemPromotion> SaleApplications { get; set; } = new List<SaleItemPromotion>();
}
