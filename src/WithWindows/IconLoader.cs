using System.Drawing;

namespace WithWindows;

/// <summary>加载嵌入资源中的应用图标；失败时回退系统默认图标。托盘与记事本共用。</summary>
internal static class IconLoader
{
    public static Icon Load()
    {
        try
        {
            using var stream = typeof(IconLoader).Assembly.GetManifestResourceStream(
                "WithWindows.Assets.with-windows.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch
        {
            // 回退到系统图标
        }
        return SystemIcons.Application;
    }
}
