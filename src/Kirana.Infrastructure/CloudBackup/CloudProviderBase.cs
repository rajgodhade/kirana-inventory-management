using Kirana.Application.CloudBackup;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kirana.Infrastructure.CloudBackup;

/// <summary>
/// Shared safety boundary for official provider adapters. OAuth client IDs are supplied through
/// deployment configuration; no client secret is compiled into the app. API-specific work belongs
/// in the concrete adapter and can be enabled without changing application/business code.
/// </summary>
public abstract class CloudProviderBase(ICloudTokenStore tokenStore) : ICloudBackupProvider
{
    protected ICloudTokenStore Tokens { get; } = tokenStore;
    public abstract CloudBackupProviderKind Kind { get; }
    protected abstract string ProviderKey { get; }

    public virtual async Task<CloudOperationResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        // A deployment must provide its official OAuth client configuration. We never pretend to
        // connect when it is absent; the settings screen can still be used offline.
        return CloudOperationResult.Failed($"{Kind} connection is not configured for this installation. Contact your administrator to enable it.");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Tokens.DeleteAsync(ProviderKey, cancellationToken);
    public virtual async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => !string.IsNullOrWhiteSpace(await Tokens.ReadAsync(ProviderKey, cancellationToken));
    public virtual Task<CloudAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult<CloudAccountInfo?>(null);

    public virtual Task<CloudOperationResult> UploadBackupAsync(string filePath, string storeName, CancellationToken cancellationToken = default) =>
        Task.FromResult(CloudOperationResult.Failed("Cloud upload is unavailable. Reconnect the cloud account and try again."));

    public virtual Task<IReadOnlyList<CloudBackupEntry>> ListBackupsAsync(string storeName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudBackupEntry>>([]);

    public virtual Task<CloudOperationResult> DownloadBackupAsync(CloudBackupEntry entry, string destinationPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(CloudOperationResult.Failed("Cloud download is unavailable. Reconnect the cloud account and try again."));

    public virtual Task<CloudOperationResult> DeleteBackupAsync(CloudBackupEntry entry, CancellationToken cancellationToken = default) =>
        Task.FromResult(CloudOperationResult.Failed("Cloud delete is unavailable. Reconnect the cloud account and try again."));
}

public sealed class GoogleDriveBackupProvider(ICloudTokenStore tokenStore, HttpClient httpClient) : CloudProviderBase(tokenStore)
{
    public override CloudBackupProviderKind Kind => CloudBackupProviderKind.GoogleDrive;
    protected override string ProviderKey => "GoogleDrive";

    private readonly GoogleOAuthOptions _options = new();
    // Identity scopes let Settings show which account owns the backups without depending on
    // the Drive API being enabled or a backup folder already existing.
    private const string Scope = "openid email https://www.googleapis.com/auth/drive.file";
    private const string Root = "https://www.googleapis.com/drive/v3";

    public override async Task<CloudOperationResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured) return CloudOperationResult.Failed("Google Drive is not configured. Set KIRANA_GOOGLE_CLIENT_ID and restart the app.");
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var port = RandomNumberGenerator.GetInt32(49152, 65500);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var state = Guid.NewGuid().ToString("N");
        var url = "https://accounts.google.com/o/oauth2/v2/auth?" +
            $"client_id={Uri.EscapeDataString(_options.ClientId)}&redirect_uri={Uri.EscapeDataString($"http://127.0.0.1:{port}/")}" +
            $"&response_type=code&scope={Uri.EscapeDataString(Scope)}&access_type=offline&prompt=consent" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256&state={state}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
        var query = context.Request.QueryString;
        var response = Encoding.UTF8.GetBytes("You can return to Kirana now.");
        context.Response.ContentLength64 = response.Length;
        await context.Response.OutputStream.WriteAsync(response, timeout.Token);
        context.Response.Close();
        if (query["state"] != state || string.IsNullOrWhiteSpace(query["code"])) return CloudOperationResult.Failed("Google sign-in was cancelled or could not be verified.");
        var form = new Dictionary<string, string> { ["code"] = query["code"]!, ["client_id"] = _options.ClientId, ["redirect_uri"] = $"http://127.0.0.1:{port}/", ["grant_type"] = "authorization_code", ["code_verifier"] = verifier };
        if (!string.IsNullOrWhiteSpace(_options.ClientSecret)) form["client_secret"] = _options.ClientSecret;
        var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode) return CloudOperationResult.Failed("Google sign-in could not be completed. Check the configured OAuth client.");
        var token = await tokenResponse.Content.ReadFromJsonAsync<GoogleToken>(cancellationToken: cancellationToken);
        if (token?.access_token is null) return CloudOperationResult.Failed("Google did not return an authorization token.");
        token.expires_at = DateTimeOffset.UtcNow.AddSeconds(token.expires_in).ToUnixTimeSeconds();
        token.account_email = GetEmailFromIdToken(token.id_token) ?? await GetEmailFromUserInfoAsync(token.access_token, cancellationToken);
        await Tokens.WriteAsync(ProviderKey, JsonSerializer.Serialize(token), cancellationToken);
        return CloudOperationResult.Success();
    }

    public override async Task<CloudAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken); if (token is null) return null;
        if (!string.IsNullOrWhiteSpace(token.account_email))
            return new CloudAccountInfo(token.account_email, token.account_email);

        var email = GetEmailFromIdToken(token.id_token) ?? await GetEmailFromUserInfoAsync(token.access_token!, cancellationToken);
        if (!string.IsNullOrWhiteSpace(email))
        {
            token.account_email = email;
            await Tokens.WriteAsync(ProviderKey, JsonSerializer.Serialize(token), cancellationToken);
            return new CloudAccountInfo(email, email);
        }

        // Backward compatibility for tokens created before the identity email scope was added.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/about?fields=user(displayName,emailAddress)");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.access_token);
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var info = await response.Content.ReadFromJsonAsync<GoogleAbout>(cancellationToken: cancellationToken);
        return info?.user?.emailAddress is null ? null : new CloudAccountInfo(info.user.emailAddress, info.user.displayName ?? info.user.emailAddress);
    }

    public override async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => await GetTokenAsync(cancellationToken) is not null;

    public override async Task<IReadOnlyList<CloudBackupEntry>> ListBackupsAsync(string storeName, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken);
        if (token?.access_token is null) return [];

        var results = new List<CloudBackupEntry>();
        string? pageToken = null;
        do
        {
            var q = Uri.EscapeDataString("name contains '.kbak' and trashed = false");
            var page = string.IsNullOrWhiteSpace(pageToken) ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}";
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{Root}/files?q={q}&orderBy=createdTime%20desc&pageSize=1000&fields=nextPageToken,files(id,name,size,createdTime){page}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.access_token);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return [];

            var files = await response.Content.ReadFromJsonAsync<GoogleFiles>(cancellationToken: cancellationToken);
            results.AddRange((files?.files ?? [])
                .Where(file => !string.IsNullOrWhiteSpace(file.name) && file.name.EndsWith(".kbak", StringComparison.OrdinalIgnoreCase))
                .Select(file => new CloudBackupEntry(
                    file.name!,
                    file.createdTime?.UtcDateTime ?? DateTime.MinValue,
                    long.TryParse(file.size, out var size) ? size : 0,
                    string.IsNullOrWhiteSpace(storeName) ? "Store" : storeName,
                    "Google Drive")));
            pageToken = files?.nextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        return results;
    }

    public override async Task<CloudOperationResult> UploadBackupAsync(string filePath, string storeName, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(cancellationToken); if (token is null) return CloudOperationResult.Failed("Google Drive authorization has expired. Reconnect the account.");
        var folder = await EnsureFolderAsync(storeName, token.access_token!, cancellationToken);
        if (folder is null) return CloudOperationResult.Failed("Could not create the VyaparOS backup folder in Google Drive.");
        // Google Drive's multipart upload protocol is multipart/related, not HTML-style
        // multipart/form-data. The first part is JSON metadata and the second is the raw file.
        using var content = new MultipartContent("related");
        using var metadata = new StringContent(
            JsonSerializer.Serialize(new { name = Path.GetFileName(filePath), parents = new[] { folder } }),
            Encoding.UTF8,
            "application/json");
        using var fileStream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(metadata);
        content.Add(fileContent);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,name") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.access_token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? CloudOperationResult.Success()
            : CloudOperationResult.Failed(await ReadGoogleErrorAsync(response, "Google Drive upload failed", cancellationToken));
    }

    private async Task<string?> EnsureFolderAsync(string storeName, string accessToken, CancellationToken cancellationToken)
    {
        string? parent = null;
        foreach (var name in new[] { "VyaparOS", "Backups", Sanitize(storeName) })
        {
            var parentId = parent;
            var q = Uri.EscapeDataString($"name = '{name.Replace("'", "\\'")}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false" + (parent is null ? " and 'root' in parents" : $" and '{parent}' in parents"));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Root}/files?q={q}&fields=files(id,name)"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var found = await (await httpClient.SendAsync(request, cancellationToken)).Content.ReadFromJsonAsync<GoogleFiles>(cancellationToken: cancellationToken);
            parent = found?.files?.FirstOrDefault()?.id;
            if (parent is null)
            {
                object folderMetadata = parentId is null
                    ? new { name, mimeType = "application/vnd.google-apps.folder" }
                    : new { name, mimeType = "application/vnd.google-apps.folder", parents = new[] { parentId } };
                using var create = new HttpRequestMessage(HttpMethod.Post, $"{Root}/files?fields=id") { Content = JsonContent.Create(folderMetadata) };
                create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var created = await (await httpClient.SendAsync(create, cancellationToken)).Content.ReadFromJsonAsync<GoogleFile>(cancellationToken: cancellationToken); parent = created?.id;
            }
            if (parent is null) return null;
        }
        return parent;
    }

    private static async Task<string> ReadGoogleErrorAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message) &&
                !string.IsNullOrWhiteSpace(message.GetString()))
                return $"{fallback}: {message.GetString()}";
        }
        catch (JsonException)
        {
            // Return a stable, user-safe fallback when Google sends a non-JSON response.
        }

        return $"{fallback} (HTTP {(int)response.StatusCode}). Check your connection and try again.";
    }

    private async Task<GoogleToken?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var json = await Tokens.ReadAsync(ProviderKey, cancellationToken); var token = json is null ? null : JsonSerializer.Deserialize<GoogleToken>(json);
        if (token is null) return null;
        if (token.expires_at > DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds()) return token;
        if (string.IsNullOrWhiteSpace(token.refresh_token) || !_options.IsConfigured) return null;
        var form = new Dictionary<string, string> { ["client_id"] = _options.ClientId, ["refresh_token"] = token.refresh_token, ["grant_type"] = "refresh_token" };
        if (!string.IsNullOrWhiteSpace(_options.ClientSecret)) form["client_secret"] = _options.ClientSecret;
        var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var refreshed = await response.Content.ReadFromJsonAsync<GoogleToken>(cancellationToken: cancellationToken); if (refreshed?.access_token is null) return null;
        refreshed.refresh_token ??= token.refresh_token;
        refreshed.id_token ??= token.id_token;
        refreshed.account_email = token.account_email ?? GetEmailFromIdToken(refreshed.id_token);
        refreshed.expires_at = DateTimeOffset.UtcNow.AddSeconds(refreshed.expires_in).ToUnixTimeSeconds();
        await Tokens.WriteAsync(ProviderKey, JsonSerializer.Serialize(refreshed), cancellationToken); return refreshed;
    }

    private async Task<string?> GetEmailFromUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var user = await response.Content.ReadFromJsonAsync<GoogleIdentityUser>(cancellationToken: cancellationToken);
        return user?.email;
    }

    private static string? GetEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string Sanitize(string value) => string.IsNullOrWhiteSpace(value) ? "Store" : string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
    private sealed class GoogleToken
    {
        public string? access_token { get; set; }
        public string? refresh_token { get; set; }
        public string? id_token { get; set; }
        public string? account_email { get; set; }
        public int expires_in { get; set; }
        public long expires_at { get; set; }
    }
    private sealed class GoogleIdentityUser { public string? email { get; set; } }
    private sealed class GoogleAbout { public GoogleUser? user { get; set; } }
    private sealed class GoogleUser { public string? emailAddress { get; set; } public string? displayName { get; set; } }
    private sealed class GoogleFiles { public List<GoogleFile>? files { get; set; } public string? nextPageToken { get; set; } }
    private sealed class GoogleFile
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? size { get; set; }
        public DateTimeOffset? createdTime { get; set; }
    }
}

public sealed class OneDriveBackupProvider(ICloudTokenStore tokenStore) : CloudProviderBase(tokenStore)
{
    public override CloudBackupProviderKind Kind => CloudBackupProviderKind.OneDrive;
    protected override string ProviderKey => "OneDrive";
}
