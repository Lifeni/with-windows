using System;
using System.IO;
using System.Linq;
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
    private readonly TrayIcon? _trayIcon;

    public TrayHost(MainWindow window)
    {
        _window = window;

        // 图标文件缺失（打包漏了 Assets）或格式不被支持时不能让应用起不来：
        // 失败则标记不可用，窗口关闭逻辑会改为「真正退出」，避免变成没有入口的后台进程。
        try
        {
            // 图标文件不存在时要直接判不可用：WinUIEx 构造 TrayIcon 时不会因路径不存在而报错，
            // 结果是「窗口关闭后隐藏到托盘、但托盘上什么都没有」——应用会变成没有入口的后台进程。
            if (!AppIcon.Exists)
            {
                IsAvailable = false;
                FailureReason = $"缺少图标文件：{AppIcon.Preferred}";
                return;
            }

            // TrayIcon 的窗口标识是 uint（AppWindow.Id.Value 是 ulong），图标只支持 .ico / .svg
            _trayIcon = new TrayIcon((uint)window.AppWindow.Id.Value, AppIcon.Preferred, "With Windows");
            // 显式置为可见：默认值不可依赖，且 Win11 会先把新图标收进溢出区
            _trayIcon.IsVisible = true;
            _trayIcon.Selected += (_, _) => window.ShowWindow();
            _trayIcon.ContextMenu += (_, e) => e.Flyout = BuildMenu();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            FailureReason = ex.Message;
        }
    }

    /// <summary>托盘图标是否可用；false 表示没有唤回窗口的入口。</summary>
    public bool IsAvailable { get; }

    /// <summary>创建失败的原因（供日志排查）。</summary>
    public string? FailureReason { get; }

    /// <summary>更新悬停提示（最长 128 字符）。</summary>
    public void UpdateTooltip(string text)
    {
        if (_trayIcon is not null)
            _trayIcon.Tooltip = text;
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        // 标题统一取自工具目录，避免改名后左栏与托盘叫法不一致
        foreach (ToolEntry tool in ToolCatalog.Tools.Where(t => t.ShowInNav))
            menu.Items.Add(CreateItem(tool.Title, tool.Id));

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

    public void Dispose() => _trayIcon?.Dispose();
}
