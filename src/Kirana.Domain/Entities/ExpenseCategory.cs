using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A configurable expense heading — Rent, Electricity, Salary and so on (PRD §32). Seeded with a
/// default set on first run, then fully editable by the shopkeeper.
/// </summary>
public class ExpenseCategory : Entity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Marks the categories seeded by the app. They can be renamed or deactivated, but
    /// deleting them is refused so a fresh install and an upgraded one behave the same.</summary>
    public bool IsSystemDefault { get; set; }

    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
