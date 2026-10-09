using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Core.Config;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Hotkeys;
using WithWindows.Services;

namespace WithWindows;

/// <summary>
/// 应用入口。持有主窗口引用，供全局访问（替代 WinUI 3 中不存在的 Window.Current）。
/// </summary>
public partial class App : Application
{
    /// <summary>主窗口引用；关闭时只是隐藏，不置空。</summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>应用宿主（配置、热键、日志）；页面通过它读写配置。启动失败时为 null。</summary>
    public static AppHost? Host { get; private set; }

    // 生命周期等于进程：必须以字段持有，禁止局部 using（否则第二个实例照样能起）
    private AppHost? _host;

    // 冒烟路径同样由字段持有：AGENTS.md 硬性约束禁止用局部 using 持有 Logger / 热键管理器
    private Logger? _smokeLog;
    private HotkeyManager? _smokeHotkeys;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            RunSmoke();
            return;
        }

        AppHostStartResult start = AppHost.Start();
        if (start.AlreadyRunning)
        {
            Exit();
            return;
        }

        // 必须在创建窗口之前赋值：页面的 Loaded 会读 App.Host（默认页记事本会立即读配置字号）
        _host = start.Host;
        Host = _host;

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (start.Host is null)
        {
            ShowStartupError((MainWindow)MainWindow, start.Error ?? "未知错误");
            return;
        }

        AppHost host = start.Host;
        host.ToolInvoker = toolId => new HotkeyRouter((MainWindow)MainWindow!, host.Config).Invoke(toolId);
    }

    /// <summary>启动失败：在当前窗口上弹对话框后退出。</summary>
    private static async void ShowStartupError(MainWindow window, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot,
                Title = "启动失败",
                Content = message,
                CloseButtonText = "退出",
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // 弹框本身失败（XamlRoot 尚不可用等）时不能把异常抛出 async void——那是未观测异常，会直接杀进程
            System.Diagnostics.Debug.WriteLine($"展示启动失败对话框失败：{ex.Message}");
        }

        Current.Exit();
    }

    /// <summary>
    /// 冒烟检查：加载配置并按配置注册热键，返回结果后退出；不抢单实例、不创建窗口。
    /// WinExe 无控制台，结果写入日志文件。
    /// </summary>
    private void RunSmoke()
    {
        _smokeLog = Logger.Open(AppPaths.DataRoot);
        Logger log = _smokeLog;
        var store = new ConfigStore(AppPaths.ConfigPath);

        AppConfig config;
        try
        {
            store.EnsureExists(log.Info);
            config = store.Load();
        }
        catch (Exception ex)
        {
            // 冒烟路径同样要兜住配置损坏，否则会以未处理异常退出、连日志都没有
            log.Error($"冒烟：配置加载失败——{ex.Message}");
            Current.Exit();
            return;
        }

        _smokeHotkeys = new HotkeyManager();
        HotkeyManager hotkeys = _smokeHotkeys;

        int bound = 0;
        foreach (ToolBinding tool in config.Tools)
        {
            if (!tool.Enabled || string.IsNullOrWhiteSpace(tool.Hotkey))
            {
                log.Info($"冒烟：{tool.Id} 未绑定热键");
                continue;
            }

            if (!HotkeyParser.TryParse(tool.Hotkey, out Hotkey hotkey, out string? parseError))
            {
                log.Error($"冒烟：{tool.Id} 解析失败——{parseError}");
                continue;
            }

            if (hotkeys.Register(hotkey, () => { }, out string? registerError))
            {
                bound++;
                log.Info($"冒烟：{tool.Id} 注册成功（{tool.Hotkey}）");
            }
            else
            {
                log.Error($"冒烟：{tool.Id} 注册失败——{registerError}");
            }
        }

        log.Info($"冒烟完成：配置 {config.Tools.Count} 个工具，成功注册 {bound} 个热键");
        Current.Exit();
    }
}
