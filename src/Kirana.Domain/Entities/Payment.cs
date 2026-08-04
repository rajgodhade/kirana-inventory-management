using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>One tender applied to a sale (PRD §20). A sale can have several rows (split payment).</summary>
public class Payment : Entity
{
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }

    /// <summary>Cash tenders only — what the customer physically handed over.</summary>
    public decimal? AmountTendered { get; set; }

    /// <summary>Cash tenders only — <see cref="AmountTendered"/> minus <see cref="Amount"/>.</summary>
    public decimal? ChangeGiven { get; set; }
}
