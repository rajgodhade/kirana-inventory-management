using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public class PromotionScope : Entity
{
    public int PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public PromotionScopeType ScopeType { get; set; }
    public ICollection<PromotionTarget> Targets { get; set; } = new List<PromotionTarget>();
}
