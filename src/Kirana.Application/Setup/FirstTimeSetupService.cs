using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Setup;

public sealed class FirstTimeSetupService(IKiranaDbContext db, IPasswordHasher passwordHasher)
    : IFirstTimeSetupService
{
    public async Task<bool> IsSetupCompletedAsync(CancellationToken cancellationToken = default)
    {
        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken);
        return store is { SetupCompleted: true };
    }

    public async Task CompleteSetupAsync(CompleteSetupRequest request, CancellationToken cancellationToken = default)
    {
        if (await IsSetupCompletedAsync(cancellationToken))
        {
            throw new InvalidOperationException("Setup has already been completed.");
        }

        var ownerRole = new Role { Name = "Owner", IsSystemRole = true };
        var managerRole = new Role { Name = "Manager", IsSystemRole = true };
        var cashierRole = new Role { Name = "Cashier", IsSystemRole = true };

        var permissionsByKey = new Dictionary<string, Permission>();
        foreach (var (key, description) in PermissionKeys.All)
        {
            permissionsByKey[key] = new Permission { Key = key, Description = description };
        }

        AttachPermissions(ownerRole, PermissionKeys.Owner, permissionsByKey);
        AttachPermissions(managerRole, PermissionKeys.Manager, permissionsByKey);
        AttachPermissions(cashierRole, PermissionKeys.Cashier, permissionsByKey);

        db.Roles.AddRange(ownerRole, managerRole, cashierRole);
        db.Permissions.AddRange(permissionsByKey.Values);

        var admin = new User
        {
            Username = request.AdminUsername,
            FullName = request.AdminFullName,
            PasswordHash = passwordHasher.Hash(request.AdminPassword),
            PinHash = string.IsNullOrWhiteSpace(request.AdminPin) ? null : passwordHasher.Hash(request.AdminPin),
            Role = ownerRole,
            IsActive = true,
        };
        db.Users.Add(admin);

        db.Stores.Add(new Store
        {
            Name = request.StoreName,
            OwnerName = request.OwnerName,
            Gstin = request.Gstin,
            Address = request.Address,
            City = request.City,
            State = request.State,
            PinCode = request.PinCode,
            ContactNumber = request.ContactNumber,
            AlternateContactNumber = request.AlternateContactNumber,
            Email = request.Email,
            LogoPath = request.LogoPath,
            InvoicePrefix = request.InvoicePrefix,
            FinancialYearStartMonth = request.FinancialYearStartMonth,
            IsGstEnabled = request.IsGstEnabled,
            DefaultInvoiceFormat = request.DefaultInvoiceFormat,
            SetupCompleted = true,
        });

        db.AppSettings.Add(new AppSettings());

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AttachPermissions(Role role, IReadOnlyList<string> keys, IReadOnlyDictionary<string, Permission> byKey)
    {
        foreach (var key in keys)
        {
            role.RolePermissions.Add(new RolePermission { Role = role, Permission = byKey[key] });
        }
    }
}
