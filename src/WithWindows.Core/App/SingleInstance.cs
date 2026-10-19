using Windows.Win32;

namespace WithWindows.Core.App;

/// <summary>
/// 单实例守卫：命名 Mutex 保证只有一个常驻实例；第二个实例通过命名事件请已有实例唤出窗口后退出，
/// 避免日志文件互斥崩溃与热键重复注册。仅常驻模式使用。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string DefaultMutexName = @"Local\WithWindows.SingleInstance";
    private const string DefaultActivationEventName = @"Local\WithWindows.Activate";
    private const uint AnyProcessId = 0xFFFFFFFF;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;

    /// <summary>当前进程是否持有互斥体（true = 唯一实例）。</summary>
    public bool Owned { get; private set; }

    public SingleInstance()
        : this(DefaultMutexName, DefaultActivationEventName)
    {
    }

    /// <summary>测试用：允许注入唯一的互斥体 / 事件名，避免碰到真实运行中的实例。</summary>
    public SingleInstance(string mutexName, string activationEventName)
    {
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
        Owned = createdNew;
        if (!createdNew)
        {
            try
            {
                // 已存在互斥体：尝试立即获得，拿不到说明另一实例正在运行
                Owned = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                Owned = true; // 前实例异常退出未释放，接管
            }
        }
    }

    /// <summary>请求已有实例唤出主窗口；只应由未持有互斥体的第二个实例调用。</summary>
    public void SignalActivation()
    {
        if (Owned)
            return;

        // 第二个实例通常带着用户点击带来的「可设置前台」权限；这个权限不会随命名事件转交，
        // 先放行任意进程设置前台，已有实例的 SetForegroundWindow 才能把窗口真正提到最前。
        PInvoke.AllowSetForegroundWindow(AnyProcessId);
        _activationEvent.Set();
    }

    /// <summary>等待「再次启动」信号；返回 false 表示等待超时或宿主正在退出。</summary>
    public bool WaitForActivation(int millisecondsTimeout)
    {
        try
        {
            return _activationEvent.WaitOne(millisecondsTimeout);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>唤醒等待中的监听线程（退出时用）；不改变互斥体持有状态。</summary>
    public void WakeActivationWaiter() => _activationEvent.Set();

    /// <summary>主动释放互斥体（重启前调用，让新实例能取得持有权）。幂等。</summary>
    public void Release()
    {
        if (Owned)
        {
            _mutex.ReleaseMutex();
            Owned = false;
        }
    }

    public void Dispose()
    {
        Release();
        _mutex.Dispose();
        _activationEvent.Dispose();
    }
}
