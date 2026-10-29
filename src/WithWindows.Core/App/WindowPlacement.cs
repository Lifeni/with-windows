namespace WithWindows.Core.App;

/// <summary>
/// 窗口尺寸 / 位置的可用性判定。Windows 对最小化窗口报告 <c>(-32000, -32000)</c> 与极小尺寸，
/// 这类系统占位矩形不能当作「用户记住的窗口状态」写回配置。
/// </summary>
public static class WindowPlacement
{
    /// <summary>最小化时系统返回的哨兵坐标；小于等于它的位置一律视为占位值。</summary>
    public const int MinimizedSentinel = -30000;

    /// <summary>矩形是否可用于持久化：坐标不是最小化哨兵、宽高为正。</summary>
    public static bool IsUsable(int x, int y, int width, int height)
        => x > MinimizedSentinel
            && y > MinimizedSentinel
            && width > 0
            && height > 0;

    /// <summary>
    /// 只有成功恢复过窗口状态后，才允许把当前矩形写回配置。
    /// <c>--tray</c> 首次启动会保留窗口的初始未显示状态；若用户从未唤出窗口，当前矩形是默认值，不能覆盖已保存的位置和尺寸。
    /// </summary>
    public static bool CanPersist(bool stateRestored, int x, int y, int width, int height)
        => stateRestored && IsUsable(x, y, width, height);
}
