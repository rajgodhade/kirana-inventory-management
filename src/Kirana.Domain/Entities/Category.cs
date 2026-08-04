using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public class Category : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
