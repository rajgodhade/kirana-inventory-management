using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Products;

public sealed class BrandService(IKiranaDbContext db, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer) : IBrandService
{
    public async Task<IReadOnlyList<Brand>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = db.Brands.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query.OrderBy(b => b.Name).ToListAsync(cancellationToken);
    }

    public async Task<Brand> CreateAsync(string name, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var trimmed = RequireName(name);

        if (await db.Brands.AnyAsync(b => b.Name == trimmed, cancellationToken))
        {
            throw new InvalidOperationException($"Brand '{trimmed}' already exists.");
        }

        var brand = new Brand { Name = trimmed, IsActive = true };
        db.Brands.Add(brand);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "BrandCreated", nameof(Brand), brand.Id.ToString(),
            newValue: brand.Name, cancellationToken: cancellationToken);

        return brand;
    }

    public async Task RenameAsync(int brandId, string newName, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var trimmed = RequireName(newName);
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == brandId, cancellationToken)
            ?? throw new InvalidOperationException("Brand not found.");

        if (await db.Brands.AnyAsync(b => b.Name == trimmed && b.Id != brandId, cancellationToken))
        {
            throw new InvalidOperationException($"Brand '{trimmed}' already exists.");
        }

        var previousName = brand.Name;
        brand.Name = trimmed;
        brand.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "BrandRenamed", nameof(Brand), brand.Id.ToString(),
            previousValue: previousName, newValue: trimmed, cancellationToken: cancellationToken);
    }

    public async Task SetActiveAsync(int brandId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == brandId, cancellationToken)
            ?? throw new InvalidOperationException("Brand not found.");

        brand.IsActive = isActive;
        brand.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, isActive ? "BrandReactivated" : "BrandDeactivated",
            nameof(Brand), brand.Id.ToString(), cancellationToken: cancellationToken);
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Brand name is required.");
        }

        return name.Trim();
    }
}
