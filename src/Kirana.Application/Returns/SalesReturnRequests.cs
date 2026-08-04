using Kirana.Domain.Entities;

namespace Kirana.Application.Returns;

/// <summary>One line the user wants to return, identified by the original sale line.</summary>
public sealed class SalesReturnLineInput
{
    public int SaleItemId { get; init; }
    public decimal Quantity { get; init; }
    public ReturnDisposition Disposition { get; init; } = ReturnDisposition.ReturnToStock;

    /// <summary>Batch the goods go back into. Only meaningful for
    /// <see cref="ReturnDisposition.ReturnToStock"/> on a batch-tracked product.</summary>
    public string? BatchNumber { get; init; }

    public string? Reason { get; init; }
}

public sealed class CreateSalesReturnRequest
{
    public int SaleId { get; init; }
    public IReadOnlyList<SalesReturnLineInput> Lines { get; init; } = [];

    public RefundMethod RefundMethod { get; init; } = RefundMethod.Cash;

    /// <summary>Amount actually refunded. Null means "refund the full value of the returned goods";
    /// it is ignored for <see cref="RefundMethod.None"/>, which always refunds zero.</summary>
    public decimal? RefundAmount { get; init; }

    public string? ReferenceNumber { get; init; }
    public string? Reason { get; init; }
    public string? Notes { get; init; }

    public int? ProcessedByUserId { get; init; }

    /// <summary>Manager/Owner who approved the refund, when step-up authorization was used.</summary>
    public int? AuthorizedByUserId { get; init; }
}

/// <summary>
/// A sale line with the quantity that is still returnable — original quantity minus everything
/// already returned across all previous returns against that line.
/// </summary>
public sealed class ReturnableLine
{
    public int SaleItemId { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public bool TracksBatches { get; init; }
    public decimal SoldQuantity { get; init; }
    public decimal AlreadyReturnedQuantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }

    public decimal ReturnableQuantity => SoldQuantity - AlreadyReturnedQuantity;

    public bool IsFullyReturned => ReturnableQuantity <= 0;
}

/// <summary>The original sale plus per-line returnable quantities — everything the Sales Return
/// screen needs to let the user pick what is coming back.</summary>
public sealed class ReturnableSale
{
    public int SaleId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime SaleDateUtc { get; init; }
    public int? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerCode { get; init; }
    public decimal GrandTotal { get; init; }
    public IReadOnlyList<ReturnableLine> Lines { get; init; } = [];

    public bool HasAnythingReturnable => Lines.Any(l => !l.IsFullyReturned);
}

/// <summary>
/// How the user is looking for the sale to return against (PRD §33): invoice number, a scanned
/// barcode, a product, or a customer.
/// </summary>
public sealed class SaleLookupQuery
{
    /// <summary>Invoice number, barcode, product code/SKU/name, or customer code/phone/name. The
    /// service decides which by shape, exactly like the POS search does.</summary>
    public string? SearchText { get; init; }

    public int? CustomerId { get; init; }

    public int MaxResults { get; init; } = 25;
}

/// <summary>Filters for the Sales Returns list screen.</summary>
public sealed class SalesReturnSearchQuery
{
    public string? SearchText { get; init; }
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public int MaxResults { get; init; } = 100;
}
