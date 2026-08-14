using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public sealed class GoodsReceiptItem : Entity
{
    public int GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;
    public int PurchaseOrderItemId { get; set; }
    public PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string ProductCodeSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public UnitOfMeasure UnitSnapshot { get; set; }
    public decimal OrderedQuantitySnapshot { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string? BarcodeSnapshot { get; set; }
    public string? Notes { get; set; }
}
