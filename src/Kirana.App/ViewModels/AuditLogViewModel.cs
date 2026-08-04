using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Audit;
using Kirana.Application.Authentication;
using Kirana.Application.Users;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Audit Log screen (PRD §37) — read-only filtering by user, action, entity,
/// and date/time. <see cref="IAuditLogQueryService"/> re-checks
/// <see cref="PermissionKeys.AuditLogView"/> server-side on every search.</summary>
public sealed partial class AuditLogViewModel(
    IAuditLogQueryService auditLogQueryService, IUserManagementService userManagementService, ManagementSession session) : ObservableObject
{
    public bool CanViewAuditLog => session.HasPermission(PermissionKeys.AuditLogView);

    private int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<AuditLogRowViewModel> Results { get; } = [];

    public ObservableCollection<User> Users { get; } = [];

    public ObservableCollection<string> Actions { get; } = [];

    [ObservableProperty]
    private User? _selectedUser;

    [ObservableProperty]
    private string? _selectedAction;

    [ObservableProperty]
    private string _entityText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _fromDate;

    [ObservableProperty]
    private DateTimeOffset? _toDate;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public async Task InitializeAsync()
    {
        var users = await userManagementService.GetAllUsersAsync();
        Users.Clear();
        foreach (var user in users)
        {
            Users.Add(user);
        }

        var actions = await auditLogQueryService.GetDistinctActionsAsync();
        Actions.Clear();
        foreach (var action in actions)
        {
            Actions.Add(action);
        }

        await SearchAsync();
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var query = new AuditLogQuery
            {
                UserId = SelectedUser?.Id,
                Action = SelectedAction,
                Entity = string.IsNullOrWhiteSpace(EntityText) ? null : EntityText.Trim(),
                FromUtc = FromDate?.UtcDateTime,
                ToUtc = ToDate?.UtcDateTime.AddDays(1).AddTicks(-1),
            };

            var entries = await auditLogQueryService.SearchAsync(query, CurrentUserId);

            Results.Clear();
            foreach (var entry in entries)
            {
                Results.Add(new AuditLogRowViewModel
                {
                    TimestampText = entry.TimestampUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm:ss tt"),
                    UserDisplay = entry.User?.FullName ?? "System",
                    Action = entry.Action,
                    Entity = entry.Entity,
                    EntityId = entry.EntityId,
                    Details = BuildDetails(entry.PreviousValue, entry.NewValue, entry.Reason),
                });
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

    public void ClearFilters()
    {
        SelectedUser = null;
        SelectedAction = null;
        EntityText = string.Empty;
        FromDate = null;
        ToDate = null;
    }

    private static string? BuildDetails(string? previousValue, string? newValue, string? reason)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(previousValue))
        {
            parts.Add($"Was: {previousValue}");
        }

        if (!string.IsNullOrWhiteSpace(newValue))
        {
            parts.Add($"Now: {newValue}");
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            parts.Add($"Reason: {reason}");
        }

        return parts.Count == 0 ? null : string.Join("  ", parts);
    }
}
