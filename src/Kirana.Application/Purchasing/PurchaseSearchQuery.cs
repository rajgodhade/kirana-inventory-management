namespace Kirana.Application.Purchasing;

/// <summary>Filters for the purchase list/search screen (PRD §28).</summary>
public sealed class PurchaseSearchQuery
{
    public int? SupplierId { get; init; }
    public string? SearchText { get; init; } // PurchaseNumber or SupplierInvoiceNumber
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public bool OutstandingOnly { get; init; }
    public int MaxResults { get; init; } = 200;
}
