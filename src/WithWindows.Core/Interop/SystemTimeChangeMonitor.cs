namespace WithWindows.Core.Interop;

/// <summary>
/// 使用独立隐藏顶层窗口监听系统时间与时区变化，避免引入 SystemEvents NuGet 依赖。
/// 创建和释放都必须在其所属 UI 线程执行。
/// </summary>
public sealed class SystemTimeChangeMonitor : IDisposable
{
    private readonly MessageWindow _window;

    public SystemTimeChangeMonitor(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        _window = new MessageWindow(_ => { }, onChanged);
    }

    public void Dispose() => _window.Dispose();
}
