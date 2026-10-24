using System;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Core.App;
using WithWindows.Core.Config;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Hotkeys;
using WithWindows.Services;
using WithWindows.Tools;

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
    private TodoReminderHost? _todoReminders;
    private int _recoveryRestartCount;
    private bool _restarting;

    public App()
    {
        InitializeComponent();

        // 兜底记录托管异常（XAML 解析/绑定错误会经过这里），方便定位崩溃。
        // 标记 Handled 让进程活下去：这是常驻托盘的后台应用，一个按钮回调里的异常
        // 不该把整个应用带走（「清除格式 / 插入链接」两次崩溃就是这么发生的）。
        UnhandledException += (_, e) =>
        {
            _host?.Log.Error($"未处理异常：{e.Exception}");
            System.Diagnostics.Debug.WriteLine($"未处理异常：{e.Exception}");
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLineArgs = Environment.GetCommandLineArgs();
        if (commandLineArgs.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            RunSmoke();
            return;
        }

        SignalRestartReady(commandLineArgs);

        if (!WaitForPreviousProcess(commandLineArgs))
        {
            System.Diagnostics.Debug.WriteLine("等待旧进程退出失败，取消本次重启。");
            Current.Exit();
            return;
        }

        _recoveryRestartCount = RestartCommand.GetRestartCount(commandLineArgs);
        string? restartPageTag = RestartCommand.GetPageTag(commandLineArgs);
        bool trayLaunch = AutoStartCommand.IsTrayLaunch(commandLineArgs);

        // 手动再次启动 = 唤出已有实例；开机自启重复触发则安静退出，不擅自弹窗
        AppHostStartResult start = AppHost.Start(activateExisting: !trayLaunch);
        if (start.AlreadyRunning)
        {
            Exit();
            return;
        }

        // 必须在创建窗口之前赋值：页面的 Loaded 会读 App.Host（默认页记事本会立即读配置字号）
        _host = start.Host;
        Host = _host;

        MainWindow = new MainWindow(start.Host is null ? null : restartPageTag);
        var mainWindow = (MainWindow)MainWindow;

        if (start.Host is null)
        {
            // 启动失败必须让窗口可见，否则错误对话框藏在托盘后面没人看得到
            mainWindow.Activate();
            ShowStartupError(mainWindow, start.Error ?? "未知错误");
            return;
        }

        AppHost host = start.Host;
        host.ToolInvoker = toolId => new HotkeyRouter(mainWindow, host.Config).Invoke(toolId);

        _todoReminders = new TodoReminderHost(
            host,
            mainWindow.ShowTodoReminder);

        // 再次双击 exe / 点开始菜单：已有实例把窗口叫出来，保留当前页面。
        host.ActivationRequested = () => mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (host.IsShuttingDown)
                return;

            host.Log.Info("再次启动：唤出主窗口");
            mainWindow.ShowCurrentPage();
        });
        host.StartActivationListener();

        // 开机自启项补上隐藏启动参数（旧版本 / 安装器写的是裸路径）
        try
        {
            if (AutoStart.EnsureUpToDate())
                host.Log.Info("开机自启项已更新为隐藏启动（补上了 --tray）");
        }
        catch (Exception ex)
        {
            // 注册表被组策略 / ACL 挡住时只记日志，不能拦着窗口启动
            host.Log.Error($"开机自启项升级失败（不影响启动）：{ex.Message}");
        }

        // 开机自启带 --tray：只驻留托盘，不打开主窗口——用户点托盘图标或按热键再唤出
        if (trayLaunch && mainWindow.IsTrayAvailable)
        {
            mainWindow.StartHidden();
            host.Log.Info("以 --tray 启动：隐藏到托盘，不打开主窗口");
        }
        else
        {
            if (trayLaunch)
                host.Log.Error("托盘不可用，--tray 启动回退为显示主窗口");
            mainWindow.Activate();
        }

        _todoReminders.Start();

        if (restartPageTag is { } pageTag)
        {
            mainWindow.DispatcherQueue.TryEnqueue(
                () => mainWindow.ShowAndNavigate(pageTag));
        }
    }

    private static bool WaitForPreviousProcess(IEnumerable<string> args)
    {
        if (RestartCommand.GetWaitForProcessId(args) is not int processId
            || processId == Environment.ProcessId)
        {
            return true;
        }

        try
        {
            using System.Diagnostics.Process process =
                System.Diagnostics.Process.GetProcessById(processId);
            if (!process.WaitForExit(10_000))
            {
                // 旧进程已经进入受控退出流程；不能因为固定超时先结束接管进程，
                // 否则旧进程稍后退出时会留下没有任何实例在运行的状态。
                System.Diagnostics.Debug.WriteLine(
                    $"旧进程 {processId} 超过 10 秒仍在退出，继续等待。");
                process.WaitForExit();
            }

            return true;
        }
        catch (ArgumentException)
        {
            // 旧进程已退出时 GetProcessById 会抛 ArgumentException。
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"等待旧进程退出失败：{ex.Message}");
            return false;
        }
    }

    private static void SignalRestartReady(IEnumerable<string> args)
    {
        if (RestartCommand.GetWaitForProcessId(args) is not int previousProcessId)
            return;

        try
        {
            using EventWaitHandle ready = EventWaitHandle.OpenExisting(
                RestartCommand.GetReadyEventName(previousProcessId));
            ready.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 旧进程已关闭交接事件时继续正常启动，不把辅助通知当成启动依赖。
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"通知旧进程启动握手失败：{ex.Message}");
        }
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

    /// <summary>真正退出前停止提醒计时器并解除仓储订阅。</summary>
    public static void StopTodoReminders()
    {
        if (Current is App app)
            app._todoReminders?.Dispose();
    }

    /// <summary>启动负责接管的新进程；成功后才允许当前进程释放资源并退出。</summary>
    public static bool TryStartApplicationRestart(string pageTag)
    {
        return Current is App app && app.TryStartApplicationRestartCore(pageTag);
    }

    /// <summary>页面对话框与托盘已释放后，结束当前失效进程。</summary>
    public static void ExitForApplicationRestart()
    {
        if (Current is App app)
            app.ExitForApplicationRestartCore();
    }

    /// <summary>页面已稳定挂载，下一次独立失效可从新的自动重启预算开始。</summary>
    public static void MarkPageReady()
    {
        if (Current is App app)
            app._recoveryRestartCount = 0;
    }

    private bool TryStartApplicationRestartCore(string pageTag)
    {
        if (_restarting)
            return false;

        if (!WindowRecoveryPolicy.CanRestartRecovery(_recoveryRestartCount))
        {
            _host?.Log.Error("页面仍未恢复挂载，已达到自动重启上限。");
            return false;
        }

        string? executable = Environment.ProcessPath;
        if (executable is null)
        {
            _host?.Log.Error("无法重启应用：找不到当前可执行文件路径。");
            return false;
        }

        string readyEventName = RestartCommand.GetReadyEventName(Environment.ProcessId);
        using var ready = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            readyEventName);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add($"--restart-after={Environment.ProcessId}");
        startInfo.ArgumentList.Add($"--restart-count={_recoveryRestartCount + 1}");
        startInfo.ArgumentList.Add($"--page={pageTag}");

        try
        {
            using System.Diagnostics.Process? process =
                System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                _host?.Log.Error("重启应用失败：系统未返回子进程。");
                return false;
            }

            // 子进程进入 WinUI 启动流程后会设置命名事件；确认握手完成再释放当前实例。
            if (!ready.WaitOne(5000) || process.HasExited)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        _host?.Log.Error($"终止未完成启动握手的子进程失败：{ex.Message}");
                    }
                }

                _host?.Log.Error("重启应用失败：新进程未完成启动握手。");
                return false;
            }

            _restarting = true;
            _host?.Log.Info($"页面失效，正在重启应用并恢复：{pageTag}");
            return true;
        }
        catch (Exception ex)
        {
            _host?.Log.Error($"重启应用失败：{ex.Message}");
            return false;
        }
    }

    private void ExitForApplicationRestartCore()
    {
        StopTodoReminders();
        _host?.Dispose();

        // 页面树已经失效时，Application.Exit() 仍会进入 XAML shutdown 并可能在
        // coreclr.dll 中崩溃。这里只结束进程；托盘、窗口状态和 AppHost 已先显式释放。
        Environment.Exit(0);
    }

}
