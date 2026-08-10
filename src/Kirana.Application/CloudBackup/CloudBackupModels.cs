namespace Kirana.Application.CloudBackup;

public enum CloudBackupProviderKind
{
    None,
    GoogleDrive,
    OneDrive,
}

public sealed record CloudAccountInfo(string Email, string DisplayName);

public sealed record CloudBackupEntry(
    string FileName,
    DateTime CreatedAtUtc,
    long SizeBytes,
    string StoreName,
    string ProviderName);

public sealed class CloudOperationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public static CloudOperationResult Success() => new() { Succeeded = true };
    public static CloudOperationResult Failed(string message) => new() { ErrorMessage = message };
}

