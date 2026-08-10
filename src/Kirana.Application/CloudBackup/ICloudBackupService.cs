namespace Kirana.Application.CloudBackup;

public interface ICloudBackupService
{
    CloudBackupProviderKind Provider { get; }
    Task<CloudOperationResult> BackupNowAsync(int? performedByUserId, CancellationToken cancellationToken = default);
    Task<CloudOperationResult> UploadValidatedBackupAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudBackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default);
    Task<CloudOperationResult> ConnectAsync(CloudBackupProviderKind provider, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);
    Task<CloudAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default);
}
