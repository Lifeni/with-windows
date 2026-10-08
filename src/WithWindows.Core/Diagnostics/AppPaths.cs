namespace WithWindows.Core.Diagnostics;

/// <summary>
/// 应用数据路径。运行时数据全部位于 %APPDATA%\WithWindows\，程序目录只放程序与资源。
/// </summary>
public static class AppPaths
{
    /// <summary>数据根目录：%APPDATA%\WithWindows。</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WithWindows");

    /// <summary>配置文件路径。</summary>
    public static string ConfigPath { get; } = Path.Combine(DataRoot, "config.json");

    /// <summary>日志文件路径。</summary>
    public static string LogPath { get; } = Path.Combine(DataRoot, "log.txt");

    /// <summary>记事本草稿路径。</summary>
    public static string NotepadDraftPath { get; } = Path.Combine(DataRoot, "notepad.txt");
}
