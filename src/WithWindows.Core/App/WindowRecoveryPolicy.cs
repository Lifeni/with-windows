namespace WithWindows.Core.App;

public enum WindowRecoveryStep
{
    Stop,
    Wait,
    Navigate,
    GiveUp,
}

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

    /// <summary>导航代次未变化时才执行已排队的页面重建，避免重复导航用户刚切到的页面。</summary>
    public static bool ShouldApplyQueuedRemount(int queuedGeneration, int currentGeneration)
    {
        return queuedGeneration == currentGeneration;
    }

    /// <summary>停用期间已完成的导航会刷新页面，因此不需要再做一次恢复导航。</summary>
    public static bool ShouldRemountAfterActivation(
        DateTimeOffset now,
        DateTimeOffset? deactivatedAt,
        TimeSpan idleThreshold,
        int navigationAtDeactivation,
        int currentNavigation)
    {
        return navigationAtDeactivation == currentNavigation
            && ShouldRemount(now, deactivatedAt, idleThreshold);
    }

    /// <summary>页面尚未挂载时，决定继续等待、重导航或停止有限恢复。</summary>
    public static WindowRecoveryStep DecideShowRecovery(
        bool pageLoaded,
        int remainingChecks,
        int remainingNavigationAttempts,
        bool hasPageType)
    {
        if (pageLoaded)
            return WindowRecoveryStep.Stop;
        if (remainingChecks > 0)
            return WindowRecoveryStep.Wait;
        if (!hasPageType || remainingNavigationAttempts <= 0)
            return WindowRecoveryStep.GiveUp;
        return WindowRecoveryStep.Navigate;
    }
}
