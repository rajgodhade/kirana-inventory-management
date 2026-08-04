using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A full or partial return against a completed <see cref="Sale"/> (PRD §33).
///
/// The originating sale is never modified — a return is an additional record that references it,
/// exactly as a <see cref="CreditPayment"/> never alters the invoice it settles. Multiple returns
/// can exist against one sale; the sum of returned quantities per line is what caps further
/// returns, not this row alone.
/// </summary>
public class SalesReturn : Entity
{
    public string ReturnNumber { get; set; } = string.Empty;

    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    /// <summary>Snapshot of the invoice number so a return prints correctly without a live join.</summary>
    public string InvoiceNumberSnapshot { get; set; } = string.Empty;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime ReturnDateUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Total value of the returned goods, before deciding how (or whether) to refund.</summary>
    public decimal TotalReturnAmount { get; set; }

    /// <summary>What was actually given back. Zero when <see cref="RefundMethod"/> is
    /// <see cref="RefundMethod.None"/>; may be less than <see cref="TotalReturnAmount"/>.</summary>
    public decimal RefundAmount { get; set; }

    public RefundMethod RefundMethod { get; set; } = RefundMethod.Cash;

    public string? ReferenceNumber { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }

    public int? ProcessedByUserId { get; set; }
    public User? ProcessedByUser { get; set; }

    /// <summary>Manager/Owner who approved the refund, where step-up authorization was required
    /// (PRD §33: "Refunds can require Manager/Admin authorization").</summary>
    public int? AuthorizedByUserId { get; set; }
    public User? AuthorizedByUser { get; set; }

    public ICollection<SalesReturnItem> Items { get; set; } = new List<SalesReturnItem>();
}
