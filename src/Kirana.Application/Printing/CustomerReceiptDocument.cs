namespace Kirana.Application.Printing;

/// <summary>
/// A printable Udhaar repayment receipt (PRD §31), built from an immutable
/// <c>CreditPayment</c> plus current <c>Store</c> settings. Mirrors
/// <see cref="InvoiceDocument"/>'s design: no <c>required</c> members, because the WinUI 3 XAML
/// compiler generates parameterless activators for every type reachable from a bound ViewModel.
/// </summary>
public sealed class CustomerReceiptDocument
{
    public int CreditPaymentId { get; init; }

    // Store header
    public string StoreName { get; init; } = string.Empty;
    public string? StoreAddress { get; init; }
    public string? StoreContactNumber { get; init; }
    public string? FooterText { get; init; }

    // Receipt header
    public string ReceiptNumber { get; init; } = string.Empty;
    public DateTime PaymentDateUtc { get; init; }
    public string? ReceivedByName { get; init; }

    // Customer
    public string CustomerCode { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }

    // Money
    public decimal AmountPaid { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string? ReferenceNumber { get; init; }
    public decimal BalanceBefore { get; init; }
    public decimal BalanceAfter { get; init; }
    public string? Notes { get; init; }

    /// <summary>Which invoices this payment settled, so the customer's copy shows exactly what was
    /// cleared rather than only a net figure.</summary>
    public IReadOnlyList<CustomerReceiptAllocationLine> Allocations { get; init; } = [];
}

/// <summary>One "this much went to that invoice" line on a repayment receipt.</summary>
public sealed class CustomerReceiptAllocationLine
{
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime SaleDateUtc { get; init; }
    public decimal AmountApplied { get; init; }
    public decimal RemainingOnThatCredit { get; init; }
}
