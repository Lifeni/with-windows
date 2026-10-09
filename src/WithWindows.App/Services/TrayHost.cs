using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;
using WithWindows.Tools;

namespace WithWindows.Services;

/// <summary>
/// 托盘宿主：左键唤出主窗口，右键给出「记事本 / 快捷键 / 设置 / 退出」。
/// 实例必须由字段持有——被 GC 回收后图标会消失且没有任何报错。
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly TrayIcon _trayIcon;

    public TrayHost(MainWindow window)
    {
        _window = window;

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        // TrayIcon 的窗口标识是 uint（AppWindow.Id.Value 是 ulong）
        _trayIcon = new TrayIcon((uint)window.AppWindow.Id.Value, iconPath, "With Windows");
        // 显式置为可见：默认值不可依赖，且 Win11 会先把新图标收进溢出区
        _trayIcon.IsVisible = true;
        _trayIcon.Selected += (_, _) => window.ShowWindow();
        _trayIcon.ContextMenu += (_, e) => e.Flyout = BuildMenu();
    }

    /// <summary>更新悬停提示（最长 128 字符）。</summary>
    public void UpdateTooltip(string text) => _trayIcon.Tooltip = text;

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateItem("记事本", ToolCatalog.NotepadId));
        menu.Items.Add(CreateItem("快捷键", ToolCatalog.HotkeysId));
        menu.Items.Add(CreateItem("设置", "settings"));
        menu.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "退出" };
        AutomationId(exit, "TrayItemExit");
        exit.Click += (_, _) => _window.ExitApplication();
        menu.Items.Add(exit);

        return menu;
    }

    private MenuFlyoutItem CreateItem(string text, string routeTag)
    {
        var item = new MenuFlyoutItem { Text = text };
        AutomationId(item, "TrayItem" + char.ToUpperInvariant(routeTag[0]) + routeTag[1..]);
        item.Click += (_, _) => _window.ShowAndNavigate(routeTag);
        return item;
    }

    private static void AutomationId(MenuFlyoutItem item, string id)
        => Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);

    public void Dispose() => _trayIcon.Dispose();
}
