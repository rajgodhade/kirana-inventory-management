using Kirana.Domain.Entities;

namespace Kirana.Application.Authentication;

public sealed record AuthResult(bool Success, string? ErrorMessage, User? User)
{
    public static AuthResult Ok(User user) => new(true, null, user);
    public static AuthResult Fail(string message) => new(false, message, null);
}
