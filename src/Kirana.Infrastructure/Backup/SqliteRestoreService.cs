using System.IO.Compression;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Restore;
using Kirana.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Kirana.Infrastructure.Backup;

/// <summary>
/// Replaces the live database with a backup's copy. The ordering here is the safety mechanism:
/// every step that can fail happens against temp files, and the live database file is not touched
/// until the very last operation — so a failed restore leaves the existing data intact by
/// construction rather than by rolling something back.
/// </summary>
public sealed class SqliteRestoreService(
    IAppPaths appPaths,
    IBackupService backupService,
    IPermissionEnforcer permissionEnforcer) : IRestoreService
{
    private static readonly string[] CountedTables =
    [
        "Products", "Inventories", "Customers", "Suppliers", "Sales", "SaleItems",
        "Purchases", "Expenses", "Users", "AuditLogs",
    ];

    public async Task<BackupInfo> GetBackupInfoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var validation = await backupService.ValidateBackupAsync(filePath, cancellationToken);
        if (!validation.IsValid || validation.Manifest is null)
        {
            throw new InvalidOperationException(
                "This backup cannot be read: " + string.Join("; ", validation.Errors));
        }

        var extracted = Path.Combine(Path.GetTempPath(), "kirana-info-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            ExtractDatabase(filePath, extracted);
            return new BackupInfo
            {
                FilePath = filePath,
                FileSizeBytes = new FileInfo(filePath).Length,
                Manifest = validation.Manifest,
                RowCounts = ReadRowCounts(extracted),
            };
        }
        finally
        {
            TryDelete(extracted);
        }
    }

    public async Task<RestoreResult> RestoreAsync(string filePath, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);

        var validation = await backupService.ValidateBackupAsync(filePath, cancellationToken);
        if (!validation.IsValid)
        {
            return RestoreResult.Failed(
                "Restore cancelled — this backup failed validation: " + string.Join("; ", validation.Errors));
        }

        // A safety backup of the CURRENT data is mandatory and must succeed before anything is
        // replaced; without it a restore is an irreversible action, which this app never performs.
        var safety = await backupService.CreateBackupAsync(
            BackupType.PreRestoreSafety, performedByUserId,
            notes: $"Automatic safety backup taken before restoring {Path.GetFileName(filePath)}",
            cancellationToken);

        if (!safety.Succeeded)
        {
            return RestoreResult.Failed(
                $"Restore cancelled — could not create a safety backup of the current data first: {safety.ErrorMessage}");
        }

        var staging = Path.Combine(Path.GetTempPath(), "kirana-restore-" + Guid.NewGuid().ToString("N") + ".db");
        var warnings = new List<string>();

        try
        {
            ExtractDatabase(filePath, staging);

            var integrity = SqliteBackupService.RunIntegrityCheck(staging);
            if (integrity is not "ok")
            {
                return RestoreResult.Failed(
                    $"Restore cancelled — the extracted database failed a final integrity check: {integrity}",
                    safety.FilePath);
            }

            warnings.AddRange(RestoreAssets(filePath));

            SwapInRestoredDatabase(staging);

            // Written straight into the freshly restored file: the restored database's own audit
            // trail is what an operator will look at afterwards, and it would otherwise have no
            // record that a restore ever happened.
            RecordRestoreInRestoredDatabase(performedByUserId, filePath, safety.FilePath);

            return new RestoreResult
            {
                Succeeded = true,
                SafetyBackupPath = safety.FilePath,
                Warnings = warnings,
            };
        }
        catch (Exception ex)
        {
            return RestoreResult.Failed(
                $"Restore failed: {ex.Message}. Your existing data was left in place; a safety backup is also available.",
                safety.FilePath);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Copy-then-replace, never write-in-place: the live path is only ever swapped for a file that
    /// is already complete on disk, so an interruption mid-operation cannot leave a half-written
    /// database behind.
    /// </summary>
    private void SwapInRestoredDatabase(string stagingPath)
    {
        var livePath = appPaths.DatabaseFilePath;

        // Release every pooled handle to the live file first, or the replace fails on Windows with
        // a sharing violation while EF's connection pool still holds it open.
        SqliteConnection.ClearAllPools();

        var incoming = livePath + ".incoming";
        File.Copy(stagingPath, incoming, overwrite: true);

        if (File.Exists(livePath))
        {
            var previous = livePath + ".previous";
            TryDelete(previous);
            File.Replace(incoming, livePath, previous, ignoreMetadataErrors: true);
            TryDelete(previous);
        }
        else
        {
            File.Move(incoming, livePath);
        }

        // SQLite's write-ahead log and shared-memory files belong to the database that was just
        // replaced; leaving them would let stale pages be replayed over the restored data.
        TryDelete(livePath + "-wal");
        TryDelete(livePath + "-shm");
    }

    private IReadOnlyList<string> RestoreAssets(string backupFilePath)
    {
        var warnings = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(backupFilePath);
            var assetEntries = archive.Entries
                .Where(e => e.FullName.StartsWith(SqliteBackupService.AssetsEntryPrefix, StringComparison.Ordinal) && e.Length > 0)
                .ToList();

            if (assetEntries.Count == 0)
            {
                return warnings;
            }

            Directory.CreateDirectory(appPaths.AssetsDirectory);

            foreach (var entry in assetEntries)
            {
                var relative = entry.FullName[SqliteBackupService.AssetsEntryPrefix.Length..];
                var target = Path.Combine(appPaths.AssetsDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
                catch (Exception ex)
                {
                    // Assets are cosmetic next to transactional data — a locked or unwritable logo
                    // file is reported, never a reason to abandon a restore.
                    warnings.Add($"Could not restore asset '{relative}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not read assets from the backup: {ex.Message}");
        }

        return warnings;
    }

    private void RecordRestoreInRestoredDatabase(int? performedByUserId, string restoredFrom, string? safetyBackupPath)
    {
        using var connection = new SqliteConnection(
            SqliteBackupService.ExclusiveConnectionString(appPaths.DatabaseFilePath));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AuditLogs (UserId, Action, Entity, EntityId, TimestampUtc, PreviousValue, NewValue, Reason, CreatedAtUtc)
            VALUES ($userId, 'RestorePerformed', 'Database', NULL, $timestamp, $safety, $restoredFrom, NULL, $timestamp);
            """;

        // The restored database is a different database — the user id that authorized the restore
        // may not exist in it, so it is recorded in the message rather than as a foreign key.
        command.Parameters.AddWithValue("$userId", DBNull.Value);
        command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$safety", (object?)safetyBackupPath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$restoredFrom",
            $"Restored from {Path.GetFileName(restoredFrom)} by user id {performedByUserId?.ToString() ?? "unknown"}");

        command.ExecuteNonQuery();
    }

    private static void ExtractDatabase(string bundlePath, string destinationPath)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        var entry = archive.GetEntry(SqliteBackupService.DatabaseEntryName)
            ?? throw new InvalidOperationException("The bundle contains no database.db.");
        entry.ExtractToFile(destinationPath, overwrite: true);
    }

    private static IReadOnlyList<(string Table, long RowCount)> ReadRowCounts(string databaseFilePath)
    {
        var counts = new List<(string, long)>();

        using var connection = new SqliteConnection(
            SqliteBackupService.ExclusiveConnectionString(databaseFilePath, readOnly: true));
        connection.Open();

        foreach (var table in CountedTables)
        {
            using var command = connection.CreateCommand();
            // Table names come from a fixed private array, never from user input.
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            try
            {
                counts.Add((table, Convert.ToInt64(command.ExecuteScalar())));
            }
            catch (SqliteException)
            {
                // An older backup may predate a table added in a later phase; report it as absent
                // rather than refusing to describe the backup at all.
                counts.Add((table, -1));
            }
        }

        return counts;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
