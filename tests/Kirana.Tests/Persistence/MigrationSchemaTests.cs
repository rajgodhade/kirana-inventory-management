using Kirana.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Persistence;

/// <summary>
/// The shared fixtures build their schema with <c>EnsureCreated()</c>, which reads the entity model
/// and ignores the migration files entirely. That leaves a blind spot: a property added to an
/// entity without a matching migration passes every other test and then crashes the real app at
/// startup, because the app runs <c>Migrate()</c> against a database that lacks the column. These
/// tests close that gap by applying the actual migration chain to a throwaway database.
/// </summary>
public sealed class MigrationSchemaTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"kirana-migrations-{Guid.NewGuid():N}.db");

    private KiranaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<KiranaDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    [Fact]
    public async Task MigrationChain_AppliesCleanly_FromEmptyDatabase()
    {
        await using var context = CreateContext();

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task MigratedSchema_HasEveryColumn_TheEntityModelExpects()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Comparing the migrated database against the model catches drift in either direction, for
        // every entity — not just the one that happened to prompt this test.
        var missing = new List<string>();
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null) continue;

            var actual = await ColumnNamesAsync(context, table);
            if (actual.Count == 0) { missing.Add($"{table} (table missing)"); continue; }

            missing.AddRange(entityType.GetProperties()
                .Select(p => p.GetColumnName())
                .Where(column => column is not null && !actual.Contains(column))
                .Select(column => $"{table}.{column}"));
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task CashRegisterSessions_HasSupplierCashPaymentsColumn()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var columns = await ColumnNamesAsync(context, "CashRegisterSessions");

        Assert.Contains("SupplierCashPayments", columns);
    }

    private static async Task<HashSet<string>> ColumnNamesAsync(KiranaDbContext context, string table)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
