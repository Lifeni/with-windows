using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Notepad;
using WinUIEx;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    private readonly HotkeyManager _hotkeys = new();
    private readonly Logger _log;
    private readonly NotepadHost _notepad;
    private TrayIcon? _tray; // 防 GC：托盘图标必须保持引用，否则会被回收
    private AutoThemeScheduler? _autoTheme;
    private ThemeAction _theme = null!;
    private DisplayModeAction _display = null!;

    /// <summary>冒烟模式统计：热键注册失败数。</summary>
    public int RegisterFailures { get; private set; }

    public MainWindow(AppConfig config, Logger log)
    {
        _log = log;
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WithWindows");
        _notepad = new NotepadHost(dataRoot, log);

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
        var notepad = new MenuFlyoutItem { Text = "记事本" };
        notepad.Click += (_, _) => _notepad.Toggle();

        var show = new MenuFlyoutItem { Text = "显示窗口" };
        show.Click += (_, _) => ShowWindow();

        var exit = new MenuFlyoutItem { Text = "退出" };
        exit.Click += (_, _) => Application.Current.Exit();

        var menu = new MenuFlyout();
        menu.Items.Add(notepad);
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
        _theme = new ThemeAction();
        _display = new DisplayModeAction(config.DisplayMode.Modes.ToArray());
        _autoTheme = new AutoThemeScheduler(
            _theme,
            AutoThemeSettings.FromConfig(config.Theme),
            _log,
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        RegisterBindings(config);

        // 自动亮暗：注册表标志或配置开关为真则恢复调度
        if (AutoThemeScheduler.GetEnabledFlag() || config.Theme.Enabled)
        {
            string? error = _autoTheme.TryStart();
            if (error is not null)
                _log.Error($"自动亮暗切换启动失败: {error}");
        }
    }

    /// <summary>重新注册全部热键绑定（设置保存后热重载：先注销再注册）。</summary>
    public void ReloadBindings(AppConfig config)
    {
        _hotkeys.UnregisterAll();
        RegisterBindings(config);
    }

    private void RegisterBindings(AppConfig config)
    {
        RegisterFailures = 0;
        foreach (var (action, hotkeyText) in config.Bindings)
        {
            if (string.IsNullOrWhiteSpace(hotkeyText)) continue; // 未绑定（可空）

            if (!HotkeyParser.TryParse(hotkeyText, out var hotkey, out var parseError))
            {
                _log.Error($"热键解析失败: {action}（{hotkeyText}）: {parseError}");
                RegisterFailures++;
                continue;
            }

            if (!_hotkeys.Register(hotkey, () => ExecuteAction(action), out var registerError))
            {
                _log.Error($"热键注册失败: {action}: {registerError}");
                RegisterFailures++;
            }
        }
    }

    private void ExecuteAction(string action)
    {
        try
        {
            ActionResult result = action switch
            {
                "theme" => _theme.Execute("toggle"),
                "display_mode" => _display.Execute("toggle"),
                "notepad" => _notepad.Toggle(),
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
