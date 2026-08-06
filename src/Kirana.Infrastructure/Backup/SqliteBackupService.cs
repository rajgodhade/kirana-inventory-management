using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Infrastructure.Backup;

/// <summary>
/// Writes backup bundles using SQLite's own online backup API. Lives in Infrastructure rather than
/// Application because it needs <see cref="SqliteConnection"/> directly — the same reason
/// <c>EfAuditLogger</c> and <c>ZXingBarcodeRenderer</c> do.
/// </summary>
public sealed class SqliteBackupService(
    IKiranaDbContext db,
    IAppPaths appPaths,
    IPermissionEnforcer permissionEnforcer,
    IAuditLogger auditLogger) : IBackupService
{
    internal const string DatabaseEntryName = "database.db";
    internal const string ManifestEntryName = "manifest.json";
    internal const string AssetsEntryPrefix = "assets/";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    public async Task<BackupResult> CreateBackupAsync(
        BackupType backupType, int? performedByUserId, string? notes = null, CancellationToken cancellationToken = default)
    {
        // Scheduled and pre-restore-safety backups run with no interactive user in context, so only
        // an operator-initiated backup is permission-checked. The automatic paths are reachable only
        // from the scheduler and from RestoreService, both of which are themselves gated.
        if (backupType == BackupType.Manual)
        {
            await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);
        }

        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var targetDirectory = ResolveBackupDirectory(settings);
        Directory.CreateDirectory(targetDirectory);

        var storeName = await db.Stores.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "kirana-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);

        var fileName = $"kirana-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{backupType.ToString().ToLowerInvariant()}.kbak";
        var destinationPath = Path.Combine(targetDirectory, fileName);

        try
        {
            var snapshotPath = Path.Combine(stagingDirectory, DatabaseEntryName);
            SnapshotDatabase(snapshotPath);

            var checksum = await ComputeSha256Async(snapshotPath, cancellationToken);
            var assetFiles = EnumerateAssetFiles();

            var manifest = new BackupManifest
            {
                CreatedAtUtc = DateTime.UtcNow,
                BackupType = backupType.ToString(),
                DatabaseSha256 = checksum,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                StoreName = storeName,
                AssetFileCount = assetFiles.Count,
            };

            WriteBundle(destinationPath, snapshotPath, manifest, assetFiles);

            // Verify what actually landed on disk, not what we believe we wrote — a backup that
            // cannot be validated is worse than no backup, because it invites false confidence.
            var validation = await ValidateBackupAsync(destinationPath, cancellationToken);
            if (!validation.IsValid)
            {
                TryDelete(destinationPath);
                var reason = string.Join("; ", validation.Errors);
                await auditLogger.RecordAsync(
                    performedByUserId, "BackupValidationFailed", "Backup", fileName,
                    reason: reason, cancellationToken: cancellationToken);

                return new BackupResult
                {
                    Succeeded = false,
                    ErrorMessage = $"The backup was written but failed verification and has been discarded: {reason}",
                };
            }

            var fileSize = new FileInfo(destinationPath).Length;
            var record = new BackupRecord
            {
                FileName = fileName,
                FilePath = destinationPath,
                FileSizeBytes = fileSize,
                BackupType = backupType,
                CreatedByUserId = performedByUserId,
                ChecksumSha256 = checksum,
                AppVersion = manifest.AppVersion,
                IsVerified = true,
                Notes = notes,
            };

            db.BackupRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);

            await auditLogger.RecordAsync(
                performedByUserId, "BackupCreated", nameof(BackupRecord), record.Id.ToString(),
                newValue: $"{backupType}, {fileSize} bytes, {fileName}", cancellationToken: cancellationToken);

            var deleted = await CleanupExpiredBackupsAsync(settings?.BackupRetentionCount ?? 14, cancellationToken);

            return new BackupResult
            {
                Succeeded = true,
                FilePath = destinationPath,
                FileSizeBytes = fileSize,
                BackupRecordId = record.Id,
                DeletedByRetentionCount = deleted,
            };
        }
        catch (Exception ex)
        {
            TryDelete(destinationPath);
            return new BackupResult { Succeeded = false, ErrorMessage = ex.Message };
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public async Task<BackupValidationResult> ValidateBackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return BackupValidationResult.Invalid($"Backup file not found: {filePath}");
        }

        var extractedDatabase = Path.Combine(Path.GetTempPath(), "kirana-validate-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            BackupManifest manifest;
            using (var archive = ZipFile.OpenRead(filePath))
            {
                var manifestEntry = archive.GetEntry(ManifestEntryName);
                if (manifestEntry is null)
                {
                    return BackupValidationResult.Invalid("The bundle has no manifest.json — it is not a Kirana backup.");
                }

                var databaseEntry = archive.GetEntry(DatabaseEntryName);
                if (databaseEntry is null)
                {
                    return BackupValidationResult.Invalid("The bundle contains no database.db.");
                }

                await using (var manifestStream = manifestEntry.Open())
                {
                    var parsed = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, ManifestJsonOptions, cancellationToken);
                    if (parsed is null)
                    {
                        return BackupValidationResult.Invalid("The manifest could not be read.");
                    }

                    manifest = parsed;
                }

                if (manifest.SchemaVersion != 1)
                {
                    return BackupValidationResult.Invalid(
                        $"Backup format version {manifest.SchemaVersion} is not supported by this version of Kirana.");
                }

                databaseEntry.ExtractToFile(extractedDatabase, overwrite: true);
            }

            var actualChecksum = await ComputeSha256Async(extractedDatabase, cancellationToken);
            if (!string.Equals(actualChecksum, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
            {
                return BackupValidationResult.Invalid(
                    "The database inside this backup does not match its recorded checksum — the file is corrupted or was modified.");
            }

            var integrity = RunIntegrityCheck(extractedDatabase);
            if (integrity is not "ok")
            {
                return BackupValidationResult.Invalid($"SQLite integrity check failed on the backup's database: {integrity}");
            }

            return new BackupValidationResult { IsValid = true, Manifest = manifest };
        }
        catch (InvalidDataException)
        {
            return BackupValidationResult.Invalid("The file is not a readable backup bundle (bad or truncated archive).");
        }
        catch (Exception ex)
        {
            return BackupValidationResult.Invalid($"The backup could not be validated: {ex.Message}");
        }
        finally
        {
            TryDelete(extractedDatabase);
        }
    }

    public async Task<IReadOnlyList<BackupHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var records = await db.BackupRecords
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return records
            .Select(r => new BackupHistoryEntry { Record = r, FileExists = File.Exists(r.FilePath) })
            .ToList();
    }

    public async Task DeleteBackupAsync(int backupRecordId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.BackupRestore, cancellationToken);

        var record = await db.BackupRecords.FirstOrDefaultAsync(b => b.Id == backupRecordId, cancellationToken)
            ?? throw new InvalidOperationException("Backup not found.");

        TryDelete(record.FilePath);
        db.BackupRecords.Remove(record);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BackupDeleted", nameof(BackupRecord), backupRecordId.ToString(),
            previousValue: record.FileName, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Trims history down to the retention count. Only ever touches files it has a
    /// <see cref="BackupRecord"/> row for — never a blind directory sweep, so pointing the backup
    /// folder at a directory holding other files is safe.
    /// </summary>
    private async Task<int> CleanupExpiredBackupsAsync(int retentionCount, CancellationToken cancellationToken)
    {
        var keep = Math.Max(1, retentionCount);

        var expired = await db.BackupRecords
            .OrderByDescending(b => b.CreatedAtUtc)
            .Skip(keep)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var record in expired)
        {
            TryDelete(record.FilePath);
        }

        db.BackupRecords.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private string ResolveBackupDirectory(AppSettings? settings) =>
        string.IsNullOrWhiteSpace(settings?.BackupDirectory) ? appPaths.BackupsDirectory : settings.BackupDirectory;

    /// <summary>
    /// Uses SQLite's online backup API against the live database. This is the whole reason a plain
    /// <c>File.Copy</c> is never used: <c>BackupDatabase</c> cooperates with SQLite's own locking, so
    /// a sale being written at this exact moment is neither blocked nor captured half-committed.
    /// </summary>
    private void SnapshotDatabase(string destinationPath)
    {
        using var source = new SqliteConnection(ExclusiveConnectionString(appPaths.DatabaseFilePath));
        using var destination = new SqliteConnection(ExclusiveConnectionString(destinationPath));
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    /// <summary>
    /// Pooling is switched off for every raw connection this feature opens. A pooled connection
    /// keeps its file handle alive after <c>Dispose</c>, which blocks the very next step here —
    /// zipping the snapshot, deleting a temp file, or replacing the live database during a restore.
    /// </summary>
    internal static string ExclusiveConnectionString(string databaseFilePath, bool readOnly = false) =>
        $"Data Source={databaseFilePath};Pooling=False" + (readOnly ? ";Mode=ReadOnly" : string.Empty);

    private IReadOnlyList<string> EnumerateAssetFiles() =>
        Directory.Exists(appPaths.AssetsDirectory)
            ? Directory.GetFiles(appPaths.AssetsDirectory, "*", SearchOption.AllDirectories)
            : [];

    private void WriteBundle(string destinationPath, string snapshotPath, BackupManifest manifest, IReadOnlyList<string> assetFiles)
    {
        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);

        archive.CreateEntryFromFile(snapshotPath, DatabaseEntryName, CompressionLevel.Optimal);

        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using (var manifestStream = manifestEntry.Open())
        {
            JsonSerializer.Serialize(manifestStream, manifest, ManifestJsonOptions);
        }

        foreach (var assetFile in assetFiles)
        {
            var relative = Path.GetRelativePath(appPaths.AssetsDirectory, assetFile).Replace('\\', '/');
            archive.CreateEntryFromFile(assetFile, AssetsEntryPrefix + relative, CompressionLevel.Optimal);
        }
    }

    internal static string RunIntegrityCheck(string databaseFilePath)
    {
        using var connection = new SqliteConnection(ExclusiveConnectionString(databaseFilePath, readOnly: true));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return command.ExecuteScalar()?.ToString() ?? "unknown";
    }

    internal static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        // Shared read: the live database legitimately has open SQLite handles against it, and
        // hashing must never be the thing that fails because of them.
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
            // A leftover temp/partial file is not worth failing an otherwise-successful operation over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
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
