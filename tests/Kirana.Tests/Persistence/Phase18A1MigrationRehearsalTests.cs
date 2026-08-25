using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Kirana.Tests.Persistence;

/// <summary>
/// Optional verifier for rehearsing Phase 18A-1 against a copy of the real development database.
/// Set KIRANA_PHASE18_SOURCE_DB and KIRANA_PHASE18_REHEARSAL_DB before running this test. The normal
/// suite returns immediately because developer-specific database paths never belong in source.
/// </summary>
public sealed class Phase18A1MigrationRehearsalTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Rehearsed_database_is_integral_and_preserves_every_preexisting_value()
    {
        var sourcePath = Environment.GetEnvironmentVariable("KIRANA_PHASE18_SOURCE_DB");
        var rehearsalPath = Environment.GetEnvironmentVariable("KIRANA_PHASE18_REHEARSAL_DB");
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(rehearsalPath))
        {
            return;
        }

        await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        await using var rehearsal = new SqliteConnection($"Data Source={rehearsalPath};Mode=ReadOnly");
        await source.OpenAsync();
        await rehearsal.OpenAsync();

        Assert.Equal("ok", await ScalarAsync(source, "PRAGMA integrity_check;"));
        Assert.Equal("ok", await ScalarAsync(rehearsal, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await LongScalarAsync(source, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(0L, await LongScalarAsync(rehearsal, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));

        var tables = await GetTablesAsync(source);
        Assert.NotEmpty(tables);
        foreach (var table in tables)
        {
            var columns = await GetColumnsAsync(source, table);
            var sourceFingerprint = await FingerprintAsync(source, table, columns);
            var rehearsalFingerprint = await FingerprintAsync(rehearsal, table, columns);
            Assert.Equal(sourceFingerprint, rehearsalFingerprint);
        }

        foreach (var (table, column) in NewNullableColumns)
        {
            Assert.Contains(column, await GetColumnsAsync(rehearsal, table));
            var resolvedCount = await LongScalarAsync(rehearsal,
                $"SELECT COUNT(*) FROM {Quote(table)} WHERE {Quote(column)} IS NOT NULL;");
            Assert.Equal(0L, resolvedCount);
        }

        foreach (var table in new[] { "Stores", "Customers", "Suppliers" })
        {
            var unresolved = await LongScalarAsync(rehearsal,
                $"SELECT COUNT(*) FROM {Quote(table)} WHERE StateCode IS NULL;");
            output.WriteLine($"{table}: {unresolved} legacy record(s) left unresolved by policy.");
        }
    }

    private static readonly (string Table, string Column)[] NewNullableColumns =
    [
        ("Stores", "LegalName"), ("Stores", "StateCode"), ("Stores", "GstRegistrationType"),
        ("Customers", "StateCode"), ("Customers", "GstRegistrationType"),
        ("Suppliers", "StateCode"), ("Suppliers", "GstRegistrationType"),
    ];

    private static async Task<IReadOnlyList<string>> GetTablesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<IReadOnlyList<string>> GetColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)});";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(1));
        return result;
    }

    private static async Task<string> FingerprintAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> columns)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(",", columns.Select(Quote))} FROM {Quote(table)} ORDER BY rowid;";
        await using var reader = await command.ExecuteReaderAsync();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.IsDBNull(index) ? "<NULL>" : Convert.ToBase64String(Encoding.UTF8.GetBytes(reader.GetValue(index).ToString()!));
                hash.AppendData(Encoding.UTF8.GetBytes($"{index}:{value.Length}:{value};"));
            }
            hash.AppendData("\n"u8);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<long> LongScalarAsync(SqliteConnection connection, string sql) =>
        Convert.ToInt64(await ScalarAsync(connection, sql));

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
