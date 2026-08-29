using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Core;
using WinUIEx;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    private readonly HotkeyManager _hotkeys = new();
    private TrayIcon? _tray; // 防 GC：托盘图标必须保持引用，否则会被回收
    private int _triggers;

    public MainWindow()
    {
        InitializeComponent();
        SetupTray();
        SetupHotkeys();
        Closed += (_, _) => _hotkeys.Dispose();
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

    private void SetupHotkeys()
    {
        // 临时验证热键：Ctrl+Shift+F13 计数并更新标题；正式绑定 Phase 3 接入配置
        _hotkeys.Register(HotkeyParser.Parse("Ctrl+Shift+F13"),
            () =>
            {
                _triggers++;
                Title = $"With Windows（热键触发 {_triggers} 次）";
            },
            out _);
    }
}
