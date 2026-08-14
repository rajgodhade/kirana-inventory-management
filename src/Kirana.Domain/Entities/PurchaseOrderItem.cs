using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public class PurchaseOrderItem : Entity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public string? HsnCodeSnapshot { get; set; }
    public string UnitSnapshot { get; set; } = string.Empty;
    public PricingType PricingTypeSnapshot { get; set; }
    public decimal GstRatePercentSnapshot { get; set; }

    public decimal OrderedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }
    public ICollection<GoodsReceiptItem> GoodsReceiptItems { get; set; } = new List<GoodsReceiptItem>();
}
