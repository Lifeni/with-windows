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
    private readonly ConfigStore _configStore;
    private TrayIcon? _tray; // 防 GC：托盘图标必须保持引用，否则会被回收
    private ToggleWindow? _toggleWindow;
    private DisplayModeAction _display = null!;

    /// <summary>冒烟模式统计：热键注册失败数。</summary>
    public int RegisterFailures { get; private set; }

    public MainWindow(AppConfig config, Logger log, ConfigStore configStore, bool withTray = true)
    {
        _log = log;
        _configStore = configStore;
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WithWindows");
        _notepad = new NotepadHost(dataRoot, log, configStore, ShowToggleWindow);

        InitializeComponent();
        if (withTray)
            SetupTray();
        SetupHotkeys(config);
        Closed += (_, _) => _hotkeys.Dispose();
    }

    private void SetupTray()
    {
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        _tray = new TrayIcon(1, iconPath, "With Windows");
        _tray.ContextMenu += (_, e) => e.Flyout = BuildMenu();
        _tray.Selected += (_, _) => _notepad.ShowOrFocus(); // 左键单击打开记事本（双击重复触发无副作用）
        _tray.IsVisible = true;
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("快捷记事", () => _notepad.Toggle()));
        menu.Items.Add(MenuItem("切换投屏", () => ExecuteAction("display_mode")));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("设置", ShowToggleWindow));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(MenuItem("退出", () => Application.Current.Exit()));
        return menu;
    }

    /// <summary>紧凑菜单项（缩小行高）。</summary>
    private static MenuFlyoutItem MenuItem(string text, Action handler)
    {
        var item = new MenuFlyoutItem { Text = text, MinHeight = 30 };
        item.Click += (_, _) => handler();
        return item;
    }

    private void ShowToggleWindow()
    {
        if (_toggleWindow is null)
        {
            var window = new ToggleWindow(_configStore, OnToggleSaved, _log);
            window.Closed += (_, _) => _toggleWindow = null; // 用户关闭窗口后下次重建
            _toggleWindow = window;
        }
        _toggleWindow.ShowAndFocus();
    }

    private void OnToggleSaved()
    {
        ReloadBindings(_configStore.Load());
    }

    private void SetupHotkeys(AppConfig config)
    {
        _display = new DisplayModeAction(config.DisplayMode.Modes.ToArray());
        RegisterBindings(config);
    }

    /// <summary>重新注册全部热键（设置保存后热重载，立即生效）。</summary>
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
