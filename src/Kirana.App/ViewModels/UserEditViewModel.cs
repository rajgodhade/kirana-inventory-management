using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Users;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Create/Edit User dialog (PRD §6). Username/password/PIN are only collected
/// in create mode — editing an existing user only changes full name and role; password/PIN
/// changes go through the separate Reset Password / Set PIN dialogs.</summary>
public sealed partial class UserEditViewModel : ObservableObject
{
    private readonly UserManagementViewModel _owner;
    private readonly IUserManagementService _userManagementService;
    private readonly int? _editingUserId;

    public bool IsEditMode => _editingUserId is not null;

    public string DialogTitle => IsEditMode ? "Edit User" : "Create User";

    public ObservableCollection<Role> Roles => _owner.Roles;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private Role? _selectedRole;

    [ObservableProperty]
    private string? _errorMessage;

    public UserEditViewModel(UserManagementViewModel owner, IUserManagementService userManagementService, UserRowViewModel? existingUser)
    {
        _owner = owner;
        _userManagementService = userManagementService;

        if (existingUser is not null)
        {
            _editingUserId = existingUser.Id;
            Username = existingUser.Username;
            FullName = existingUser.FullName;
            SelectedRole = Roles.FirstOrDefault(r => r.Id == existingUser.RoleId);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (SelectedRole is null)
        {
            ErrorMessage = "Select a role.";
            return;
        }

        try
        {
            if (IsEditMode)
            {
                await _userManagementService.UpdateAsync(
                    _editingUserId!.Value,
                    new UpdateUserRequest { FullName = FullName, RoleId = SelectedRole.Id },
                    _owner.CurrentUserId);
            }
            else
            {
                await _userManagementService.CreateAsync(
                    new CreateUserRequest
                    {
                        Username = Username,
                        FullName = FullName,
                        Password = Password,
                        Pin = string.IsNullOrWhiteSpace(Pin) ? null : Pin,
                        RoleId = SelectedRole.Id,
                    },
                    _owner.CurrentUserId);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
