namespace Kirana.Application.CloudBackup;

/// <summary>Provider-specific cloud storage boundary. Implementations must never expose tokens to callers.</summary>
public interface ICloudBackupProvider
{
    CloudBackupProviderKind Kind { get; }
    Task<CloudOperationResult> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);
    Task<CloudAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default);
    Task<CloudOperationResult> UploadBackupAsync(string filePath, string storeName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudBackupEntry>> ListBackupsAsync(string storeName, CancellationToken cancellationToken = default);
    Task<CloudOperationResult> DownloadBackupAsync(CloudBackupEntry entry, string destinationPath, CancellationToken cancellationToken = default);
    Task<CloudOperationResult> DeleteBackupAsync(CloudBackupEntry entry, CancellationToken cancellationToken = default);
}

