using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Kirana.Tests.Persistence;

/// <summary>
/// Optional read-only verification against an operator-supplied database. The normal suite skips
/// the machine-specific check; release verification opts in with <c>KIRANA_PHASE18A3_DB</c>.
/// </summary>
public sealed class Phase18A3DatabaseVerificationTests
{
    [Fact]
    public async Task Jurisdiction_release_verification_does_not_modify_the_database()
    {
        var databasePath = Environment.GetEnvironmentVariable("KIRANA_PHASE18A3_DB");
        if (string.IsNullOrWhiteSpace(databasePath)) return;

        var hashBefore = await HashAsync(databasePath);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();

        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", (await integrity.ExecuteScalarAsync())?.ToString());
        }

        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";
            Assert.Equal(0L, Convert.ToInt64(await foreignKeys.ExecuteScalarAsync()));
        }

        await connection.CloseAsync();
        Assert.Equal(hashBefore, await HashAsync(databasePath));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
