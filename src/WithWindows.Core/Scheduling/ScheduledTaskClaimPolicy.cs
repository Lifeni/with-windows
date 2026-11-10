namespace WithWindows.Core.Scheduling;

public static class ScheduledTaskClaimPolicy
{
    public static IReadOnlyList<ScheduledTaskOccurrence> GetDueClaims(
        IEnumerable<ScheduledTask> tasks,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DateTimeOffset? notBefore = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(timeZone);

        var claims = new List<ScheduledTaskOccurrence>();
        var seenTaskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScheduledTask task in tasks)
        {
            if (task.Schedule.Kind == ScheduleKind.Manual
                || string.IsNullOrWhiteSpace(task.Id)
                || task.ScheduleRevision < 1
                || !seenTaskIds.Add(task.Id))
            {
                continue;
            }

            DateTimeOffset? latest = ScheduleCalculator.LatestAtOrBefore(
                task.Schedule,
                now,
                timeZone);
            if (latest is null)
                continue;

            if (notBefore is { } lowerBound
                && latest.Value < lowerBound)
            {
                continue;
            }

            if (task.CreatedAt != default
                && latest.Value < task.CreatedAt)
            {
                continue;
            }

            if (task.LastHandledScheduledFor is { } handled
                && latest.Value <= handled)
            {
                continue;
            }

            claims.Add(new ScheduledTaskOccurrence(
                task.Id,
                task.ScheduleRevision,
                latest.Value));
        }

        return claims;
    }
}
