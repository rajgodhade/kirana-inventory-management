namespace Kirana.App.ViewModels;

/// <summary>Flattened row for the User Management list (PRD §6). No <c>required</c> members —
/// see the Phase 5 note on avoiding required members on types reachable from a bound ViewModel.</summary>
public sealed class UserRowViewModel
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string FullName { get; init; } = "";
    public int RoleId { get; init; }
    public string RoleName { get; init; } = "";
    public bool IsActive { get; init; }
    public bool IsLocked { get; init; }
    public string LastLoginText { get; init; } = "Never";
}
