namespace Kirana.Application.Users;

public sealed class CreateUserRequest
{
    public required string Username { get; init; }
    public required string FullName { get; init; }
    public required string Password { get; init; }
    public string? Pin { get; init; }
    public required int RoleId { get; init; }
}
