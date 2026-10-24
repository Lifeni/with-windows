namespace WithWindows.Core.App;

/// <summary>应用自恢复重启时使用的命令行参数。</summary>
public static class RestartCommand
{
    private const string WaitForProcessPrefix = "--restart-after=";
    private const string RestartCountPrefix = "--restart-count=";
    private const string PagePrefix = "--page=";
    private const string ReadyEventPrefix = @"Local\WithWindows.RestartReady.";

    /// <summary>读取需要等待退出的旧进程号；非法或非正值返回 null。</summary>
    public static int? GetWaitForProcessId(IEnumerable<string> args)
    {
        string? value = args.FirstOrDefault(argument =>
            argument.StartsWith(WaitForProcessPrefix, StringComparison.OrdinalIgnoreCase));
        if (value is null
            || !int.TryParse(value[WaitForProcessPrefix.Length..], out int processId)
            || processId <= 0)
        {
            return null;
        }

        return processId;
    }

    /// <summary>读取重启后要恢复的页面标识。</summary>
    public static string? GetPageTag(IEnumerable<string> args)
    {
        string? value = args.FirstOrDefault(argument =>
            argument.StartsWith(PagePrefix, StringComparison.OrdinalIgnoreCase));
        string? page = value?[PagePrefix.Length..];
        return string.IsNullOrWhiteSpace(page) ? null : page;
    }

    /// <summary>读取当前自动恢复链已执行的重启次数；缺失或非法时按首次启动处理。</summary>
    public static int GetRestartCount(IEnumerable<string> args)
    {
        string? value = args.FirstOrDefault(argument =>
            argument.StartsWith(RestartCountPrefix, StringComparison.OrdinalIgnoreCase));
        return value is not null
            && int.TryParse(value[RestartCountPrefix.Length..], out int restartCount)
            && restartCount > 0
                ? restartCount
                : 0;
    }

    /// <summary>子进程进入 WinUI 启动流程后用于通知旧进程的命名事件。</summary>
    public static string GetReadyEventName(int previousProcessId)
        => $"{ReadyEventPrefix}{previousProcessId}";
}
