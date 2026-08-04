using Kirana.Domain.Entities;

namespace Kirana.Application.Users;

/// <summary>
/// Local user administration (PRD §6-10): create/edit users, assign roles, activate/deactivate,
/// reset passwords/PINs, and unlock accounts after a lockout. Every mutating method requires
/// <see cref="PermissionKeys.UsersManage"/> and refuses to leave the store without any active
/// Owner account.
/// </summary>
public interface IUserManagementService
{
    Task<IReadOnlyList<User>> GetAllUsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Role>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<User> CreateAsync(CreateUserRequest request, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<User> UpdateAsync(int userId, UpdateUserRequest request, int? performedByUserId, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int userId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(int userId, string newPassword, int? performedByUserId, CancellationToken cancellationToken = default);

    Task SetPinAsync(int userId, string? newPin, int? performedByUserId, CancellationToken cancellationToken = default);

    Task UnlockAccountAsync(int userId, int? performedByUserId, CancellationToken cancellationToken = default);
}
