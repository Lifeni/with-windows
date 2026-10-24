namespace WithWindows.Core.App;

/// <summary>判断长时间停用后的窗口重新激活是否需要重建 XAML 页面。</summary>
public static class WindowRecoveryPolicy
{
    public static bool ShouldRemount(
        DateTimeOffset now,
        DateTimeOffset? deactivatedAt,
        TimeSpan idleThreshold)
    {
        if (deactivatedAt is null)
            return false;

        TimeSpan idle = now - deactivatedAt.Value;
        return idle >= idleThreshold;
    }
}
