namespace Kirana.Application.Printing;

/// <summary>
/// A printable sales-return / refund receipt (PRD §33). Built from the immutable
/// <c>SalesReturn</c> record plus current <c>Store</c> settings. Like the other document types it
/// has no <c>required</c> members — the WinUI 3 XAML compiler generates parameterless activators
/// for every type reachable from a bound ViewModel and fails opaquely otherwise.
/// </summary>
public sealed class ReturnReceiptDocument
{
    public int SalesReturnId { get; init; }

    // Store header
    public string StoreName { get; init; } = string.Empty;
    public string? StoreAddress { get; init; }
    public string? StoreContactNumber { get; init; }
    public string? FooterText { get; init; }

    // Return header
    public string ReturnNumber { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime ReturnDateUtc { get; init; }
    public string? ProcessedByName { get; init; }

    // Customer (optional — walk-in returns have none)
    public string? CustomerName { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerPhone { get; init; }

    // Money
    public decimal TotalReturnAmount { get; init; }
    public decimal RefundAmount { get; init; }
    public string RefundMethod { get; init; } = string.Empty;
    public string? ReferenceNumber { get; init; }
    public string? Reason { get; init; }
    public string? Notes { get; init; }

    /// <summary>True when nothing was refunded — the slip then reads as an exchange/adjustment
    /// record rather than a refund receipt.</summary>
    public bool IsRefund => RefundAmount > 0;

    public IReadOnlyList<ReturnReceiptLine> Lines { get; init; } = [];
}

public sealed class ReturnReceiptLine
{
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineRefundAmount { get; init; }

    /// <summary>"Returned to stock" or "Damaged" — printed so the customer's copy and the shop's
    /// copy agree on what happened to the goods.</summary>
    public string Disposition { get; init; } = string.Empty;
}
