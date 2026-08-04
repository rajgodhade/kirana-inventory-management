namespace Kirana.Application.Returns;

public sealed class PurchaseReturnLineInput
{
    public int PurchaseItemId { get; init; }
    public decimal Quantity { get; init; }

    /// <summary>Batch the goods are taken out of, for batch-tracked products.</summary>
    public string? BatchNumber { get; init; }

    public string? Reason { get; init; }
}

public sealed class CreatePurchaseReturnRequest
{
    public int PurchaseId { get; init; }
    public IReadOnlyList<PurchaseReturnLineInput> Lines { get; init; } = [];

    public string? Reason { get; init; }
    public string? Notes { get; init; }

    public int? ProcessedByUserId { get; init; }
}

/// <summary>A purchase line with the quantity still returnable to the supplier.</summary>
public sealed class ReturnablePurchaseLine
{
    public int PurchaseItemId { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public bool TracksBatches { get; init; }
    public string? BatchNumber { get; init; }
    public decimal ReceivedQuantity { get; init; }
    public decimal AlreadyReturnedQuantity { get; init; }
    public decimal StockOnHand { get; init; }
    public decimal PurchasePrice { get; init; }
    public decimal LineTotal { get; init; }

    public decimal ReturnableQuantity => ReceivedQuantity - AlreadyReturnedQuantity;

    public bool IsFullyReturned => ReturnableQuantity <= 0;
}

public sealed class ReturnablePurchase
{
    public int PurchaseId { get; init; }
    public string PurchaseNumber { get; init; } = string.Empty;
    public string? SupplierInvoiceNumber { get; init; }
    public DateTime PurchaseDateUtc { get; init; }
    public int SupplierId { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string SupplierCode { get; init; } = string.Empty;
    public decimal GrandTotal { get; init; }
    public IReadOnlyList<ReturnablePurchaseLine> Lines { get; init; } = [];

    public bool HasAnythingReturnable => Lines.Any(l => !l.IsFullyReturned);
}

public sealed class PurchaseReturnSearchQuery
{
    public string? SearchText { get; init; }
    public int? SupplierId { get; init; }
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public int MaxResults { get; init; } = 100;
}
