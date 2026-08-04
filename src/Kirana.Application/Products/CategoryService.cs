using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Products;

public sealed class CategoryService(IKiranaDbContext db, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer) : ICategoryService
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = db.Categories.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.OrderBy(c => c.Name).ToListAsync(cancellationToken);
    }

    public async Task<Category> CreateAsync(string name, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var trimmed = RequireName(name);

        if (await db.Categories.AnyAsync(c => c.Name == trimmed, cancellationToken))
        {
            throw new InvalidOperationException($"Category '{trimmed}' already exists.");
        }

        var category = new Category { Name = trimmed, IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "CategoryCreated", nameof(Category), category.Id.ToString(),
            newValue: category.Name, cancellationToken: cancellationToken);

        return category;
    }

    public async Task RenameAsync(int categoryId, string newName, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var trimmed = RequireName(newName);
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new InvalidOperationException("Category not found.");

        if (await db.Categories.AnyAsync(c => c.Name == trimmed && c.Id != categoryId, cancellationToken))
        {
            throw new InvalidOperationException($"Category '{trimmed}' already exists.");
        }

        var previousName = category.Name;
        category.Name = trimmed;
        category.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "CategoryRenamed", nameof(Category), category.Id.ToString(),
            previousValue: previousName, newValue: trimmed, cancellationToken: cancellationToken);
    }

    public async Task SetActiveAsync(int categoryId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken)
            ?? throw new InvalidOperationException("Category not found.");

        category.IsActive = isActive;
        category.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, isActive ? "CategoryReactivated" : "CategoryDeactivated",
            nameof(Category), category.Id.ToString(), cancellationToken: cancellationToken);
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.");
        }

        return name.Trim();
    }
}
