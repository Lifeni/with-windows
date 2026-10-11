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

    /// <summary>触发工具时的回调（由 App 在创建主窗口后注入）。未注入时热键只记日志。</summary>
    public Action<string>? ToolInvoker { get; set; }

    /// <summary>启动宿主：抢单实例 → 打开日志 → 加载配置 → 注册热键。</summary>
    public static AppHostStartResult Start()
    {
        var single = new SingleInstance();
        if (!single.Owned)
        {
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
        Log.Info("应用退出");
        Hotkeys.Dispose();
        Log.Dispose();
        _singleInstance.Dispose();
    }
}
