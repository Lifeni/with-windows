using System;
using System.Collections.Generic;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Services;
using WithWindows.Tools;
using WithWindows.Views;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    /// <summary>导航项 Tag → 页面类型；由工具目录生成，不手写。</summary>
    private readonly Dictionary<string, Type> _routes = new(StringComparer.Ordinal);

    // 托盘图标必须由字段持有，否则被 GC 回收后图标消失且无任何报错
    private readonly TrayHost _tray;

    // 窗口尺寸/位置记忆；由字段持有到窗口销毁
    private readonly WindowStateStore _windowState;

    /// <summary>true 表示正在真正退出（区分「关闭窗口 = 隐藏到托盘」）。</summary>
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();

        // 用自定义 TitleBar 控件替代系统标题栏（否则会同时出现两条标题栏）
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        if (AppIcon.Exists)
            AppWindow.SetIcon(AppIcon.Preferred);

        BuildNavigation();
        NavView.SelectedItem = NavView.MenuItems[0];

        // 关闭（X 或热键）= 隐藏到托盘，不销毁窗口。
        // Window.Closed 在 WinUI 3 里不可取消，必须用 AppWindow.Closing（AppWindowClosingEventArgs.Cancel）。
        AppWindow.Closing += OnAppWindowClosing;
        _tray = new TrayHost(this);
        if (!_tray.IsAvailable)
            App.Host?.Log.Error($"托盘图标不可用：{_tray.FailureReason}");

        _windowState = new WindowStateStore(this, App.Host?.Config ?? new WithWindows.Core.Config.AppConfig(), () => App.Host?.SaveConfig());
        AppWindow.Changed += OnAppWindowChanged;

        // 必须先激活再恢复：XamlRoot 要等窗口有内容后才可用，缩放率拿不到就会按 100% 误算尺寸
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _windowState.Restore();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange)
            _windowState.OnWindowChanged();
    }

    /// <summary>按工具目录生成左栏导航项；新增有页面的工具不需要改这里。</summary>
    private void BuildNavigation()
    {
        foreach (ToolEntry tool in ToolCatalog.Tools)
        {
            if (!tool.ShowInNav || tool.PageType is null)
                continue;

            _routes[tool.Id] = tool.PageType;

            var item = new NavigationViewItem
            {
                Content = tool.Title,
                Tag = tool.Id,
                Icon = new FontIcon { Glyph = tool.Glyph },
            };
            AutomationProperties.SetAutomationId(item, ToolCatalog.NavAutomationId(tool.Id));
            NavView.MenuItems.Add(item);
        }

        // 设置页固定在左栏底部（NavigationView 的特殊设置项）
        _routes[SettingsPage.RouteTag] = typeof(SettingsPage);
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate(SettingsPage.RouteTag);
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            Navigate(tag);
    }

    private void Navigate(string tag)
    {
        if (!_routes.TryGetValue(tag, out Type? pageType) || NavFrame.CurrentSourcePageType == pageType)
            return;

        NavFrame.Navigate(pageType);
    }

    /// <summary>显示主窗口并切到指定页面；托盘与热键共用（关闭窗口只是隐藏，需要重新显示）。</summary>
    public void ShowAndNavigate(string routeTag)
    {
        ShowWindow();

        NavigationViewItem? target = null;
        foreach (object menuItem in NavView.MenuItems)
        {
            if (menuItem is NavigationViewItem { Tag: string tag } candidate && tag == routeTag)
            {
                target = candidate;
                break;
            }
        }

        // 设置页挂在 NavigationView 的特殊设置项上，不在 MenuItems 里
        if (target is null && routeTag == SettingsPage.RouteTag && NavView.SettingsItem is NavigationViewItem settingsItem)
            target = settingsItem;

        // 切换选中项会触发 SelectionChanged → Navigate；已是当前选中项时事件不触发，直接切页
        if (target is not null && !ReferenceEquals(NavView.SelectedItem, target))
            NavView.SelectedItem = target;
        else
            Navigate(routeTag);
    }

    /// <summary>仅显示并激活窗口，不改变当前页面。</summary>
    public void ShowWindow()
    {
        AppWindow.Show();
        Activate();
    }

    /// <summary>托盘里的直接动作（不切页），执行结果写日志。</summary>
    public void RunTrayAction(string toolId)
    {
        try
        {
            var router = new HotkeyRouter(this, App.Host!.Config);
            Core.Actions.ActionResult? result = router.Invoke(toolId);
            App.Host.Log.Info($"托盘动作「{toolId}」：{result?.Message ?? "已执行"}");
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"托盘动作「{toolId}」失败：{ex.Message}");
        }
    }

    /// <summary>真正退出应用；绕过「关闭 = 隐藏」的拦截。</summary>
    public void ExitApplication()
    {
        _exiting = true;
        _windowState.Dispose();
        _tray.Dispose();
        // 显式释放热键、日志与单实例互斥体（AppHost 由 App 字段持有，这里主动收尾）
        App.Host?.Dispose();
        Application.Current.Exit();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting)
            return;

        // 没有托盘图标就没有唤回窗口的入口，此时关闭应当真正退出，否则应用会「消失」
        if (!_tray.IsAvailable)
        {
            ExitApplication();
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
    }
}
