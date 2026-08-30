namespace WithWindows.Core;

/// <summary>
/// 单实例守卫：命名 Mutex 保证只有一个常驻实例。第二个实例（重复双击 exe 等）立即退出，
/// 避免日志文件互斥崩溃与热键重复注册。仅常驻模式使用；--smoke 不抢互斥体。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;

    /// <summary>当前进程是否持有互斥体（true = 唯一实例）。</summary>
    public bool Owned { get; private set; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, @"Local\WithWindows.SingleInstance", out bool createdNew);
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
    }
}
