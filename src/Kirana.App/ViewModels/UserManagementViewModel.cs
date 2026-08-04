using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Users;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the User Management page (PRD §6): create/edit users, assign roles,
/// activate/deactivate, reset password/PIN, and unlock accounts. Every mutation is delegated to
/// <see cref="IUserManagementService"/>, which independently re-checks
/// <see cref="PermissionKeys.UsersManage"/> and last-active-Owner protection server-side — this
/// ViewModel's own <see cref="CanManageUsers"/> only controls what the UI shows/enables.</summary>
public sealed partial class UserManagementViewModel(IUserManagementService userManagementService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanManageUsers => session.HasPermission(PermissionKeys.UsersManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<UserRowViewModel> Users { get; } = [];

    public ObservableCollection<Role> Roles { get; } = [];

    public async Task InitializeAsync()
    {
        await LoadRolesAsync();
        await LoadUsersAsync();
    }

    public async Task LoadRolesAsync()
    {
        var roles = await userManagementService.GetRolesAsync();
        Roles.Clear();
        foreach (var role in roles)
        {
            Roles.Add(role);
        }
    }

    public async Task LoadUsersAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var users = await userManagementService.GetAllUsersAsync();
            Users.Clear();
            foreach (var user in users)
            {
                Users.Add(ToRow(user));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> SetActiveAsync(int userId, bool isActive)
    {
        ErrorMessage = null;
        try
        {
            await userManagementService.SetActiveAsync(userId, isActive, CurrentUserId);
            await LoadUsersAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
    {
        ErrorMessage = null;
        try
        {
            await userManagementService.ResetPasswordAsync(userId, newPassword, CurrentUserId);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<bool> SetPinAsync(int userId, string? newPin)
    {
        ErrorMessage = null;
        try
        {
            await userManagementService.SetPinAsync(userId, newPin, CurrentUserId);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task<bool> UnlockAccountAsync(int userId)
    {
        ErrorMessage = null;
        try
        {
            await userManagementService.UnlockAccountAsync(userId, CurrentUserId);
            await LoadUsersAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    private static UserRowViewModel ToRow(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        RoleId = user.RoleId,
        RoleName = user.Role.Name,
        IsActive = user.IsActive,
        IsLocked = user.LockedUntilUtc is { } lockedUntil && lockedUntil > DateTime.UtcNow,
        LastLoginText = user.LastLoginAtUtc?.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt") ?? "Never",
    };
}
