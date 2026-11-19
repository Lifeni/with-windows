using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Samples;

/// <summary>示例内容初始化时写入的示例待办与 C# 脚本。</summary>
public static class SampleContent
{
    private const string EnvironmentScanScript =
        """
        Console.WriteLine($"计算机：{Environment.MachineName}");
        Console.WriteLine($"系统：{Environment.OSVersion}");
        Console.WriteLine($"64 位进程：{(Environment.Is64BitProcess ? "是" : "否")}");
        Console.WriteLine($"逻辑处理器：{Environment.ProcessorCount}");
        Console.WriteLine($"运行时：{Environment.Version}");
        Console.WriteLine($"时区：{TimeZoneInfo.Local.DisplayName}");
        Console.WriteLine("脚本以当前用户权限运行，可直接编辑为你的巡检命令。");
        """;

    private const string DailySummaryScript =
        """
        Console.WriteLine($"每日摘要 · {DateTime.Now:yyyy 年 M 月 d 日}");
        Console.WriteLine("先完成待办中最重要的一项，再查看今天的脚本运行日志。");
        Console.WriteLine("可在脚本编辑对话框中把这里换成自己的每日清单。");
        """;

    private const string WeeklyReleaseCheckScript =
        """
        Console.WriteLine($"每周发布检查 · {DateTime.Now:yyyy 年 M 月 d 日}");
        Console.WriteLine("发布前检查：");
        Console.WriteLine("1. 单元测试与界面自检通过");
        Console.WriteLine("2. 版本号和更新日志已同步");
        Console.WriteLine("3. 安装器与校验文件已生成");
        Console.WriteLine("可在脚本编辑对话框中改成自己的每周检查项。");
        """;

    /// <summary>创建示例待办标题；每次调用都返回新列表。</summary>
    public static IReadOnlyList<string> CreateTodoTitles() =>
    [
        "示例：勾选左侧复选框即可完成",
        "示例：点右侧铃铛设置一次性提醒",
        "示例：在下方输入框添加自己的待办",
    ];

    /// <summary>创建示例脚本；每次调用都返回带新计划对象的任务。</summary>
    public static IReadOnlyList<ScheduledTask> CreateTasks(DateTimeOffset now) =>
    [
        CreateTask(
            "示例：手动环境巡检",
            new TaskSchedule { Kind = ScheduleKind.Manual },
            EnvironmentScanScript,
            now),
        CreateTask(
            "示例：每日摘要",
            new TaskSchedule
            {
                Kind = ScheduleKind.Daily,
                TimeOfDay = new TimeOnly(9, 0),
            },
            DailySummaryScript,
            now),
        CreateTask(
            "示例：每周发布检查",
            new TaskSchedule
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeOnly(17, 30),
                DaysOfWeek = [DayOfWeek.Friday],
            },
            WeeklyReleaseCheckScript,
            now),
    ];

    private static ScheduledTask CreateTask(
        string name,
        TaskSchedule schedule,
        string script,
        DateTimeOffset now) => new()
        {
            Name = name,
            Schedule = schedule,
            ScriptText = script,
            TimeoutSeconds = 120,
            CreatedAt = now,
            UpdatedAt = now,
        };
}
