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
    // 页面恢复只在显示或导航后短暂运行；2 秒足以避开冷启动和锁屏恢复时的瞬时未挂载。
    private const int RecoveryCheckCount = 10;

    /// <summary>导航项 Tag → 页面类型；由工具目录生成，不手写。</summary>
    private readonly Dictionary<string, Type> _routes = new(StringComparer.Ordinal);

    // 托盘图标必须由字段持有，否则被 GC 回收后图标消失且无任何报错
    private readonly TrayHost _tray;

    // 窗口尺寸/位置记忆；由字段持有到窗口销毁
    private readonly WindowStateStore _windowState;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _showRecoveryTimer;
    private int _showGeneration;
    private int _navigationGeneration;
    private string? _pendingRouteTag;
    private bool _pendingNavigationAttempted;
    private IReadOnlyList<string>? _pendingTodoReminderTitles;
    private bool _isHiddenToTray;

    /// <summary>true 表示正在真正退出（区分「关闭窗口 = 隐藏到托盘」）。</summary>
    private bool _exiting;

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public MainWindow(string? initialRouteTag = null)
    {
        InitializeComponent();

        // WinUI TitleBar 只管布局，窗口按钮由系统 caption 区域绘制与交互。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        if (AppIcon.Exists)
            AppWindow.SetIcon(AppIcon.Preferred);

        BuildNavigation();
        NavFrame.Navigating += (_, args) =>
            LogLifecycle($"导航开始：{NavFrame.CurrentSourcePageType?.Name ?? "(无)"} → {args.SourcePageType.Name}");
        NavFrame.Navigated += (_, args) =>
            LogLifecycle($"导航完成：{args.SourcePageType.Name}");
        InitializeInitialPage(initialRouteTag);

        // 关闭（X 或热键）= 隐藏到托盘，不销毁窗口。
        // Window.Closed 在 WinUI 3 里不可取消，必须用 AppWindow.Closing（AppWindowClosingEventArgs.Cancel）。
        AppWindow.Closing += OnAppWindowClosing;
        _tray = new TrayHost(this);
        if (!_tray.IsAvailable)
            App.Host?.Log.Error($"托盘图标不可用：{_tray.FailureReason}");

        _windowState = new WindowStateStore(this, App.Host?.Config ?? new WithWindows.Core.Config.AppConfig(), () => App.Host?.SaveConfig());
        AppWindow.Changed += OnAppWindowChanged;
        Activated += OnWindowActivated;

        // 普通启动在首次激活后恢复：此时窗口有内容、DPI 已就绪，不会按 100% 误算尺寸；
        // 隐藏启动（--tray）不激活窗口，由 StartHidden() 在创建后立即恢复。
        Activated += OnFirstActivated;
    }

    private void InitializeInitialPage(string? initialRouteTag)
    {
        if (initialRouteTag is not null && _routes.ContainsKey(initialRouteTag))
        {
            Navigate(initialRouteTag);
            if (FindNavigationItem(initialRouteTag) is { } target)
                NavView.SelectedItem = target;
            return;
        }

        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _windowState.Restore();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        LogLifecycle($"主窗口 Activated：{args.WindowActivationState}");

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _showRecoveryTimer?.Stop();
            _showRecoveryTimer = null;
            return;
        }

        // 不直接操作可能已经失效的页面；先等待，仍然未挂载时重启进程恢复。
        ScheduleShowRecovery(
            _pendingRouteTag ?? CurrentRouteTag(),
            _showGeneration,
            _navigationGeneration,
            RecoveryCheckCount);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange)
            _windowState.OnWindowChanged();

    }

    private static void LogLifecycle(string message)
    {
        if (App.Host is { IsShuttingDown: false } host)
            host.Log.Info(message);
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

    private void OnPaneToggleClick(object sender, RoutedEventArgs e)
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
        if (!_routes.TryGetValue(tag, out Type? pageType))
        {
            _pendingRouteTag = null;
            _pendingNavigationAttempted = false;
            return;
        }

        if (!CanNavigateCurrentPage())
        {
            if (_pendingRouteTag != tag)
                _pendingNavigationAttempted = false;
            _pendingRouteTag = tag;
            LogLifecycle($"页面尚未挂载，暂缓导航：{pageType.Name}");
            ScheduleShowRecovery(
                tag,
                _showGeneration,
                _navigationGeneration,
                RecoveryCheckCount);
            return;
        }

        if (NavFrame.CurrentSourcePageType == pageType)
        {
            _pendingRouteTag = null;
            _pendingNavigationAttempted = false;
            ScheduleShowRecovery(
                tag,
                _showGeneration,
                _navigationGeneration,
                RecoveryCheckCount);
            TryShowPendingTodoReminder();
            return;
        }

        _pendingNavigationAttempted = true;
        bool navigated = NavFrame.Navigate(pageType);
        if (!navigated)
        {
            _pendingRouteTag = tag;
            ScheduleShowRecovery(
                tag,
                _showGeneration,
                _navigationGeneration,
                RecoveryCheckCount);
            return;
        }

        _navigationGeneration++;
        _pendingRouteTag = null;
        _pendingNavigationAttempted = false;
        ScheduleShowRecovery(
            tag,
            _showGeneration,
            _navigationGeneration,
            RecoveryCheckCount);
        TryShowPendingTodoReminder();
    }

    /// <summary>显示主窗口并切到指定页面；托盘与热键共用（关闭窗口只是隐藏，需要重新显示）。</summary>
    public void ShowAndNavigate(string routeTag)
    {
        if (!_routes.ContainsKey(routeTag))
            return;

        _pendingRouteTag = routeTag;
        _pendingNavigationAttempted = false;
        ShowWindow();
        TryApplyPendingNavigation();
    }

    private void TryApplyPendingNavigation()
    {
        if (_pendingRouteTag is not { } routeTag || !CanNavigateCurrentPage())
            return;

        NavigationViewItem? target = FindNavigationItem(routeTag);

        // 切换选中项会触发 SelectionChanged → Navigate；事件未同步完成时再直接导航一次兜底。
        if (target is not null && !ReferenceEquals(NavView.SelectedItem, target))
            NavView.SelectedItem = target;
        if (_pendingRouteTag is null || _pendingNavigationAttempted)
            return;

        Navigate(routeTag);
    }

    private NavigationViewItem? FindNavigationItem(string routeTag)
    {
        NavigationViewItem? target = null;
        foreach (object menuItem in NavView.MenuItems)
        {
            if (menuItem is NavigationViewItem { Tag: string tag } candidate && tag == routeTag)
            {
                target = candidate;
                break;
            }
        }

        // 设置页挂在 NavigationView 的特殊设置项上，不在 MenuItems 里。
        if (target is null && routeTag == SettingsPage.RouteTag && NavView.SettingsItem is NavigationViewItem settingsItem)
            target = settingsItem;

        return target;
    }

    private bool CanNavigateCurrentPage()
    {
        Page? page = NavFrame.Content as Page;
        return Core.App.WindowRecoveryPolicy.CanNavigatePage(
            NavFrame.CurrentSourcePageType is not null,
            page is { IsLoaded: true },
            page?.XamlRoot is not null);
    }

    /// <summary>仅显示并激活窗口，不改变当前页面。</summary>
    public void ShowWindow()
    {
        int showGeneration = ++_showGeneration;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool needsShow = _isHiddenToTray || Core.Interop.WindowInterop.IsMinimized(hwnd);
        if (needsShow)
        {
            Core.Interop.WindowInterop.ClearCaptionButtonHover(hwnd);

            if (AppWindow.Presenter is OverlappedPresenter)
                AppWindow.IsShownInSwitchers = true;

            // SW_SHOWNOACTIVATE 能从最小化状态恢复内容且不会触发第二次激活。
            // Presenter.Restore() 在本次崩溃环境中会破坏 XAML 内容树。
            Core.Interop.WindowInterop.Show(hwnd);
        }

        _isHiddenToTray = false;
        Activate();
        // Activate() 对「已可见但在别的窗口后面」不可靠（再次启动 / 托盘左键都会走到这里），补一次显式前置
        Core.Interop.WindowInterop.BringToFront(hwnd);
        ScheduleShowRecovery(
            _pendingRouteTag ?? CurrentRouteTag(),
            showGeneration,
            _navigationGeneration,
            RecoveryCheckCount);
    }

    /// <summary>再次启动应用时显示现有窗口，并保留当前页面。</summary>
    public void ShowCurrentPage() => ShowWindow();

    /// <summary>把到期项作为应用内提醒显示，并唤出待办页。</summary>
    public void ShowTodoReminder(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0)
            return;

        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowTodoReminder(titles));
            return;
        }

        _pendingTodoReminderTitles = titles;
        ShowAndNavigate(ToolCatalog.TodoId);
        DispatcherQueue.TryEnqueue(TryShowPendingTodoReminder);
    }

    private void TryShowPendingTodoReminder()
    {
        if (_pendingTodoReminderTitles is not { } titles)
            return;

        if (NavFrame.Content is not TodoPage { IsLoaded: true, XamlRoot: not null } page)
        {
            LogLifecycle("待办提醒已到期，但待办页尚未挂载。");
            return;
        }

        _pendingTodoReminderTitles = null;
        try
        {
            page.ShowReminderNotice(titles);
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"显示待办提醒失败：{ex.Message}");
        }
    }

    private void ScheduleShowRecovery(
        string expectedRouteTag,
        int showGeneration,
        int navigationGeneration,
        int remainingChecks)
    {
        if (!_routes.TryGetValue(expectedRouteTag, out Type? expectedPageType))
            return;

        _showRecoveryTimer?.Stop();
        var timer = DispatcherQueue.CreateTimer();
        _showRecoveryTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(200);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!ReferenceEquals(_showRecoveryTimer, timer)
                || showGeneration != _showGeneration
                || navigationGeneration != _navigationGeneration
                || App.Host is { IsShuttingDown: true })
            {
                return;
            }

            _showRecoveryTimer = null;
            Page? page = NavFrame.Content as Page;
            bool expectedPageReady = Core.App.WindowRecoveryPolicy.IsExpectedPageReady(
                page?.GetType() == expectedPageType,
                page is { IsLoaded: true },
                page?.XamlRoot is not null);
            Core.App.WindowRecoveryStep step = Core.App.WindowRecoveryPolicy.DecideShowRecovery(
                expectedPageReady,
                remainingChecks,
                hasPageType: true);
            switch (step)
            {
                case Core.App.WindowRecoveryStep.Stop:
                    if (_pendingRouteTag == expectedRouteTag)
                    {
                        _pendingRouteTag = null;
                        _pendingNavigationAttempted = false;
                    }
                    App.MarkPageReady();
                    TryShowPendingTodoReminder();
                    return;

                case Core.App.WindowRecoveryStep.Wait:
                    if (_pendingRouteTag is not null
                        && !_pendingNavigationAttempted
                        && CanNavigateCurrentPage())
                    {
                        TryApplyPendingNavigation();
                        return;
                    }

                    ScheduleShowRecovery(
                        expectedRouteTag,
                        showGeneration,
                        navigationGeneration,
                        remainingChecks - 1);
                    return;

                case Core.App.WindowRecoveryStep.Restart:
                    LogLifecycle($"窗口激活后目标页面仍未挂载，重启应用以恢复：{expectedPageType.Name}");
                    RestartApplication(expectedRouteTag);
                    return;
            }

            LogLifecycle("窗口显示后页面未能恢复挂载。");
        };
        timer.Start();
    }

    /// <summary>托盘图标是否可用；隐藏启动前必须确认，否则窗口藏起来后应用就没有任何入口。</summary>
    public bool IsTrayAvailable => _tray.IsAvailable;

    /// <summary>
    /// 以「隐藏到托盘」方式启动（开机自启带 <c>--tray</c>）：不激活窗口，主窗口只待在托盘里，
    /// 用户点托盘图标或按热键时再唤出。
    /// 尺寸/位置在这里就恢复好：窗口一直没激活过，等首次唤出才恢复会先按默认尺寸闪一下再跳成保存的尺寸。
    /// </summary>
    public void StartHidden()
    {
        _windowState.Restore();
        HideToTray();
    }

    /// <summary>
    /// 「显示 / 隐藏窗口」热键：按一次唤出（并切到指定页面），再按一次收起。
    /// 判定放 Core（纯逻辑，可测）：隐藏 → 显示；前台可见 → 隐藏；可见但被别的窗口挡住 → 只前置聚焦，
    /// 否则用户正要找的窗口会被一把藏起来；最小化也算「要显示」。托盘左键与托盘菜单不经过这里，仍然只负责显示。
    /// </summary>
    public void ToggleWindow(string routeTag)
    {
        if (_isHiddenToTray)
        {
            App.Host?.Log.Info("热键：从托盘唤出主窗口");
            ShowAndNavigate(routeTag);
            return;
        }

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Core.App.WindowToggleAction action = Core.App.WindowToggle.Decide(
            Core.Interop.WindowInterop.IsVisible(hwnd),
            Core.Interop.WindowInterop.IsForeground(hwnd),
            Core.Interop.WindowInterop.IsMinimized(hwnd));

        switch (action)
        {
            case Core.App.WindowToggleAction.Hide:
                App.Host?.Log.Info("热键：主窗口正在前台，隐藏到托盘");
                HideToTray();
                break;

            case Core.App.WindowToggleAction.Activate:
                App.Host?.Log.Info("热键：主窗口已显示但不在前台，前置");
                Activate();
                // Activate() 对「已可见但在后面」的窗口不可靠，补一次显式前置
                Core.Interop.WindowInterop.BringToFront(hwnd);
                break;

            default:
                App.Host?.Log.Info("热键：唤出主窗口");
                ShowAndNavigate(routeTag);
                break;
        }
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

    /// <summary>页面在激活后仍失效时重启进程，避免继续操作已卸载的 XAML 树。</summary>
    private void RestartApplication(string pageTag)
    {
        if (_exiting)
            return;

        _exiting = true;
        _showRecoveryTimer?.Stop();
        _showRecoveryTimer = null;
        if (!App.TryStartApplicationRestart(pageTag))
        {
            _exiting = false;
            return;
        }

        _windowState.Dispose();
        _tray.Dispose();
        App.ExitForApplicationRestart();
    }

    private string CurrentRouteTag()
    {
        if (NavView.SelectedItem is NavigationViewItem { Tag: string tag })
            return tag;

        if (NavFrame.CurrentSourcePageType == typeof(SettingsPage))
            return SettingsPage.RouteTag;

        Type? pageType = NavFrame.CurrentSourcePageType;
        if (pageType is not null)
        {
            foreach ((string route, Type type) in _routes)
            {
                if (type == pageType)
                    return route;
            }
        }

        return ToolCatalog.NotepadId;
    }

    /// <summary>真正退出应用；绕过「关闭 = 隐藏」的拦截。</summary>
    public void ExitApplication()
    {
        _exiting = true;
        App.StopTodoReminders();
        _showRecoveryTimer?.Stop();
        _showRecoveryTimer = null;
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

        // 系统关闭按钮与 Alt+F4 都收进托盘；真正退出只走托盘「退出」。
        // 在事件回调栈里直接切换 presenter 会与 WinUI 的消息处理重入，
        // 因此先取消关闭，再排队收起窗口。
        args.Cancel = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_tray.IsAvailable)
                HideToTray();
            else
                ExitApplication();
        });
    }

    private void HideToTray()
    {
        if (_isHiddenToTray)
            return;

        _isHiddenToTray = true;
        _showRecoveryTimer?.Stop();
        _showRecoveryTimer = null;
        LogLifecycle("托盘收起：最小化");
        Core.Interop.WindowInterop.Minimize(Hwnd);
        if (AppWindow.Presenter is OverlappedPresenter)
        {
            LogLifecycle("托盘收起：从切换器移除");
            AppWindow.IsShownInSwitchers = false;
        }

        LogLifecycle("托盘收起：完成");
    }
}
