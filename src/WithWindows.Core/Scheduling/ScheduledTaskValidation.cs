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

        if (task.ScheduleRevision < 1)
        {
            task.ScheduleRevision = 1;
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

        List<DayOfWeek> days = task.Schedule.DaysOfWeek.Distinct().ToList();
        if (days.Count != task.Schedule.DaysOfWeek.Count)
        {
            task.Schedule.DaysOfWeek = days;
            changed = true;
        }

        return changed;
    }

    public static void ValidateForSave(ScheduledTask task)
    {
        if (!IsValidTaskId(task.Id))
            throw new ArgumentException("任务 ID 必须是 32 位 GUID。", nameof(task));
        if (!Enum.IsDefined(task.Schedule.Kind)
            || !Enum.IsDefined(task.Source.Kind)
            || !Enum.IsDefined(task.PythonSource)
            || task.Schedule.DaysOfWeek.Any(day => !Enum.IsDefined(day)))
        {
            throw new ArgumentException("任务包含未知枚举值。", nameof(task));
        }

        if (string.IsNullOrWhiteSpace(task.Name))
            throw new ArgumentException("任务名称不能为空。", nameof(task));

        if (task.Schedule.Kind == ScheduleKind.Once && task.Schedule.OneTimeRunAt is null)
            throw new ArgumentException("一次性任务必须设置执行时间。", nameof(task));

        if (task.Schedule.Kind is ScheduleKind.Daily or ScheduleKind.Weekly
            && task.Schedule.TimeOfDay is null)
        {
            throw new ArgumentException("每天或每周任务必须设置执行时间。", nameof(task));
        }

        if (task.Schedule.Kind == ScheduleKind.Weekly
            && task.Schedule.DaysOfWeek.Count == 0)
        {
            throw new ArgumentException("每周任务至少选择一天。", nameof(task));
        }

        if (task.Source.Kind == ScriptSourceKind.Inline
            && string.IsNullOrWhiteSpace(task.Source.ScriptText))
        {
            throw new ArgumentException("粘贴脚本不能为空。", nameof(task));
        }

        if (task.Source.Kind == ScriptSourceKind.File
            && string.IsNullOrWhiteSpace(task.Source.ScriptPath))
        {
            throw new ArgumentException("必须选择 Python 文件。", nameof(task));
        }
    }

    public static bool ScheduleEquals(TaskSchedule left, TaskSchedule right)
        => left.Kind == right.Kind
            && left.OneTimeRunAt == right.OneTimeRunAt
            && left.TimeOfDay == right.TimeOfDay
            && left.DaysOfWeek.SequenceEqual(right.DaysOfWeek);
}
