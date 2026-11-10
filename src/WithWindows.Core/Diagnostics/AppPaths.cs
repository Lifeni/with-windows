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

    /// <summary>轮转后的历史日志路径（当前日志超过上限时改名到此）。</summary>
    public static string LogRotatedPath { get; } = Path.Combine(DataRoot, "log.1.txt");

    /// <summary>记事本草稿路径。</summary>
    public static string NotepadDraftPath { get; } = Path.Combine(DataRoot, "notepad.txt");

    /// <summary>待办数据文件路径。</summary>
    public static string TodoPath { get; } = Path.Combine(DataRoot, "todos.json");

    /// <summary>定时任务数据目录。</summary>
    public static string ScheduledTasksDirectory { get; } =
        Path.Combine(DataRoot, "scheduled-tasks");

    /// <summary>定时任务定义文件路径。</summary>
    public static string ScheduledTasksPath { get; } =
        Path.Combine(ScheduledTasksDirectory, "tasks.json");

    /// <summary>定时任务执行历史文件路径。</summary>
    public static string ScheduledTaskRunsPath { get; } =
        Path.Combine(ScheduledTasksDirectory, "runs.json");

    /// <summary>内联脚本运行副本目录。</summary>
    public static string ScheduledTaskScriptsDirectory { get; } =
        Path.Combine(ScheduledTasksDirectory, "scripts");

    /// <summary>执行日志目录。</summary>
    public static string ScheduledTaskLogsDirectory { get; } =
        Path.Combine(ScheduledTasksDirectory, "logs");

    /// <summary>隐藏 script helper 的一次性运行规格目录。</summary>
    public static string ScheduledTaskRunnerDirectory { get; } =
        Path.Combine(ScheduledTasksDirectory, "runner");

    /// <summary>AI 对话数据目录。</summary>
    public static string ChatDirectory { get; } = Path.Combine(DataRoot, "chat");

    /// <summary>当前 AI 对话记录。</summary>
    public static string ChatHistoryPath { get; } = Path.Combine(ChatDirectory, "current.json");

    /// <summary>AI 对话记录备份。</summary>
    public static string ChatHistoryBackupPath => ChatHistoryPath + ".bak";

    /// <summary>DPAPI 保护的 API Key 文件。</summary>
    public static string ChatCredentialPath { get; } =
        Path.Combine(ChatDirectory, "credential.bin");

    /// <summary>会话图片附件目录。</summary>
    public static string ChatAttachmentsDirectory { get; } =
        Path.Combine(ChatDirectory, "attachments");
}
