namespace Kirana.Infrastructure.CloudBackup;

public sealed class GoogleOAuthOptions
{
    public string ClientId { get; init; } = Environment.GetEnvironmentVariable("KIRANA_GOOGLE_CLIENT_ID") ?? string.Empty;
    public string ClientSecret { get; init; } = Environment.GetEnvironmentVariable("KIRANA_GOOGLE_CLIENT_SECRET") ?? string.Empty;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
