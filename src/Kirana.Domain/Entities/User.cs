using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Local user account. Password and PIN are always stored hashed (PRD §7, §44) —
/// never assign to <see cref="PasswordHash"/>/<see cref="PinHash"/> directly from
/// plaintext; go through the password hashing service in Kirana.Infrastructure.
/// </summary>
public class User : Entity
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? PinHash { get; set; }
    public bool IsActive { get; set; } = true;

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}
