using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>Exactly one target FK is populated, matching the owning scope type.</summary>
public class PromotionTarget : Entity
{
    public int PromotionScopeId { get; set; }
    public PromotionScope PromotionScope { get; set; } = null!;
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
}
