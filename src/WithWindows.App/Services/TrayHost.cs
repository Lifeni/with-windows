using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;
using WithWindows.Core.Actions;
using WithWindows.Tools;
using WithWindows.Views;

namespace WithWindows.Services;

/// <summary>
/// 托盘宿主：左键唤出主窗口，右键给出「记事 / 待办 / 对话 / 切换模式 · 设置 · 退出」。
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

            // 图标 id 必须是 0：WinUIEx 2.9.3 用 HIWORD(lParam) 当图标 id 比对
            // （见 TrayIcon.ProcessTrayIconEvents），而外壳回调的 lParam 低位才是消息、
            // 高位通常是 0——传 AppWindow.Id（几百万的大数）时永远对不上，事件会被整个丢掉。
            const uint iconId = 0;
            _trayIcon = new TrayIcon(iconId, AppIcon.Preferred, "With Windows");
            // 显式置为可见：默认值不可依赖，且 Win11 会先把新图标收进溢出区
            _trayIcon.IsVisible = true;
            _trayIcon.Selected += (_, _) =>
            {
                App.Host?.Log.Info("托盘：左键");
                window.ShowWindow();
            };
            _trayIcon.ContextMenu += (_, e) =>
            {
                App.Host?.Log.Info("托盘：右键菜单");
                try
                {
                    e.Flyout = BuildMenu();
                }
                catch (Exception ex)
                {
                    // 构建失败不能让异常穿回 WinUIEx 的回调里（那会变成未处理异常）
                    App.Host?.Log.Error($"托盘菜单构建失败：{ex.Message}");
                }
            };
            IsAvailable = true;
            App.Host?.Log.Info($"托盘图标已创建：id={iconId}，图标={AppIcon.Preferred}");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            FailureReason = ex.Message;
            App.Host?.Log.Error($"托盘图标创建失败：{ex.Message}");
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

    /// <summary>
    /// 菜单选项：记事 / 待办 / 对话 / 切换模式 · 设置 · 退出。
    /// 页面入口与左栏保持一致；操作项每次打开菜单时显示实际目标。
    /// </summary>
    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        menu.Items.Add(NavigateItem(TitleOf(ToolCatalog.NotepadId), ToolCatalog.NotepadId, "TrayItemNotepad"));
        menu.Items.Add(NavigateItem(TitleOf(ToolCatalog.TodoId), ToolCatalog.TodoId, "TrayItemTodo"));
        menu.Items.Add(NavigateItem("对话", ToolCatalog.AiChatId, "TrayItemAiChat"));

        var displayMode = new DisplayModeAction(
            App.Host?.Config.DisplayMode.Modes.ToArray() ?? Array.Empty<string>());
        menu.Items.Add(ActionItem(
            displayMode.GetToggleMenuText(),
            ToolCatalog.DisplayModeId,
            "TrayItemSwitchDisplay"));

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(NavigateItem("设置", SettingsPage.RouteTag, "TrayItemSettings"));
        menu.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "退出" };
        AutomationId(exit, "TrayItemExit");
        exit.Click += (_, _) => _window.ExitApplication();
        menu.Items.Add(exit);

        return menu;
    }

    private static string TitleOf(string toolId)
        => ToolCatalog.Tools.FirstOrDefault(t => t.Id == toolId)?.Title ?? toolId;

    private MenuFlyoutItem NavigateItem(string text, string routeTag, string automationId)
    {
        var item = new MenuFlyoutItem { Text = text };
        AutomationId(item, automationId);
        item.Click += (_, _) => _window.ShowAndNavigate(routeTag);
        return item;
    }

    private MenuFlyoutItem ActionItem(string text, string toolId, string automationId)
    {
        var item = new MenuFlyoutItem { Text = text };
        AutomationId(item, automationId);
        item.Click += (_, _) => _window.RunTrayAction(toolId);
        return item;
    }

    private static void AutomationId(MenuFlyoutItem item, string id)
        => Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, id);

    public void Dispose() => _trayIcon?.Dispose();
}
