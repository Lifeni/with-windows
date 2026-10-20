using WithWindows.Core.App;
using WithWindows.Core.Config;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Hotkeys;

namespace WithWindows.Services;

/// <summary>启动结果：要么拿到宿主，要么说明是重复实例，要么给出失败原因。</summary>
/// <param name="Host">启动成功时的宿主；否则为 null。</param>
/// <param name="AlreadyRunning">已有实例在运行（应静默退出）。</param>
/// <param name="Error">启动失败原因；无失败时为 null。</param>
public sealed record AppHostStartResult(AppHost? Host, bool AlreadyRunning, string? Error);

/// <summary>
/// 应用宿主：持有单实例守卫、日志、配置与热键管理器（生命周期等于进程）。
/// 由 <c>App</c> 以实例字段持有到退出；禁止写成局部 using。
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly SingleInstance _singleInstance;
    private Thread? _activationThread;
    private volatile bool _shuttingDown;

    private AppHost(SingleInstance singleInstance, Logger log, ConfigStore store, AppConfig config)
    {
        _singleInstance = singleInstance;
        Log = log;
        Store = store;
        Config = config;
        Hotkeys = new HotkeyManager();
    }

    public Logger Log { get; }
    public ConfigStore Store { get; }
    public AppConfig Config { get; private set; }
    public HotkeyManager Hotkeys { get; }
    public HttpClient HttpClient { get; } = new(
        new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    /// <summary>触发工具时的回调（由 App 在创建主窗口后注入）。未注入时热键只记日志。</summary>
    public Action<string>? ToolInvoker { get; set; }

    /// <summary>已有实例收到「再次启动」请求时要执行的动作（由 App 在创建主窗口后注入）。</summary>
    public Action? ActivationRequested { get; set; }

    /// <summary>宿主是否正在退出；UI 线程上的延迟回调据此避免访问已释放的资源。</summary>
    public bool IsShuttingDown => _shuttingDown;

    /// <summary>
    /// 启动宿主：抢单实例 → 打开日志 → 加载配置 → 注册热键。
    /// </summary>
    /// <param name="activateExisting">
    /// 已有实例在运行时是否为它发送「唤出窗口」信号。只有用户手动启动才发送；
    /// 开机自启重复触发（<c>--tray</c>）应安静退出，不能把用户之后的窗口擅自弹出来。
    /// </param>
    public static AppHostStartResult Start(bool activateExisting = true)
    {
        var single = new SingleInstance();
        if (!single.Owned)
        {
            if (activateExisting)
                single.SignalActivation();
            single.Dispose();
            return new AppHostStartResult(null, AlreadyRunning: true, Error: null);
        }

        Logger log = Logger.Open(AppPaths.DataRoot);
        var store = new ConfigStore(AppPaths.ConfigPath);
        AppConfig config;
        try
        {
            store.EnsureExists(log.Info);
            config = store.Load();
        }
        catch (Exception ex)
        {
            log.Error($"配置加载失败：{ex.Message}");
            single.Dispose();
            log.Dispose();
            return new AppHostStartResult(null, AlreadyRunning: false, Error: $"无法读取配置文件：{ex.Message}");
        }

        var host = new AppHost(single, log, store, config);
        foreach (string failure in host.ReloadBindings())
            host.Log.Error(failure);
        host.Log.Info("应用已启动");
        return new AppHostStartResult(host, AlreadyRunning: false, Error: null);
    }

    /// <summary>
    /// 启动「再次启动应用 → 唤出主窗口」的监听线程。必须在注入 <see cref="ActivationRequested"/> 后调用；
    /// 监听线程由字段持有到进程退出（与 Logger / 单实例互斥体同一条生命周期约束）。
    /// </summary>
    public void StartActivationListener()
    {
        if (_activationThread is not null)
            return;

        _activationThread = new Thread(ActivationLoop)
        {
            IsBackground = true,
            Name = "WithWindows.Activation",
        };
        _activationThread.Start();
    }

    private void ActivationLoop()
    {
        while (!_shuttingDown)
        {
            // 超时返回 false 只代表「这一轮没人启动」，不能当成退出条件——否则线程 500 ms 后就没了
            if (!_singleInstance.WaitForActivation(500))
                continue;
            if (_shuttingDown)
                break;

            try
            {
                ActivationRequested?.Invoke();
            }
            catch (Exception ex)
            {
                if (!_shuttingDown)
                    Log.Error($"处理再次启动请求失败：{ex.Message}");
            }
        }
    }

    /// <summary>注销并按配置重注册全部热键；返回每条失败原因（供快捷键页展示）。</summary>
    public IReadOnlyList<string> ReloadBindings()
    {
        Hotkeys.UnregisterAll();

        var failures = new List<string>();
        foreach (ToolBinding tool in Config.Tools)
        {
            if (!tool.Enabled || string.IsNullOrWhiteSpace(tool.Hotkey))
                continue;

            if (!HotkeyParser.TryParse(tool.Hotkey, out Hotkey hotkey, out string? parseError))
            {
                failures.Add($"{tool.Id}：{parseError}");
                continue;
            }

            string toolId = tool.Id;
            if (!Hotkeys.Register(hotkey, () => OnHotkey(toolId), out string? registerError))
                failures.Add($"{tool.Id}：{registerError}");
        }

        if (failures.Count > 0)
            Log.Error($"热键注册存在失败：{string.Join("；", failures)}");
        return failures;
    }

    private void OnHotkey(string toolId)
    {
        try
        {
            ToolInvoker?.Invoke(toolId);
        }
        catch (Exception ex)
        {
            Log.Error($"工具“{toolId}”执行失败：{ex.Message}");
        }
    }

    /// <summary>保存配置到磁盘；失败只记日志，不打断界面操作。</summary>
    public void SaveConfig()
    {
        try
        {
            Store.Save(Config);
        }
        catch (Exception ex)
        {
            Log.Error($"配置保存失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        _shuttingDown = true;

        // 先停监听线程：它可能正在回调或写日志；唤醒等待避免 join 空等 500 ms
        if (_activationThread is not null)
        {
            _singleInstance.WakeActivationWaiter();
            _activationThread.Join(TimeSpan.FromSeconds(1));
            _activationThread = null;
        }

        Log.Info("应用退出");
        HttpClient.Dispose();
        Hotkeys.Dispose();
        // 先关日志再放互斥体：否则新实例可能抢到互斥体，却因 log.txt 仍被占用而静默丢掉全部日志
        Log.Dispose();
        _singleInstance.Dispose();
    }
}
