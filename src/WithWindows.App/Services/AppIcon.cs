using System;
using System.IO;
using WithWindows.Core.Config;

namespace WithWindows.Services;

/// <summary>
/// 应用图标路径。图标在运行期从磁盘读（`Assets\with-windows.ico`），因此安装包与便携包都必须带上 `Assets\`。
/// 注意：不能拿 exe 路径当兜底——WinUIEx 的 <c>TrayIcon</c> 只接受 `.ico` / `.svg` 文件。
/// </summary>
public static class AppIcon
{
    /// <summary>Assets 下的图标路径。</summary>
    public static string Preferred { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");

    /// <summary>图标文件是否存在（缺失说明打包漏了 Assets）。</summary>
    public static bool Exists => File.Exists(Preferred);

    /// <summary>
    /// 按配置和当前主题选择托盘图标；闪电图标缺失时回退到默认黄色图标，
    /// 避免切换样式后托盘没有入口。
    /// </summary>
    public static string ResolveTray(string iconStyle, bool darkTheme)
    {
        string normalized = TrayIconStyle.Normalize(iconStyle);
        string stem = normalized switch
        {
            TrayIconStyle.LightningThin => "tray-lightning-thin",
            _ => string.Empty,
        };
        string preferred = string.IsNullOrEmpty(stem)
            ? Preferred
            : Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                $"{stem}-{(darkTheme ? "dark" : "light")}.ico");
        if (File.Exists(preferred))
            return preferred;
        return File.Exists(Preferred) ? Preferred : preferred;
    }
}
