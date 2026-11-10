namespace WithWindows.Core.Scheduling;

public static class ScheduledTaskValidation
{
    public const int MinimumTimeoutSeconds = 5;
    public const int MaximumTimeoutSeconds = 86_400;

    public static bool IsValidTaskId(string? id)
        => Guid.TryParseExact(id, "N", out _);

    public static bool IsSafeRunId(string? id)
        => !string.IsNullOrWhiteSpace(id)
            && id.Length <= 128
            && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static bool Normalize(ScheduledTask task)
    {
        bool changed = false;
        string name = task.Name.Trim();
        if (!string.Equals(name, task.Name, StringComparison.Ordinal))
        {
            task.Name = name;
            changed = true;
        }

        string script = task.ScriptText.ReplaceLineEndings("\n");
        if (!string.Equals(script, task.ScriptText, StringComparison.Ordinal))
        {
            task.ScriptText = script;
            changed = true;
        }

        if (task.ScheduleRevision < 1)
        {
            task.ScheduleRevision = 1;
            changed = true;
        }

        List<DayOfWeek> days = task.Schedule.DaysOfWeek.Distinct().ToList();
        if (days.Count != task.Schedule.DaysOfWeek.Count)
        {
            task.Schedule.DaysOfWeek = days;
            changed = true;
        }

        int timeout = Math.Clamp(
            task.TimeoutSeconds,
            MinimumTimeoutSeconds,
            MaximumTimeoutSeconds);
        if (timeout != task.TimeoutSeconds)
        {
            task.TimeoutSeconds = timeout;
            changed = true;
        }

        return changed;
    }

    public static void ValidateForSave(ScheduledTask task)
    {
        if (!IsValidTaskId(task.Id))
            throw new ArgumentException("任务 ID 必须是 32 位 GUID。", nameof(task));
        if (!Enum.IsDefined(task.Schedule.Kind))
            throw new ArgumentException("任务包含未知计划类型。", nameof(task));
        if (task.Schedule.DaysOfWeek.Any(day => !Enum.IsDefined(day)))
            throw new ArgumentException("任务包含未知星期。", nameof(task));
        if (string.IsNullOrWhiteSpace(task.Name))
            throw new ArgumentException("脚本名称不能为空。", nameof(task));
        if (task.Schedule.Kind == ScheduleKind.Once
            && task.Schedule.OneTimeRunAt is null)
        {
            throw new ArgumentException("单次任务必须设置执行时间。", nameof(task));
        }
        if (task.Schedule.Kind is ScheduleKind.Daily
            or ScheduleKind.Weekly
            or ScheduleKind.Monthly
            && task.Schedule.TimeOfDay is null)
        {
            throw new ArgumentException("定期任务必须设置执行时间。", nameof(task));
        }
        if (task.Schedule.Kind == ScheduleKind.Weekly
            && task.Schedule.DaysOfWeek.Count == 0)
        {
            throw new ArgumentException("每周任务至少选择一天。", nameof(task));
        }
        if (task.Schedule.Kind == ScheduleKind.Monthly
            && task.Schedule.DayOfMonth is not (>= 1 and <= 31))
        {
            throw new ArgumentException("每月任务必须选择 1 至 31 日。", nameof(task));
        }
        if (string.IsNullOrWhiteSpace(task.ScriptText))
            throw new ArgumentException("Lua 脚本不能为空。", nameof(task));
    }

    public static bool ScheduleEquals(TaskSchedule left, TaskSchedule right)
        => left.Kind == right.Kind
            && left.OneTimeRunAt == right.OneTimeRunAt
            && left.TimeOfDay == right.TimeOfDay
            && left.DayOfMonth == right.DayOfMonth
            && left.DaysOfWeek.SequenceEqual(right.DaysOfWeek);
}
