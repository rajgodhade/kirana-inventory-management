using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Kirana.Tests.Persistence;

/// <summary>
/// Optional release verification against an operator-supplied database. The connection is
/// read-only and proves that Phase 18A-4 leaves transaction counts and GST identity snapshots
/// byte-for-byte unchanged.
/// </summary>
public sealed class Phase18A4DatabaseVerificationTests
{
    [Fact]
    public async Task Classification_release_verification_preserves_database_and_historical_snapshots()
    {
        var databasePath = Environment.GetEnvironmentVariable("KIRANA_PHASE18A4_DB");
        if (string.IsNullOrWhiteSpace(databasePath)) return;

        var hashBefore = await HashFileAsync(databasePath);
        var before = await ReadEvidenceAsync(databasePath);
        var after = await ReadEvidenceAsync(databasePath);

        Assert.Equal(before.SaleCount, after.SaleCount);
        Assert.Equal(before.PurchaseCount, after.PurchaseCount);
        Assert.Equal(before.SnapshotHash, after.SnapshotHash);
        Assert.Equal(hashBefore, await HashFileAsync(databasePath));
    }

    private static async Task<DatabaseEvidence> ReadEvidenceAsync(string databasePath)
    {
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

        var saleCount = await CountAsync(connection, "Sales");
        var purchaseCount = await CountAsync(connection, "Purchases");
        var snapshotHash = await SnapshotHashAsync(connection);
        return new(saleCount, purchaseCount, snapshotHash);
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> SnapshotHashAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 'S', Id,
                   quote(GstIdentitySnapshotCapturedAtUtc), quote(StoreStateCodeSnapshot),
                   quote(CustomerGstRegistrationTypeSnapshot), quote(CustomerGstinSnapshot),
                   quote(CustomerStateCodeSnapshot)
            FROM Sales
            UNION ALL
            SELECT 'P', Id,
                   quote(GstIdentitySnapshotCapturedAtUtc), quote(StoreStateCodeSnapshot),
                   quote(SupplierGstRegistrationTypeSnapshot), quote(SupplierGstinSnapshot),
                   quote(SupplierStateCodeSnapshot)
            FROM Purchases
            ORDER BY 1, 2;
            """;

        var evidence = new StringBuilder();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                if (index > 0) evidence.Append('|');
                evidence.Append(reader.GetValue(index));
            }
            evidence.AppendLine();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())));
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed record DatabaseEvidence(long SaleCount, long PurchaseCount, string SnapshotHash);
}
