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
        _notepad = new NotepadHost(dataRoot, log, configStore);

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
        menu.Items.Add(MenuItem("退出", () =>
        {
            App.IsExiting = true;
            Application.Current.Exit();
        }));
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

    /// <summary>设置窗口保存后的回调：热重载配置并返回热键注册失败说明。</summary>
    private IReadOnlyList<string> OnToggleSaved() => ReloadBindings(_configStore.Load());

    private void SetupHotkeys(AppConfig config)
    {
        _display = new DisplayModeAction(config.DisplayMode.Modes.ToArray());
        RegisterBindings(config);
    }

    /// <summary>
    /// 重新注册全部热键（设置保存后热重载，立即生效），并同步投屏候选模式；
    /// 返回注册/解析失败说明（空列表 = 全部成功），供设置窗口提示用户。
    /// </summary>
    public IReadOnlyList<string> ReloadBindings(AppConfig config)
    {
        _display = new DisplayModeAction(config.DisplayMode.Modes.ToArray()); // 投屏模式热更新
        _hotkeys.UnregisterAll();
        return RegisterBindings(config);
    }

    private IReadOnlyList<string> RegisterBindings(AppConfig config)
    {
        var failures = new List<string>();
        RegisterFailures = 0;
        foreach (var (action, hotkeyText) in config.Bindings)
        {
            if (string.IsNullOrWhiteSpace(hotkeyText)) continue; // 未绑定（可空）

            // 配置在运行期间被外部改动时的兜底：未知动作不注册，否则它会占住热键却什么都不做
            if (!AppConfig.KnownActions.Contains(action))
            {
                _log.Error($"未知动作已跳过: {action}（可用动作：notepad、display_mode）");
                failures.Add($"{action}（未知动作，可用：notepad、display_mode）");
                continue;
            }

            if (!HotkeyParser.TryParse(hotkeyText, out var hotkey, out var parseError))
            {
                _log.Error($"热键解析失败: {action}（{hotkeyText}）: {parseError}");
                failures.Add($"{ActionDisplayName(action)} {hotkeyText}（{parseError}）");
                RegisterFailures++;
                continue;
            }

            if (!_hotkeys.Register(hotkey, () => ExecuteAction(action), out var registerError))
            {
                _log.Error($"热键注册失败: {action}: {registerError}");
                failures.Add($"{ActionDisplayName(action)} {hotkeyText}（{registerError}）");
                RegisterFailures++;
            }
        }
        return failures;
    }

    private void ExecuteAction(string action)
    {
        try
        {
            ActionResult result;
            switch (action)
            {
                case "display_mode":
                    result = _display.Execute("toggle");
                    break;
                case "notepad":
                    result = _notepad.Toggle();
                    break;
                default:
                    // 未知动作必须留痕：以前这里返回 Changed=false，日志分支进不去，配置写错会完全无声
                    _log.Error($"[{action}] 未知动作（可用动作：notepad、display_mode）");
                    return;
            }
            if (result.Changed)
                _log.Info($"[{action}] {result.Message}");
        }
        catch (Exception ex)
        {
            _log.Error($"[{action}] 执行失败: {ex}");
        }
    }

    /// <summary>动作名 → 用户可读名称（设置窗口提示用）。</summary>
    private static string ActionDisplayName(string action) => action switch
    {
        "notepad" => "快捷记事",
        "display_mode" => "切换投屏",
        _ => action,
    };
}
