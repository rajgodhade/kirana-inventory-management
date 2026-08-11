using System.Text;
using System.Runtime.InteropServices;

namespace Kirana.Infrastructure.CloudBackup;

public interface ICloudTokenStore
{
    Task<string?> ReadAsync(string provider, CancellationToken cancellationToken = default);
    Task WriteAsync(string provider, string token, CancellationToken cancellationToken = default);
    Task DeleteAsync(string provider, CancellationToken cancellationToken = default);
}

/// <summary>Stores provider credentials encrypted for the current Windows user. The token file is
/// deliberately outside the database and is never included in a .kbak bundle or audit log.</summary>
public sealed class WindowsCloudTokenStore : ICloudTokenStore
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kirana", "CloudTokens");

    public async Task<string?> ReadAsync(string provider, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, provider + ".token");
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        try { return Encoding.UTF8.GetString(Unprotect(protectedBytes)); }
        catch { return null; }
    }

    public async Task WriteAsync(string provider, string token, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var bytes = Protect(Encoding.UTF8.GetBytes(token));
        await File.WriteAllBytesAsync(Path.Combine(_directory, provider + ".token"), bytes, cancellationToken);
    }

    public Task DeleteAsync(string provider, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, provider + ".token");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] data) => CryptProtect(data);
    private static byte[] Unprotect(byte[] data) => CryptUnprotect(data);

    private static byte[] CryptProtect(byte[] data)
    {
        var input = new DataBlob(data); var output = new DataBlob();
        if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output)) throw new InvalidOperationException("Windows data protection failed.");
        return output.ToArray();
    }

    private static byte[] CryptUnprotect(byte[] data)
    {
        var input = new DataBlob(data); var output = new DataBlob();
        if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref output)) throw new InvalidOperationException("Windows data protection failed.");
        return output.ToArray();
    }

    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob
    {
        public int cbData; public IntPtr pbData;
        public DataBlob(byte[] bytes) { cbData = bytes.Length; pbData = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, pbData, bytes.Length); }
        public byte[] ToArray() { var bytes = new byte[cbData]; Marshal.Copy(pbData, bytes, 0, cbData); Marshal.FreeHGlobal(pbData); return bytes; }
    }
}
