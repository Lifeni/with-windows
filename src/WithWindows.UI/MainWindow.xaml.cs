using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;
using WinUIEx;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    private readonly HotkeyManager _hotkeys = new();
    private readonly Logger _log;
    private TrayIcon? _tray; // 防 GC：托盘图标必须保持引用，否则会被回收
    private AutoThemeScheduler? _autoTheme;

    /// <summary>冒烟模式统计：热键注册失败数。</summary>
    public int RegisterFailures { get; private set; }

    public MainWindow(AppConfig config, Logger log)
    {
        _log = log;
        InitializeComponent();
        SetupTray();
        SetupHotkeys(config);
        Closed += (_, _) =>
        {
            _hotkeys.Dispose();
            _autoTheme?.Stop();
        };
    }

    private void SetupTray()
    {
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        _tray = new TrayIcon(1, iconPath, "With Windows");
        _tray.ContextMenu += (_, e) => e.Flyout = BuildMenu();
        _tray.LeftDoubleClick += (_, _) => ShowWindow();
        _tray.IsVisible = true;
    }

    private MenuFlyout BuildMenu()
    {
        var show = new MenuFlyoutItem { Text = "显示窗口" };
        show.Click += (_, _) => ShowWindow();

        var exit = new MenuFlyoutItem { Text = "退出" };
        exit.Click += (_, _) => Application.Current.Exit();

        var menu = new MenuFlyout();
        menu.Items.Add(show);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void ShowWindow()
    {
        this.Show();
        this.Activate();
    }

    private void SetupHotkeys(AppConfig config)
    {
        var theme = new ThemeAction();
        var display = new DisplayModeAction(config.DisplayMode.Modes.ToArray());

        // 热键 → 动作分发
        foreach (var (action, hotkeyText) in config.Bindings)
        {
            if (!HotkeyParser.TryParse(hotkeyText, out var hotkey, out var parseError))
            {
                _log.Error($"热键解析失败: {action}（{hotkeyText}）: {parseError}");
                RegisterFailures++;
                continue;
            }

            if (!_hotkeys.Register(hotkey, () => ExecuteAction(action, theme, display), out var registerError))
            {
                _log.Error($"热键注册失败: {action}: {registerError}");
                RegisterFailures++;
            }
        }

        // 自动亮暗：注册表标志或配置开关为真则恢复调度
        _autoTheme = new AutoThemeScheduler(
            theme,
            AutoThemeSettings.FromConfig(config.Theme),
            _log,
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        if (AutoThemeScheduler.GetEnabledFlag() || config.Theme.Enabled)
        {
            string? error = _autoTheme.TryStart();
            if (error is not null)
                _log.Error($"自动亮暗切换启动失败: {error}");
        }
    }

    private void ExecuteAction(string action, ThemeAction theme, DisplayModeAction display)
    {
        try
        {
            ActionResult result = action switch
            {
                "theme" => theme.Execute("toggle"),
                "display_mode" => display.Execute("toggle"),
                "notepad" => new ActionResult(false, "记事本尚未实现"),
                _ => new ActionResult(false, $"未知动作 {action}"),
            };
            if (result.Changed)
                _log.Info($"[{action}] {result.Message}");
        }
        catch (Exception ex)
        {
            _log.Error($"[{action}] 执行失败: {ex}");
        }
    }
}
