namespace WithWindows.Core.Theme;

/// <summary>
/// 主题壁纸文件管理：把用户选择的图片复制到应用数据目录，避免原文件移动或删除后失效。
/// 只允许常见静态壁纸格式，删除时只处理本目录内的文件。
/// </summary>
public static class WallpaperStore
{
    private static readonly string[] AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>复制壁纸并返回新路径；文件不存在或格式不支持时抛异常。</summary>
    public static string Import(
        string sourcePath,
        string directory,
        string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("选择的壁纸文件不存在。", sourcePath);

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension, StringComparer.Ordinal))
            throw new ArgumentException("壁纸仅支持 JPG、PNG 或 BMP 图片。", nameof(sourcePath));

        Directory.CreateDirectory(directory);
        string fileName = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        string destination = Path.Combine(directory, fileName);
        File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    /// <summary>删除应用数据目录内的旧壁纸；目录外路径或不存在时什么都不做。</summary>
    public static void DeleteOwned(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string fullDirectory = Path.GetFullPath(directory);
        string fullPath = Path.GetFullPath(path);
        string prefix = fullDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            File.Delete(fullPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>配置文件中的壁纸仍存在时返回原路径，否则返回 null。</summary>
    public static string? ExistingOrNull(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
}
