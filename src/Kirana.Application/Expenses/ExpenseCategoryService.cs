using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Expenses;

public sealed class ExpenseCategoryService(
    IKiranaDbContext db, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : IExpenseCategoryService
{
    /// <summary>The headings a kirana store almost always needs (PRD §32). Seeded once, then fully
    /// editable — they are a starting point, not a fixed list.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> Defaults =
    [
        ("Rent", "Shop rent and lease payments"),
        ("Electricity", "Power bills"),
        ("Salary", "Staff wages and salaries"),
        ("Transport", "Delivery, freight and travel"),
        ("Internet", "Broadband and phone"),
        ("Packaging", "Bags, wrapping and packing material"),
        ("Maintenance", "Repairs, servicing and upkeep"),
        ("Miscellaneous", "Anything that does not fit another category"),
    ];

    public async Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(
        bool includeInactive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        IQueryable<ExpenseCategory> categories = db.ExpenseCategories.AsNoTracking();
        if (!includeInactive)
        {
            categories = categories.Where(c => c.IsActive);
        }

        return await categories.OrderBy(c => c.Name).ToListAsync(cancellationToken);
    }

    public async Task<ExpenseCategory?> GetByIdAsync(int categoryId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);
        return await db.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
    }

    public async Task<ExpenseCategory> CreateAsync(CreateExpenseCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var name = RequireName(request.Name);
        await EnsureNameAvailableAsync(name, excludingCategoryId: null, cancellationToken);

        var category = new ExpenseCategory
        {
            Name = name,
            Description = Normalize(request.Description),
            IsActive = true,
        };

        db.ExpenseCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ExpenseCategoryCreated", nameof(ExpenseCategory), category.Id.ToString(),
            newValue: category.Name, cancellationToken: cancellationToken);

        return category;
    }

    public async Task<ExpenseCategory> UpdateAsync(int categoryId, UpdateExpenseCategoryRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new InvalidOperationException("Expense category not found.");

        var name = RequireName(request.Name);
        await EnsureNameAvailableAsync(name, categoryId, cancellationToken);

        var previousName = category.Name;
        category.Name = name;
        category.Description = Normalize(request.Description);
        category.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ExpenseCategoryUpdated", nameof(ExpenseCategory), category.Id.ToString(),
            previousValue: previousName, newValue: category.Name, cancellationToken: cancellationToken);

        return category;
    }

    public async Task<ExpenseCategory> SetActiveAsync(
        int categoryId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new InvalidOperationException("Expense category not found.");

        if (category.IsActive == isActive)
        {
            return category;
        }

        category.IsActive = isActive;
        category.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, isActive ? "ExpenseCategoryReactivated" : "ExpenseCategoryDeactivated",
            nameof(ExpenseCategory), category.Id.ToString(), newValue: category.Name,
            cancellationToken: cancellationToken);

        return category;
    }

    public async Task DeleteAsync(int categoryId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new InvalidOperationException("Expense category not found.");

        // Deleting a category that past expenses point at would orphan those records; deleting a
        // seeded default would make an upgraded install diverge from a fresh one. Deactivating
        // keeps the history intact and hides the heading from new entries.
        if (await db.Expenses.AnyAsync(e => e.ExpenseCategoryId == categoryId, cancellationToken))
        {
            throw new InvalidOperationException(
                $"'{category.Name}' has expenses recorded against it and cannot be deleted. Deactivate it instead.");
        }

        if (category.IsSystemDefault)
        {
            throw new InvalidOperationException(
                $"'{category.Name}' is a built-in category and cannot be deleted. Deactivate it instead.");
        }

        db.ExpenseCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "ExpenseCategoryDeleted", nameof(ExpenseCategory), categoryId.ToString(),
            previousValue: category.Name, cancellationToken: cancellationToken);
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        // Runs on every launch, like permission seeding: adds only what is missing, so a store set
        // up before this phase gets the default headings without a reinstall, and adding a new
        // default in a future version reaches existing installs too.
        var existing = await db.ExpenseCategories
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        var existingNames = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;

        foreach (var (name, description) in Defaults)
        {
            if (existingNames.Contains(name))
            {
                continue;
            }

            db.ExpenseCategories.Add(new ExpenseCategory
            {
                Name = name,
                Description = description,
                IsActive = true,
                IsSystemDefault = true,
            });
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureNameAvailableAsync(string name, int? excludingCategoryId, CancellationToken cancellationToken)
    {
        var taken = await db.ExpenseCategories
            .AnyAsync(c => c.Name.ToLower() == name.ToLower() && c.Id != (excludingCategoryId ?? 0), cancellationToken);

        if (taken)
        {
            throw new InvalidOperationException($"An expense category named '{name}' already exists.");
        }
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.");
        }

        return name.Trim();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
