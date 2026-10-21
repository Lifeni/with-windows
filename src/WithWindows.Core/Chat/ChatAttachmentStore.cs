using System.Text;

namespace WithWindows.Core.Chat;

/// <summary>保存并读取会话图片附件；单张图片上限 1 MiB。</summary>
public sealed class ChatAttachmentStore
{
    public const int MaxBytes = 1024 * 1024;

    private static readonly HashSet<string> SupportedMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/webp",
            "image/gif",
        };

    public string DirectoryPath { get; }

    public ChatAttachmentStore(string directoryPath)
        => DirectoryPath = directoryPath;

    public ChatAttachment Save(
        ReadOnlySpan<byte> bytes,
        string displayName,
        string mimeType,
        int width,
        int height)
    {
        EnsureSupportedMimeType(mimeType);
        if (bytes.Length > MaxBytes)
            throw new ArgumentException("图片不能超过 1 MiB。", nameof(bytes));

        string id = Guid.NewGuid().ToString("N");
        string path = PathFor(id, mimeType);
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllBytes(path, bytes.ToArray());

        return new ChatAttachment(
            id,
            NormalizeDisplayName(displayName, mimeType),
            mimeType.ToLowerInvariant(),
            bytes.Length,
            width,
            height);
    }

    public byte[] LoadBytes(ChatAttachment attachment)
        => File.ReadAllBytes(PathFor(attachment.Id, attachment.MimeType));

    public string LoadDataUrl(ChatAttachment attachment)
    {
        byte[] bytes = LoadBytes(attachment);
        return $"data:{attachment.MimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    public void Delete(ChatAttachment attachment)
    {
        string path = PathFor(attachment.Id, attachment.MimeType);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Clear()
    {
        if (!Directory.Exists(DirectoryPath))
            return;

        foreach (string file in Directory.EnumerateFiles(DirectoryPath))
            File.Delete(file);
    }

    public static bool IsSupportedMimeType(string mimeType)
        => SupportedMimeTypes.Contains(mimeType);

    public static string ExtensionForMimeType(string mimeType)
        => mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => throw new ArgumentException("当前仅支持 PNG、JPEG、WebP 或 GIF 图片。", nameof(mimeType)),
        };

    private string PathFor(string id, string mimeType)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("附件 ID 无效。", nameof(id));

        EnsureSupportedMimeType(mimeType);
        return Path.Combine(DirectoryPath, id + ExtensionForMimeType(mimeType));
    }

    private static void EnsureSupportedMimeType(string mimeType)
    {
        if (!IsSupportedMimeType(mimeType))
            throw new ArgumentException(
                "当前仅支持 PNG、JPEG、WebP 或 GIF 图片。",
                nameof(mimeType));
    }

    private static string NormalizeDisplayName(string displayName, string mimeType)
    {
        string name = string.IsNullOrWhiteSpace(displayName)
            ? "图片" + ExtensionForMimeType(mimeType)
            : displayName.Trim();
        return name.Length <= 120 ? name : name[..120];
    }
}
