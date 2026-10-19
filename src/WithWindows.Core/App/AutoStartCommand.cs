namespace WithWindows.Core.App;

/// <summary>
/// 开机自启项的命令行形式：注册表里写「exe 全路径 + <c>--tray</c>」，程序据此判断
/// 这次是不是开机拉起来的——是就只驻留托盘、不打开主窗口。
/// 纯字符串逻辑单独拆出来（自身不碰注册表），这样才能被单元测试覆盖。
/// </summary>
public static class AutoStartCommand
{
    /// <summary>开机自启专用参数：启动后隐藏到托盘，不弹出主窗口。</summary>
    public const string TrayArgument = "--tray";

    /// <summary>拼注册表里要写的命令行：路径加引号（防路径含空格时被截断），再带隐藏启动参数。</summary>
    public static string Build(string? exePath)
        => $"\"{exePath}\" {TrayArgument}";

    /// <summary>本次进程是不是由开机自启拉起来的。</summary>
    public static bool IsTrayLaunch(IEnumerable<string> args)
        => args.Any(arg => string.Equals(arg, TrayArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>注册表里的值是否已经是当前该写的形式：同一个 exe、且带隐藏启动参数。</summary>
    public static bool IsCurrent(string? command, string? exePath)
        => !string.IsNullOrEmpty(exePath)
            && string.Equals(command?.Trim(), Build(exePath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 注册表里的值指向的程序是不是 <paramref name="exePath"/>。
    /// 旧版本（以及安装器的可选任务）写的是不带参数的裸路径，靠它判断「这条自启项确实是当前程序留下的」——
    /// 只有这种条目才敢原地补参数；否则会把用户随手运行的另一个副本写成开机启动项。
    /// </summary>
    public static bool PointsAt(string? command, string? exePath)
        => !string.IsNullOrEmpty(exePath)
            && ExecutableOf(command) is string path
            && string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>取命令行里的 exe 路径：带引号的取第一对引号之间的内容，没引号的取第一个空格之前的部分。</summary>
    public static string? ExecutableOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        string text = command.Trim();
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            return end < 0 ? null : text[1..end];
        }

        int space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }
}
