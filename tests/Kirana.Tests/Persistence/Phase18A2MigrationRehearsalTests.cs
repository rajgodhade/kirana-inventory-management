using System.Security.Cryptography;
using System.Text;
using Kirana.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit.Abstractions;

namespace Kirana.Tests.Persistence;

/// <summary>
/// Optional real-database verifier for Phase 18A-2. The source is the immutable pre-migration
/// backup and the rehearsal is its migrated copy. Normal test runs return immediately because
/// machine-specific database paths do not belong in source control.
/// </summary>
public sealed class Phase18A2MigrationRehearsalTests(ITestOutputHelper output)
{
    [Fact]
    public void Migration_is_additive_nullable_and_contains_no_legacy_backfill_operation()
    {
        var operations = TestablePhase18A2Migration.GetOperations();

        Assert.All(operations.OfType<AddColumnOperation>(), operation => Assert.True(operation.IsNullable));
        Assert.DoesNotContain(operations, operation => operation is SqlOperation or UpdateDataOperation);
        Assert.All(operations, operation => Assert.IsType<AddColumnOperation>(operation));
    }

    [Fact]
    public async Task Rehearsal_preserves_all_preexisting_data_and_leaves_legacy_snapshots_null()
    {
        var sourcePath = Environment.GetEnvironmentVariable("KIRANA_PHASE18A2_SOURCE_DB");
        var rehearsalPath = Environment.GetEnvironmentVariable("KIRANA_PHASE18A2_REHEARSAL_DB");
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(rehearsalPath)) return;

        await using var source = await OpenReadOnlyAsync(sourcePath);
        await using var rehearsal = await OpenReadOnlyAsync(rehearsalPath);

        Assert.Equal("ok", await ScalarAsync(source, "PRAGMA integrity_check;"));
        Assert.Equal("ok", await ScalarAsync(rehearsal, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await LongScalarAsync(source, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(0L, await LongScalarAsync(rehearsal, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));

        foreach (var table in await GetTablesAsync(source))
        {
            var originalColumns = await GetColumnsAsync(source, table);
            Assert.Equal(
                await FingerprintAsync(source, table, originalColumns),
                await FingerprintAsync(rehearsal, table, originalColumns));
            output.WriteLine($"{table}: count and preexisting-column fingerprint preserved.");
        }

        foreach (var (table, column) in SnapshotColumns)
        {
            Assert.Contains(column, await GetColumnsAsync(rehearsal, table));
            Assert.Equal(0L, await LongScalarAsync(rehearsal,
                $"SELECT COUNT(*) FROM {Quote(table)} WHERE {Quote(column)} IS NOT NULL;"));
        }

        foreach (var table in new[] { "Sales", "SaleItems", "Purchases", "PurchaseItems" })
        {
            var count = await LongScalarAsync(rehearsal, $"SELECT COUNT(*) FROM {Quote(table)};");
            output.WriteLine($"{table}: {count} historical row(s), unchanged.");
        }
    }

    private static readonly (string Table, string Column)[] SnapshotColumns =
    [
        ("Sales", "GstIdentitySnapshotCapturedAtUtc"),
        ("Sales", "StoreTradeNameSnapshot"), ("Sales", "StoreLegalNameSnapshot"),
        ("Sales", "StoreGstinSnapshot"), ("Sales", "StoreStateCodeSnapshot"),
        ("Sales", "StoreStateNameSnapshot"), ("Sales", "StoreGstRegistrationTypeSnapshot"),
        ("Sales", "StoreAddressSnapshot"), ("Sales", "StoreCitySnapshot"),
        ("Sales", "StorePinCodeSnapshot"), ("Sales", "StoreContactNumberSnapshot"),
        ("Sales", "CustomerNameSnapshot"), ("Sales", "CustomerPhoneSnapshot"),
        ("Sales", "CustomerGstinSnapshot"), ("Sales", "CustomerStateCodeSnapshot"),
        ("Sales", "CustomerStateNameSnapshot"), ("Sales", "CustomerGstRegistrationTypeSnapshot"),
        ("Sales", "CustomerAddressSnapshot"),
        ("Purchases", "GstIdentitySnapshotCapturedAtUtc"),
        ("Purchases", "StoreTradeNameSnapshot"), ("Purchases", "StoreLegalNameSnapshot"),
        ("Purchases", "StoreGstinSnapshot"), ("Purchases", "StoreStateCodeSnapshot"),
        ("Purchases", "StoreStateNameSnapshot"), ("Purchases", "StoreGstRegistrationTypeSnapshot"),
        ("Purchases", "StoreAddressSnapshot"), ("Purchases", "StoreCitySnapshot"),
        ("Purchases", "StorePinCodeSnapshot"), ("Purchases", "StoreContactNumberSnapshot"),
        ("Purchases", "SupplierNameSnapshot"), ("Purchases", "SupplierCodeSnapshot"),
        ("Purchases", "SupplierGstinSnapshot"), ("Purchases", "SupplierStateCodeSnapshot"),
        ("Purchases", "SupplierStateNameSnapshot"), ("Purchases", "SupplierGstRegistrationTypeSnapshot"),
        ("Purchases", "SupplierAddressSnapshot"),
    ];

    private static async Task<SqliteConnection> OpenReadOnlyAsync(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        return connection;
    }

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
        SqliteConnection connection, string table, IReadOnlyList<string> columns)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(",", columns.Select(Quote))} FROM {Quote(table)} ORDER BY rowid;";
        await using var reader = await command.ExecuteReaderAsync();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var text = reader.IsDBNull(index) ? "<NULL>" : reader.GetValue(index).ToString()!;
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                hash.AppendData(Encoding.UTF8.GetBytes($"{index}:{encoded.Length}:{encoded};"));
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

    private sealed class TestablePhase18A2Migration : Phase18A2HistoricalGstIdentitySnapshots
    {
        public static IReadOnlyList<MigrationOperation> GetOperations()
        {
            var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
            new TestablePhase18A2Migration().Up(builder);
            return builder.Operations;
        }
    }
}
