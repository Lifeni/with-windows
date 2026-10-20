using System.Text;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace WithWindows.Core.Chat;

/// <summary>使用当前 Windows 用户的 DPAPI 保护 API Key。</summary>
public sealed class ChatSecretStore
{
    private readonly string _path;

    public ChatSecretStore(string path) => _path = path;

    public async Task SaveAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Clear();
            return;
        }

        byte[] clearBytes = Encoding.UTF8.GetBytes(apiKey);
        try
        {
            var provider = new DataProtectionProvider("LOCAL=user");
            var input = CryptographicBuffer.CreateFromByteArray(clearBytes);
            var protectedBuffer = await provider.ProtectAsync(input);
            cancellationToken.ThrowIfCancellationRequested();

            CryptographicBuffer.CopyToByteArray(protectedBuffer, out byte[] protectedBytes);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            Array.Clear(clearBytes);
        }
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return null;

        byte[] protectedBytes =
            await File.ReadAllBytesAsync(_path, cancellationToken);
        var input = CryptographicBuffer.CreateFromByteArray(protectedBytes);
        var provider = new DataProtectionProvider();
        var protectedBuffer = await provider.UnprotectAsync(input);
        cancellationToken.ThrowIfCancellationRequested();

        CryptographicBuffer.CopyToByteArray(protectedBuffer, out byte[] clearBytes);
        try
        {
            return Encoding.UTF8.GetString(clearBytes);
        }
        finally
        {
            Array.Clear(clearBytes);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
            if (File.Exists(_path + ".tmp"))
                File.Delete(_path + ".tmp");
        }
        catch (IOException)
        {
        }
    }
}
