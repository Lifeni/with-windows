namespace WithWindows.Core.App;

/// <summary>按热键切换窗口时应当执行的动作。</summary>
public enum WindowToggleAction
{
    /// <summary>显示窗口（隐藏状态下唤出）。</summary>
    Show,

    /// <summary>隐藏到托盘（窗口正在前台，再按一次就是收起）。</summary>
    Hide,

    /// <summary>仅前置并聚焦（窗口可见但被别的窗口挡住）。</summary>
    Activate,
}

/// <summary>
/// 「显示／隐藏窗口」热键的判定：按一次显示、再按一次隐藏。
/// 纯逻辑放在 Core 是为了能被单元测试覆盖——App 侧带 Microsoft.UI.Xaml，测不了这个分支。
/// </summary>
public static class WindowToggle
{
    /// <summary>
    /// 按窗口当前状态判定该做什么。
    /// 最小化必须按「显示」处理：那时 <c>IsWindowVisible</c> 仍是 true，但它既不在前台、也不是
    /// 「被别的窗口挡住」，只做前置的话用户按了热键窗口不会回来。
    /// </summary>
    public static WindowToggleAction Decide(bool isVisible, bool isActive, bool isMinimized)
        => !isVisible || isMinimized
            ? WindowToggleAction.Show
            : isActive
                ? WindowToggleAction.Hide
                : WindowToggleAction.Activate;
}
