using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>One cart line inside a <see cref="HeldBill"/> — resumed against the product's
/// *current* price/GST, since the bill isn't final yet.</summary>
public class HeldBillItem : Entity
{
    public int HeldBillId { get; set; }
    public HeldBill HeldBill { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal DiscountPercent { get; set; }
}
