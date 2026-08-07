using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>Immutable promotion snapshot for one completed-sale line. Several rows on one item are
/// the durable representation of stacking.</summary>
public class SaleItemPromotion : Entity
{
    public int SaleItemId { get; set; }
    public SaleItem SaleItem { get; set; } = null!;
    public int PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public string PromotionCodeSnapshot { get; set; } = string.Empty;
    public string PromotionNameSnapshot { get; set; } = string.Empty;
    public PromotionType PromotionTypeSnapshot { get; set; }
    public DiscountCalculationMode CalculationModeSnapshot { get; set; }
    public decimal DiscountAmount { get; set; }
}
