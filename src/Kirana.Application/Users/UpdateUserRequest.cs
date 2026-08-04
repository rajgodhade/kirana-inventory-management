namespace Kirana.Application.Users;

public sealed class UpdateUserRequest
{
    public required string FullName { get; init; }
    public required int RoleId { get; init; }
}
