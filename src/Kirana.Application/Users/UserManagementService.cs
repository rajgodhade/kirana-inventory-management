using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Users;

public sealed class UserManagementService(
    IKiranaDbContext db, IPasswordHasher passwordHasher, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : IUserManagementService
{
    private const string OwnerRoleName = "Owner";
    private const int MinPasswordLength = 6;

    public async Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken cancellationToken = default) =>
        await db.Users.Include(u => u.Role).OrderBy(u => u.Username).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        await db.Roles.OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public async Task<User> CreateAsync(CreateUserRequest request, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.UsersManage, cancellationToken);

        var username = RequireUsername(request.Username);
        RequirePassword(request.Password);
        RequirePin(request.Pin);

        if (await db.Users.AnyAsync(u => u.Username == username, cancellationToken))
        {
            throw new InvalidOperationException($"Username '{username}' is already taken.");
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken)
            ?? throw new InvalidOperationException("Selected role does not exist.");

        var user = new User
        {
            Username = username,
            FullName = RequireFullName(request.FullName),
            PasswordHash = passwordHasher.Hash(request.Password),
            PinHash = string.IsNullOrWhiteSpace(request.Pin) ? null : passwordHasher.Hash(request.Pin),
            Role = role,
            IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "UserCreated", nameof(User), user.Id.ToString(),
            newValue: $"{user.Username} ({role.Name})", cancellationToken: cancellationToken);

        return user;
    }

    public async Task<User> UpdateAsync(int userId, UpdateUserRequest request, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.UsersManage, cancellationToken);

        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, cancellationToken)
            ?? throw new InvalidOperationException("Selected role does not exist.");

        if (role.Id != user.RoleId && user.IsActive && user.Role.Name == OwnerRoleName)
        {
            await EnsureAnotherActiveOwnerExistsAsync(user.Id, "reassign the last active Owner's role", cancellationToken);
        }

        user.FullName = RequireFullName(request.FullName);
        user.RoleId = role.Id;
        user.Role = role;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "UserUpdated", nameof(User), user.Id.ToString(),
            newValue: $"{user.Username} ({role.Name})", cancellationToken: cancellationToken);

        return user;
    }

    public async Task SetActiveAsync(int userId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.UsersManage, cancellationToken);

        var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        if (user.IsActive == isActive)
        {
            return;
        }

        if (!isActive && user.Role.Name == OwnerRoleName)
        {
            await EnsureAnotherActiveOwnerExistsAsync(user.Id, "deactivate the last active Owner account", cancellationToken);
        }

        user.IsActive = isActive;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, isActive ? "UserActivated" : "UserDeactivated",
            nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
    }

    public async Task ResetPasswordAsync(int userId, string newPassword, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.UsersManage, cancellationToken);
        RequirePassword(newPassword);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.FailedLoginAttempts = 0;
        user.LockedUntilUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "PasswordReset", nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
    }

    public async Task SetPinAsync(int userId, string? newPin, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.UsersManage, cancellationToken);
        RequirePin(newPin);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        user.PinHash = string.IsNullOrWhiteSpace(newPin) ? null : passwordHasher.Hash(newPin);
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, string.IsNullOrWhiteSpace(newPin) ? "PinCleared" : "PinChanged",
            nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
    }

    public async Task UnlockAccountAsync(int userId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.UsersManage, cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        user.FailedLoginAttempts = 0;
        user.LockedUntilUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, "AccountUnlocked", nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
    }

    private async Task EnsureAnotherActiveOwnerExistsAsync(int excludingUserId, string actionDescription, CancellationToken cancellationToken)
    {
        var otherActiveOwners = await db.Users
            .Include(u => u.Role)
            .CountAsync(u => u.Id != excludingUserId && u.IsActive && u.Role.Name == OwnerRoleName, cancellationToken);

        if (otherActiveOwners == 0)
        {
            throw new InvalidOperationException($"Cannot {actionDescription} — at least one active Owner is required.");
        }
    }

    private static string RequireUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.");
        }

        return username.Trim();
    }

    private static string RequireFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required.");
        }

        return fullName.Trim();
    }

    private static void RequirePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            throw new ArgumentException($"Password must be at least {MinPasswordLength} characters.");
        }
    }

    private static void RequirePin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return;
        }

        if (pin.Length is < 4 or > 6 || !pin.All(char.IsDigit))
        {
            throw new ArgumentException("PIN must be 4-6 digits.");
        }
    }
}
