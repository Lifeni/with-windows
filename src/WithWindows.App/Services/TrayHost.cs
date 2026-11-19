using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Core.Interop;
using WithWindows.Core.Todos;
using WithWindows.Tools;
using WithWindows.Views;

namespace WithWindows.Services;

/// <summary>
/// 托盘宿主：左键唤出主窗口，右键给出「记事 / 待办 / 对话 / 脚本 / 切换 · 设置 · 退出」。
/// 实例必须由字段持有——被 GC 回收后图标会消失且没有任何报错。
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly MainWindow _window;
    private readonly TrayIcon? _trayIcon;
    private readonly TrayNotificationService? _notifications;
    private string? _currentIconPath;

    public TrayHost(MainWindow window, bool darkTheme)
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
            string iconPath = AppIcon.ResolveTray(
                App.Host?.Config.Tray.IconStyle ?? TrayIconStyle.LightningThin,
                darkTheme);
            _trayIcon = new TrayIcon(iconId, iconPath, "With Windows");
            _currentIconPath = iconPath;
            _notifications = new TrayNotificationService(iconId);
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
            App.Host?.Log.Info($"托盘图标已创建：id={iconId}，图标={iconPath}");
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

    /// <summary>配置或系统主题变化后替换托盘图标；相同路径不重复调用 Shell。</summary>
    public void ApplyIconStyle(string iconStyle, bool darkTheme)
    {
        if (_trayIcon is null)
            return;

        string iconPath = AppIcon.ResolveTray(iconStyle, darkTheme);
        if (string.Equals(_currentIconPath, iconPath, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            _trayIcon.SetIcon(iconPath);
            _currentIconPath = iconPath;
            App.Host?.Log.Info($"托盘图标已切换：{iconPath}");
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"切换托盘图标失败：{ex.Message}");
        }
    }

    /// <summary>更新悬停提示（最长 128 字符）。</summary>
    public void UpdateTooltip(string text)
    {
        if (_trayIcon is not null)
            _trayIcon.Tooltip = text;
    }

    /// <summary>通过现有托盘图标显示 Windows 原生通知横幅。</summary>
    public bool ShowTodoNotification(IReadOnlyList<string> titles)
    {
        if (_notifications is null)
            return false;

        try
        {
            return _notifications.Show(
                TodoReminderNotificationFormatter.Title,
                TodoReminderNotificationFormatter.FormatBody(titles));
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"显示系统通知失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 菜单选项：记事 / 待办 / 对话 / 脚本 / 切换 · 设置 · 退出。
    /// 「切换」下只放显示模式与亮暗主题两项，均按循环切换并显示下一次的四字目标。
    /// </summary>
    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        menu.Items.Add(NavigateItem(TitleOf(ToolCatalog.NotepadId), ToolCatalog.NotepadId, "TrayItemNotepad"));
        menu.Items.Add(NavigateItem(TitleOf(ToolCatalog.TodoId), ToolCatalog.TodoId, "TrayItemTodo"));
        menu.Items.Add(NavigateItem("对话", ToolCatalog.AiChatId, "TrayItemAiChat"));
        menu.Items.Add(NavigateItem(
            "脚本",
            ToolCatalog.ScheduledId,
            "TrayItemScheduled"));

        var switchMenu = new MenuFlyoutSubItem { Text = "切换" };
        AutomationProperties.SetAutomationId(switchMenu, "TraySwitchMenu");

        var displayMode = new DisplayModeAction(
            App.Host?.Config.DisplayMode.Modes.ToArray() ?? Array.Empty<string>());
        switchMenu.Items.Add(ActionItem(
            displayMode.GetToggleMenuText(),
            ToolCatalog.DisplayModeId,
            "TrayItemSwitchDisplay"));

        var themeSwitch = new ThemeToggleAction(
            App.Host?.Config.ThemeSwitch ?? new ThemeSwitchConfig());
        switchMenu.Items.Add(ActionItem(
            themeSwitch.GetToggleMenuText(),
            ToolCatalog.ThemeToggleId,
            "TrayItemSwitchTheme"));
        menu.Items.Add(switchMenu);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(NavigateItem("设置", SettingsPage.RouteTag, "TrayItemSettings"));
        menu.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "退出" };
        AutomationId(exit, "TrayItemExit");
        bool exitRequested = false;
        menu.Closed += (_, _) =>
        {
            if (!exitRequested)
                return;

            exitRequested = false;
            // 菜单关闭事件仍处于 WinUI 输入回调链中；退出排到下一轮 UI 队列，
            // 避免在销毁菜单所属托盘时重入 XAML / DirectManipulation。
            _window.RequestExitFromTray();
        };
        exit.Click += (_, _) =>
        {
            exitRequested = true;
            App.Host?.Log.Info("托盘：请求退出");
            try
            {
                // 点击菜单项通常会自动关闭；显式 Hide 用于覆盖 Automation 与
                // 弹窗未取得焦点等边界，确保 Closed 一定触发。
                menu.Hide();
            }
            catch (Exception ex)
            {
                // 即使隐藏失败也不能放弃退出请求：Closed 逻辑不依赖 Hide 成功。
                App.Host?.Log.Error($"托盘菜单关闭失败：{ex.Message}");
            }
        };
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
