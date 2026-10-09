using System;
using System.IO;

namespace WithWindows.Services;

/// <summary>
/// 应用图标路径。图标在运行期从磁盘读（`Assets\with-windows.ico`），因此安装包与便携包都必须带上 `Assets\`。
/// 注意：不能拿 exe 路径当兜底——WinUIEx 的 <c>TrayIcon</c> 只接受 `.ico` / `.svg` 文件。
/// </summary>
public static class AppIcon
{
    /// <summary>Assets 下的图标路径。</summary>
    public static string Preferred { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");

    /// <summary>Assets 下的 PNG 图标路径（界面上显示用；`ms-appx:///` 在未打包应用里不可靠）。</summary>
    public static string PreferredPng { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.png");

    /// <summary>图标文件是否存在（缺失说明打包漏了 Assets）。</summary>
    public static bool Exists => File.Exists(Preferred);

    /// <summary>PNG 图标是否存在。</summary>
    public static bool PngExists => File.Exists(PreferredPng);
}
