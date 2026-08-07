using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public class Brand : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<PromotionTarget> PromotionTargets { get; set; } = new List<PromotionTarget>();
}
