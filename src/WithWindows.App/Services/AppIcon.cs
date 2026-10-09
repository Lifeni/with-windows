using System;
using System.IO;

namespace WithWindows.Services;

/// <summary>
/// 应用图标路径。图标是运行期从磁盘读的（`Assets\with-windows.ico`），
/// 缺文件时退回 exe 自身的图标（csproj 的 ApplicationIcon 已把图标嵌进 exe），
/// 避免便携版漏带 Assets 时托盘图标直接丢失。
/// </summary>
public static class AppIcon
{
    /// <summary>Assets 下的图标路径（存在时优先）。</summary>
    public static string Preferred { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");

    /// <summary>解析出可用的图标路径；Assets 缺失时退回 exe 自身。</summary>
    public static string Resolve()
    {
        if (File.Exists(Preferred))
            return Preferred;

        return Environment.ProcessPath ?? Preferred;
    }
}
