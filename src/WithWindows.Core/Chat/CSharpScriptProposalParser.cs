using System.Globalization;
using System.Text.Json;
using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Chat;

/// <summary>解析并校验 AI 返回的 C# 脚本提案。</summary>
public static class CSharpScriptProposalParser
{
    public static ScheduledTask ParseTask(
        string arguments,
        DateTimeOffset now)
    {
        using JsonDocument document = JsonDocument.Parse(arguments);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("脚本提案必须是 JSON 对象。");

        string name = RequiredString(root, "name").Trim();
        string script = RequiredString(root, "script").ReplaceLineEndings("\n");
        if (name.Length > 80)
            throw new ArgumentException("脚本名称最多 80 个字符。");
        if (script.Length > 120_000)
            throw new ArgumentException("脚本正文过长。");

        int timeoutSeconds = ReadTimeout(root);
        TaskSchedule schedule = ParseSchedule(
            RequiredObject(root, "schedule"),
            now);
        var task = new ScheduledTask
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ScheduleRevision = 1,
            Schedule = schedule,
            ScriptText = script,
            TimeoutSeconds = timeoutSeconds,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ScheduledTaskValidation.Normalize(task);
        ScheduledTaskValidation.ValidateForSave(task);
        return task;
    }

    private static TaskSchedule ParseSchedule(
        JsonElement schedule,
        DateTimeOffset now)
    {
        string kind = RequiredString(schedule, "kind").Trim().ToLowerInvariant();
        ScheduleKind parsedKind = kind switch
        {
            "manual" => ScheduleKind.Manual,
            "once" => ScheduleKind.Once,
            "daily" => ScheduleKind.Daily,
            "weekly" => ScheduleKind.Weekly,
            "monthly" => ScheduleKind.Monthly,
            _ => throw new ArgumentException("脚本提案包含未知执行方式。"),
        };

        var result = new TaskSchedule
        {
            Kind = parsedKind,
        };
        if (parsedKind == ScheduleKind.Manual)
            return result;

        string timeText = OptionalString(schedule, "time")
            ?? throw new ArgumentException("脚本提案缺少执行时间。");
        if (!TimeOnly.TryParseExact(
                timeText,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly time))
        {
            throw new ArgumentException("执行时间必须使用 HH:mm 格式。");
        }

        if (parsedKind == ScheduleKind.Once)
        {
            string dateText = OptionalString(schedule, "date")
                ?? throw new ArgumentException("单次脚本提案缺少执行日期。");
            if (!DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly date))
            {
                throw new ArgumentException("执行日期必须使用 yyyy-MM-dd 格式。");
            }

            DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
            DateTimeOffset runAt = new(local, now.Offset);
            if (runAt <= now)
                throw new ArgumentException("单次执行时间必须晚于当前时间。");
            result.OneTimeRunAt = runAt;
            return result;
        }

        result.TimeOfDay = time;
        if (parsedKind == ScheduleKind.Weekly)
        {
            result.DaysOfWeek = ParseDays(schedule)
                .OrderBy(WeekdayOrder)
                .ToList();
        }
        else if (parsedKind == ScheduleKind.Monthly)
        {
            if (!schedule.TryGetProperty("dayOfMonth", out JsonElement day)
                || day.ValueKind != JsonValueKind.Number
                || !day.TryGetInt32(out int dayOfMonth))
            {
                throw new ArgumentException("每月脚本提案缺少执行日期。");
            }
            result.DayOfMonth = dayOfMonth;
        }

        return result;
    }

    private static List<DayOfWeek> ParseDays(JsonElement schedule)
    {
        if (!schedule.TryGetProperty("daysOfWeek", out JsonElement days)
            || days.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("每周脚本提案缺少星期。");
        }

        var result = new List<DayOfWeek>();
        foreach (JsonElement day in days.EnumerateArray())
        {
            if (day.ValueKind != JsonValueKind.String)
                throw new ArgumentException("星期值必须是字符串。");

            string value = day.GetString()!.Trim().ToLowerInvariant();
            DayOfWeek parsed = value switch
            {
                "monday" => DayOfWeek.Monday,
                "tuesday" => DayOfWeek.Tuesday,
                "wednesday" => DayOfWeek.Wednesday,
                "thursday" => DayOfWeek.Thursday,
                "friday" => DayOfWeek.Friday,
                "saturday" => DayOfWeek.Saturday,
                "sunday" => DayOfWeek.Sunday,
                _ => throw new ArgumentException("脚本提案包含未知星期。"),
            };
            if (!result.Contains(parsed))
                result.Add(parsed);
        }

        return result;
    }

    private static int ReadTimeout(JsonElement root)
    {
        if (!root.TryGetProperty("timeoutSeconds", out JsonElement timeout))
            return 600;
        if (timeout.ValueKind != JsonValueKind.Number
            || !timeout.TryGetInt32(out int seconds))
        {
            throw new ArgumentException("超时时间必须是整数。");
        }
        return Math.Clamp(
            seconds,
            ScheduledTaskValidation.MinimumTimeoutSeconds,
            ScheduledTaskValidation.MaximumTimeoutSeconds);
    }

    private static JsonElement RequiredObject(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"脚本提案缺少对象字段 {name}。");
        }
        return value;
    }

    private static string RequiredString(
        JsonElement parent,
        string name)
        => OptionalString(parent, name)
            ?? throw new ArgumentException($"脚本提案缺少字段 {name}。");

    private static string? OptionalString(
        JsonElement parent,
        string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int WeekdayOrder(DayOfWeek day)
        => day == DayOfWeek.Sunday ? 7 : (int)day;
}
