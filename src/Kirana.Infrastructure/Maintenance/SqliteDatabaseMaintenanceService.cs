using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Maintenance;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Infrastructure.Maintenance;

/// <summary>
/// SQLite housekeeping. This is the one service that deliberately goes around the DbSet layer:
/// PRAGMA/VACUUM/ANALYZE have no EF equivalent. Every statement issued here is either a documented
/// SQLite maintenance command or a read-only SELECT — nothing writes business data.
/// </summary>
public sealed class SqliteDatabaseMaintenanceService(
    KiranaDbContext db,
    IKiranaDbContext appDb,
    IAppPaths appPaths,
    IPermissionEnforcer permissionEnforcer,
    IAuditLogger auditLogger) : IDatabaseMaintenanceService
{
    private static readonly string[] CountedTables =
    [
        "Products", "Categories", "Brands", "Inventories", "StockMovements", "ProductBatches",
        "Customers", "Sales", "SaleItems", "Payments", "CustomerCredits",
        "Suppliers", "Purchases", "PurchaseItems", "SupplierPayments",
        "SalesReturns", "PurchaseReturns", "Expenses", "Users", "AuditLogs", "BackupRecords",
    ];

    private static readonly (string Description, string Sql)[] OrphanQueries =
    [
        ("Stock levels whose product no longer exists",
            "SELECT i.Id FROM Inventories i LEFT JOIN Products p ON p.Id = i.ProductId WHERE p.Id IS NULL"),
        ("Stock movements whose product no longer exists",
            "SELECT m.Id FROM StockMovements m LEFT JOIN Products p ON p.Id = m.ProductId WHERE p.Id IS NULL"),
        ("Invoice lines whose sale no longer exists",
            "SELECT si.Id FROM SaleItems si LEFT JOIN Sales s ON s.Id = si.SaleId WHERE s.Id IS NULL"),
        ("Payments whose sale no longer exists",
            "SELECT pm.Id FROM Payments pm LEFT JOIN Sales s ON s.Id = pm.SaleId WHERE s.Id IS NULL"),
        ("Purchase lines whose purchase no longer exists",
            "SELECT pi.Id FROM PurchaseItems pi LEFT JOIN Purchases p ON p.Id = pi.PurchaseId WHERE p.Id IS NULL"),
        ("Product batches whose product no longer exists",
            "SELECT b.Id FROM ProductBatches b LEFT JOIN Products p ON p.Id = b.ProductId WHERE p.Id IS NULL"),
        ("Udhaar credits whose customer no longer exists",
            "SELECT c.Id FROM CustomerCredits c LEFT JOIN Customers cu ON cu.Id = c.CustomerId WHERE cu.Id IS NULL"),
    ];

    public async Task<IntegrityCheckResult> RunIntegrityCheckAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);

        var messages = new List<string>();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(reader.GetString(0));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new IntegrityCheckResult
        {
            IsHealthy = messages is ["ok"],
            Messages = messages,
        };
    }

    public Task<MaintenanceOperationResult> VacuumAsync(int? performedByUserId, CancellationToken cancellationToken = default) =>
        RunMaintenanceCommandAsync("VACUUM;", "DatabaseVacuumed", performedByUserId, cancellationToken);

    public Task<MaintenanceOperationResult> AnalyzeAsync(int? performedByUserId, CancellationToken cancellationToken = default) =>
        RunMaintenanceCommandAsync("ANALYZE;", "DatabaseAnalyzed", performedByUserId, cancellationToken);

    private async Task<MaintenanceOperationResult> RunMaintenanceCommandAsync(
        string sql, string auditAction, int? performedByUserId, CancellationToken cancellationToken)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);

        var sizeBefore = CurrentFileSize();

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch (SqliteException ex)
        {
            return new MaintenanceOperationResult { Succeeded = false, ErrorMessage = ex.Message };
        }

        var sizeAfter = CurrentFileSize();
        var details = sizeBefore > 0 && sizeAfter > 0
            ? $"Database size {FormatBytes(sizeBefore)} → {FormatBytes(sizeAfter)}."
            : "Completed.";

        await auditLogger.RecordAsync(
            performedByUserId, auditAction, "Database", null, newValue: details, cancellationToken: cancellationToken);

        return new MaintenanceOperationResult { Succeeded = true, Details = details };
    }

    public async Task<DatabaseStatistics> GetStatisticsAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);

        long pageCount, pageSize, freePages;
        var tableCounts = new List<(string, long)>();

        try
        {
            pageCount = await ScalarAsync(connection, "PRAGMA page_count;", cancellationToken);
            pageSize = await ScalarAsync(connection, "PRAGMA page_size;", cancellationToken);
            freePages = await ScalarAsync(connection, "PRAGMA freelist_count;", cancellationToken);

            foreach (var table in CountedTables)
            {
                // Table names come from a fixed private array, never from user input.
                var count = await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"{table}\";", cancellationToken);
                tableCounts.Add((table, count));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return new DatabaseStatistics
        {
            DatabaseFilePath = appPaths.DatabaseFilePath,
            FileSizeBytes = CurrentFileSize(),
            PageCount = pageCount,
            PageSize = pageSize,
            FreePageCount = freePages,
            TableRowCounts = tableCounts,
            LastBackupUtc = await appDb.BackupRecords.AsNoTracking()
                .OrderByDescending(b => b.CreatedAtUtc)
                .Select(b => (DateTime?)b.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken),
            LastVacuumUtc = await LastAuditTimestampAsync("DatabaseVacuumed", cancellationToken),
            LastAnalyzeUtc = await LastAuditTimestampAsync("DatabaseAnalyzed", cancellationToken),
        };
    }

    public async Task<IReadOnlyList<OrphanRecordGroup>> FindOrphanRecordsAsync(
        int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);

        var results = new List<OrphanRecordGroup>();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            foreach (var (description, sql) in OrphanQueries)
            {
                var ids = new List<int>();
                using var command = connection.CreateCommand();
                command.CommandText = sql;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    ids.Add(reader.GetInt32(0));
                }

                results.Add(new OrphanRecordGroup
                {
                    Description = description,
                    Count = ids.Count,
                    SampleIds = ids.Take(10).ToList(),
                });
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return results;
    }

    private async Task<DateTime?> LastAuditTimestampAsync(string action, CancellationToken cancellationToken) =>
        await appDb.AuditLogs.AsNoTracking()
            .Where(a => a.Action == action)
            .OrderByDescending(a => a.TimestampUtc)
            .Select(a => (DateTime?)a.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private long CurrentFileSize() =>
        File.Exists(appPaths.DatabaseFilePath) ? new FileInfo(appPaths.DatabaseFilePath).Length : 0;

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} bytes",
    };
}
