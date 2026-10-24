namespace WithWindows.Core.App;

public enum WindowRecoveryStep
{
    Stop,
    Wait,
    Restart,
    GiveUp,
}

/// <summary>判断长时间停用后的窗口重新激活是否需要重建 XAML 页面。</summary>
public static class WindowRecoveryPolicy
{
    private const int MaximumAutomaticRestarts = 1;

    /// <summary>没有当前页时可进行首次导航；已有页面必须仍在可视化树中。</summary>
    public static bool CanNavigatePage(
        bool hasCurrentPage,
        bool pageLoaded,
        bool hasXamlRoot)
    {
        if (!hasCurrentPage)
            return !pageLoaded && !hasXamlRoot;

        return pageLoaded && hasXamlRoot;
    }

    /// <summary>自动恢复最多跨进程重启一次，避免页面持续失效时形成重启循环。</summary>
    public static bool CanRestartRecovery(int restartCount)
        => restartCount >= 0 && restartCount < MaximumAutomaticRestarts;

    /// <summary>恢复完成必须同时匹配目标页类型，并确认页面仍在可视化树中。</summary>
    public static bool IsExpectedPageReady(
        bool typeMatches,
        bool pageLoaded,
        bool hasXamlRoot)
        => typeMatches && pageLoaded && hasXamlRoot;

    /// <summary>页面尚未挂载时，决定继续等待、重启应用或停止恢复。</summary>
    public static WindowRecoveryStep DecideShowRecovery(
        bool pageLoaded,
        int remainingChecks,
        bool hasPageType)
    {
        if (pageLoaded)
            return WindowRecoveryStep.Stop;
        if (remainingChecks > 0)
            return WindowRecoveryStep.Wait;
        if (!hasPageType)
            return WindowRecoveryStep.GiveUp;
        return WindowRecoveryStep.Restart;
    }
}
