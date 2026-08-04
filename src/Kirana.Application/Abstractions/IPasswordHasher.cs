namespace Kirana.Application.Abstractions;

/// <summary>
/// Salted hashing for both account passwords and quick-access PINs (PRD §7, §44).
/// Implemented in Kirana.Infrastructure using BCrypt — never store plaintext.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string hash);
}
