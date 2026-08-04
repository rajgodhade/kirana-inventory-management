using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A store expense (PRD §32). Deliberately has no link to products, inventory or stock movements —
/// paying the electricity bill must never touch stock. Feeds profitability reporting in a later
/// phase.
/// </summary>
public class Expense : Entity
{
    public string ExpenseNumber { get; set; } = string.Empty;

    public DateTime ExpenseDateUtc { get; set; } = DateTime.UtcNow;

    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory ExpenseCategory { get; set; } = null!;

    /// <summary>Snapshot of the category name at the time of entry, so renaming a category later
    /// does not silently rewrite what past receipts say.</summary>
    public string CategoryNameSnapshot { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    public string? Description { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
