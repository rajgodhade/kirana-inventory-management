using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.CloudBackup;

/// <summary>Orchestrates validated local backups and cloud transfer. Billing never depends on it.</summary>
public sealed class CloudBackupService(
    IKiranaDbContext db,
    IBackupService backupService,
    IEnumerable<ICloudBackupProvider> providers,
    IAuditLogger auditLogger) : ICloudBackupService
{
    private ICloudBackupProvider? CurrentProvider => providers.FirstOrDefault(p => p.Kind == Provider);

    public CloudBackupProviderKind Provider => Enum.TryParse<CloudBackupProviderKind>(
        db.AppSettings.AsNoTracking().Select(s => s.CloudBackupProvider).FirstOrDefault() ?? "None", out var value) ? value : CloudBackupProviderKind.None;

    public async Task<CloudOperationResult> ConnectAsync(CloudBackupProviderKind provider, CancellationToken cancellationToken = default)
    {
        var implementation = providers.FirstOrDefault(p => p.Kind == provider);
        if (implementation is null) return CloudOperationResult.Failed("That cloud provider is not available.");
        CloudOperationResult result;
        try
        {
            result = await implementation.ConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return CloudOperationResult.Failed("Google sign-in timed out or was cancelled.");
        }
        catch (Exception)
        {
            return CloudOperationResult.Failed("Cloud sign-in could not be completed. Check your connection and try again.");
        }
        if (!result.Succeeded) return result;
        var settings = await db.AppSettings.FirstAsync(cancellationToken);
        settings.CloudBackupProvider = provider.ToString();
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(null, "CloudProviderConnected", nameof(AppSettings), settings.Id.ToString(), newValue: provider.ToString(), cancellationToken: cancellationToken);
        return result;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentProvider is not null) await CurrentProvider.DisconnectAsync(cancellationToken);
        var settings = await db.AppSettings.FirstAsync(cancellationToken);
        settings.CloudBackupProvider = CloudBackupProviderKind.None.ToString();
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(null, "CloudProviderDisconnected", nameof(AppSettings), settings.Id.ToString(), cancellationToken: cancellationToken);
    }

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => CurrentProvider?.IsConnectedAsync(cancellationToken) ?? Task.FromResult(false);
    public Task<CloudAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default) => CurrentProvider?.GetAccountInfoAsync(cancellationToken) ?? Task.FromResult<CloudAccountInfo?>(null);
    public async Task<IReadOnlyList<CloudBackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var provider = CurrentProvider;
        if (provider is null) return [];
        var store = await db.Stores.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync(cancellationToken) ?? "Store";
        return await provider.ListBackupsAsync(store, cancellationToken);
    }

    public async Task<CloudOperationResult> BackupNowAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        var provider = CurrentProvider;
        if (provider is null || !await provider.IsConnectedAsync(cancellationToken)) return CloudOperationResult.Failed("Connect a cloud account before starting a cloud backup.");
        var local = await backupService.CreateBackupAsync(BackupType.Manual, performedByUserId, "Cloud backup source", cancellationToken);
        if (!local.Succeeded || string.IsNullOrWhiteSpace(local.FilePath)) return CloudOperationResult.Failed(local.ErrorMessage ?? "The local backup could not be created.");
        var validation = await backupService.ValidateBackupAsync(local.FilePath, cancellationToken);
        if (!validation.IsValid) return CloudOperationResult.Failed($"The local backup failed verification: {string.Join("; ", validation.Errors)}");
        return await UploadValidatedBackupAsync(local.FilePath, cancellationToken);
    }

    public async Task<CloudOperationResult> UploadValidatedBackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var provider = CurrentProvider;
        if (provider is null || !await provider.IsConnectedAsync(cancellationToken)) return CloudOperationResult.Failed("Connect a cloud account before starting a cloud backup.");
        if (!File.Exists(filePath)) return CloudOperationResult.Failed("The local backup file is no longer available.");
        var validation = await backupService.ValidateBackupAsync(filePath, cancellationToken);
        if (!validation.IsValid) return CloudOperationResult.Failed($"The local backup failed verification: {string.Join("; ", validation.Errors)}");
        var store = await db.Stores.AsNoTracking().Select(s => s.Name).FirstOrDefaultAsync(cancellationToken) ?? "Store";
        var uploaded = await provider.UploadBackupAsync(filePath, store, cancellationToken);
        var settings = await db.AppSettings.FirstAsync(cancellationToken);
        if (uploaded.Succeeded) settings.LastCloudBackupUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(null, uploaded.Succeeded ? "CloudBackupCompleted" : "CloudBackupFailed", nameof(BackupRecord), Path.GetFileName(filePath), reason: uploaded.ErrorMessage, cancellationToken: cancellationToken);
        return uploaded;
    }
}
