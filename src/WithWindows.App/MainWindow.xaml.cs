using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WithWindows.Services;
using WithWindows.Core.Todos;
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
    private bool _exitRequested;
    private bool _windowThemeRefreshQueued;
    private bool _windowThemeRefreshIncludesCaptionTheme;

    // 主题切换会同步写入注册表并应用壁纸；串行执行，避免快速连点时两个后台任务
    // 同时读取旧主题后重复应用同一目标。
    private readonly SemaphoreSlim _themeToggleGate = new(1, 1);

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public MainWindow(string? initialRouteTag = null)
    {
        InitializeComponent();

        // WinUI TitleBar 只管布局，窗口按钮由系统 caption 区域绘制与交互。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyTitleBarTheme(includeCaptionTheme: true);

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
        _tray = new TrayHost(this, ResolveTitleBarTheme() == ElementTheme.Dark);
        if (!_tray.IsAvailable)
            App.Host?.Log.Error($"托盘图标不可用：{_tray.FailureReason}");
        if (Content is FrameworkElement themeRoot)
            themeRoot.ActualThemeChanged += OnRootActualThemeChanged;
        RefreshTrayIcon();

        _windowState = new WindowStateStore(this, App.Host?.Config ?? new WithWindows.Core.Config.AppConfig(), () => App.Host?.SaveConfig());
        AppWindow.Changed += OnAppWindowChanged;
        Activated += OnWindowActivated;

        // 普通启动和 --tray 首次唤出都在首次激活后恢复：此时窗口有内容、DPI 已就绪，
        // 不会按 100% 误算尺寸；--tray 启动时不能提前操作尚未显示的窗口。
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
        QueueWindowThemeRefresh();
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        QueueWindowThemeRefresh(includeCaptionTheme: true);
    }

    /// <summary>
    /// 标题栏与托盘图标都依赖系统非客户区 / Shell 状态。直接写在 Activated 或
    /// ActualThemeChanged 回调里，会让 uxtheme 的 SendMessage 与 WinUI Input
    /// 重入；低优先级排到下一轮消息循环，并合并同一轮内的多次请求。
    /// </summary>
    private void QueueWindowThemeRefresh()
        => QueueWindowThemeRefresh(includeCaptionTheme: false);

    private void QueueWindowThemeRefresh(bool includeCaptionTheme)
    {
        _windowThemeRefreshIncludesCaptionTheme |= includeCaptionTheme;
        if (_windowThemeRefreshQueued)
            return;

        _windowThemeRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    bool refreshCaptionTheme = _windowThemeRefreshIncludesCaptionTheme;
                    _windowThemeRefreshQueued = false;
                    _windowThemeRefreshIncludesCaptionTheme = false;
                    if (App.Host is { IsShuttingDown: true })
                        return;

                    ApplyTitleBarTheme(refreshCaptionTheme);
                    RefreshTrayIcon();
                }))
        {
            _windowThemeRefreshQueued = false;
            _windowThemeRefreshIncludesCaptionTheme = false;
        }
    }

    /// <summary>按当前托盘样式与窗口实际主题刷新图标；闪电图标据此在亮暗主题间换色。</summary>
    public void RefreshTrayIcon()
    {
        if (App.Host is not { IsShuttingDown: false } host)
            return;

        _tray.ApplyIconStyle(
            host.Config.Tray.IconStyle,
            ResolveTitleBarTheme() == ElementTheme.Dark);
    }

    /// <summary>
    /// 系统亮暗主题变化后，WinUI 内容会自动换色，但窗口按钮仍由系统 caption
    /// 区域绘制。PreferredTheme 只负责 caption 背景；窗口非前台时系统不会重算
    /// 按钮字形，必须显式同步前景色，并在重新激活时再刷新一次。扩展内容到
    /// 标题栏时按钮背景保持透明，露出 Mica。
    /// </summary>
    private void ApplyTitleBarTheme(bool includeCaptionTheme)
    {
        IntPtr hwnd = Hwnd;
        if (!Core.Interop.WindowInterop.IsWindow(hwnd)
            || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        ElementTheme theme = ResolveTitleBarTheme();
        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        if (includeCaptionTheme)
        {
            titleBar.PreferredTheme = theme switch
            {
                ElementTheme.Dark => TitleBarTheme.Dark,
                ElementTheme.Light => TitleBarTheme.Light,
                _ => TitleBarTheme.UseDefaultAppMode,
            };
        }

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

        Windows.UI.Color foreground = CaptionButtonForeground(theme);
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = CaptionButtonInactiveForeground();
    }

    private ElementTheme ResolveTitleBarTheme()
    {
        if (Content is FrameworkElement root
            && root.ActualTheme != ElementTheme.Default)
        {
            return root.ActualTheme;
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;
    }

    private static Windows.UI.Color CaptionButtonForeground(ElementTheme theme)
        => theme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    private static Windows.UI.Color CaptionButtonInactiveForeground()
        => Windows.UI.Color.FromArgb(0xFF, 0x99, 0x99, 0x99);

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        LogLifecycle($"主窗口 Activated：{args.WindowActivationState}");

        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            _showRecoveryTimer?.Stop();
            _showRecoveryTimer = null;
            return;
        }

        // 主题可能在窗口非前台时切换；DWM 只刷新 caption 背景，
        // 不会重算按钮字形，激活后再同步一次。必须避开激活消息栈。
        QueueWindowThemeRefresh();

        // 不直接操作可能已经失效的页面；先等待，仍然未挂载时重启进程恢复。
        ScheduleShowRecovery(
            _pendingRouteTag ?? CurrentRouteTag(),
            _showGeneration,
            _navigationGeneration,
            RecoveryCheckCount);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            _windowState.OnWindowChanged(applyMinimumSize: true);
        }
        else if (args.DidPositionChange)
        {
            // 拖动标题栏会连续触发位置变化。OverlappedPresenter 在系统移动循环中
            // 被反复写入 PreferredMinimum* 会进入 WinUI 的 E_INVALIDARG fail-fast；
            // 位置变化只需保存位置，最小尺寸在尺寸或 DPI 变化时再重算。
            _windowState.OnWindowChanged(applyMinimumSize: false);
        }
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
        bool navigated = NavFrame.Navigate(
            pageType,
            parameter: null,
            new EntranceNavigationTransitionInfo());
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

            if (AppWindow.Presenter is OverlappedPresenter
                && Core.Interop.WindowInterop.IsWindow(hwnd))
            {
                try
                {
                    AppWindow.IsShownInSwitchers = true;
                }
                catch (Exception ex)
                {
                    LogLifecycle($"托盘唤出：恢复到切换器失败：{ex.Message}");
                }
            }

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

    /// <summary>在 UI 线程完成窗口状态判断、原生通知和失败回退。</summary>
    public TodoReminderDeliveryResult DeliverTodoReminder(IReadOnlyList<string> titles)
    {
        if (!DispatcherQueue.HasThreadAccess)
            return TodoReminderDeliveryResult.Failed;

        if (!Core.Todos.TodoReminderVisibilityPolicy.CanNotify(
                Core.Interop.WindowInterop.IsVisible(Hwnd),
                Core.Interop.WindowInterop.IsMinimized(Hwnd)))
        {
            return TodoReminderDeliveryResult.Deferred;
        }

        if (_tray.ShowTodoNotification(titles))
        {
            App.Host?.Log.Info("待办提醒：已显示 Windows 系统通知");
            return TodoReminderDeliveryResult.Delivered;
        }

        App.Host?.Log.Info("待办提醒：Windows 系统通知不可用，回退到应用内提醒");
        ShowTodoReminder(titles);
        return TodoReminderDeliveryResult.Delivered;
    }

    /// <summary>系统通知不可用时，在 UI 线程显示应用内提醒并唤出待办页。</summary>
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
    /// 以「隐藏到托盘」方式启动（开机自启带 <c>--tray</c>）：保留新窗口的初始未显示状态，
    /// 不激活、最小化或提前恢复尺寸。用户点托盘图标或按热键时再显示并由首次激活恢复尺寸。
    /// </summary>
    public void StartHidden()
    {
        HideToTray(initialStartup: true);
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
        if (string.Equals(toolId, ToolCatalog.ThemeToggleId, StringComparison.Ordinal))
        {
            RunThemeToggleInBackground();
            return;
        }

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

    /// <summary>
    /// 托盘与热键共用：系统主题广播和壁纸写入都放到后台，避免同步等待 Shell
    /// 与桌面窗口而让主窗口数秒无响应。自动切换本来就在计时器线程执行。
    /// </summary>
    public void RunThemeToggleInBackground()
    {
        if (App.Host is not { IsShuttingDown: false } host)
            return;

        _ = ToggleThemeInBackgroundAsync(host);
    }

    private async Task ToggleThemeInBackgroundAsync(AppHost host)
    {
        await _themeToggleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (host.IsShuttingDown)
                return;

            var stopwatch = Stopwatch.StartNew();
            Core.Actions.ActionResult result = await Task.Run(
                () => new Core.Actions.ThemeToggleAction(host.Config.ThemeSwitch)
                    .Execute("toggle")).ConfigureAwait(false);
            stopwatch.Stop();

            if (!host.IsShuttingDown)
            {
                host.Log.Info(
                    $"托盘主题动作：{result.Message}；系统应用耗时 {stopwatch.ElapsedMilliseconds} ms");
                DispatcherQueue.TryEnqueue(RefreshTrayIcon);
            }
        }
        catch (Exception ex)
        {
            if (!host.IsShuttingDown)
                host.Log.Error($"托盘主题动作失败：{ex.Message}");
        }
        finally
        {
            _themeToggleGate.Release();
        }
    }

    /// <summary>页面在激活后仍失效时重启进程，避免继续操作已卸载的 XAML 树。</summary>
    private async void RestartApplication(string pageTag)
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

        try
        {
            await App.ShutdownAsync();
            _windowState.Dispose();
            _tray.Dispose();
            App.Host?.Dispose();
            App.ExitForApplicationRestart();
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"页面恢复重启的关闭流程失败：{ex}");
            Environment.Exit(1);
        }
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

    /// <summary>
    /// 托盘菜单关闭后进入退出流程。菜单点击回调不能直接释放托盘与 XAML 对象，
    /// 因此这里再排一轮 UI 队列，并捕获所有托管异常后强制结束进程。
    /// </summary>
    public void RequestExitFromTray()
    {
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ExitApplicationAsync();
                }
                catch (Exception ex)
                {
                    App.Host?.Log.Error($"托盘退出流程异常：{ex}");
                    Environment.Exit(1);
                }
            }))
        {
            App.Host?.Log.Error("无法排队执行退出流程，直接结束进程。");
            Environment.Exit(1);
        }
    }

    /// <summary>真正退出应用；先等待所有后台回调与脚本停止。</summary>
    public async Task ExitApplicationAsync()
    {
        if (_exiting || _exitRequested)
            return;

        _exitRequested = true;
        try
        {
            if (App.Scheduler?.HasActiveTasks == true)
            {
                if (_isHiddenToTray || Core.Interop.WindowInterop.IsMinimized(Hwnd))
                {
                    try
                    {
                        ShowWindow();
                    }
                    catch (Exception ex)
                    {
                        App.Host?.Log.Error($"退出前唤出窗口失败：{ex.Message}");
                    }
                }

                await Task.Yield();

                if (Content.XamlRoot is not { } xamlRoot)
                {
                    App.Host?.Log.Error("有脚本正在运行，但主窗口尚未挂载，无法显示退出确认。");
                    // 用户已从托盘明确请求退出；无法展示确认时也必须完成退出，
                    // 否则会留下一个点了“退出”却没有反应的常驻进程。
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        XamlRoot = xamlRoot,
                        Title = "脚本正在运行",
                        Content = "退出 With Windows 将停止所有正在运行的脚本。",
                        PrimaryButtonText = "停止并退出",
                        CloseButtonText = "取消",
                        DefaultButton = ContentDialogButton.Close,
                    };
                    if (await App.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
                    {
                        _exitRequested = false;
                        return;
                    }
                }
            }

            // 用户已确认退出后不再回滚状态。任何清理步骤失败都只记录日志，
            // 最后仍强制结束进程，避免“点了退出但进程还在”。
            _exiting = true;
            _showRecoveryTimer?.Stop();
            _showRecoveryTimer = null;
            App.Host?.Log.Info("退出应用：开始停止后台任务");

            bool cleanupFailed = false;
            void RecordFailure(string stage, Exception ex)
            {
                cleanupFailed = true;
                App.Host?.Log.Error($"退出应用：{stage}失败：{ex}");
            }

            try
            {
                await App.ShutdownAsync();
                App.Host?.Log.Info("退出应用：后台任务已停止");
            }
            catch (Exception ex)
            {
                RecordFailure("停止后台任务", ex);
            }

            try
            {
                _windowState.Dispose();
                App.Host?.Log.Info("退出应用：窗口状态已保存");
            }
            catch (Exception ex)
            {
                RecordFailure("保存窗口状态", ex);
            }

            try
            {
                _tray.Dispose();
                App.Host?.Log.Info("退出应用：托盘已释放");
            }
            catch (Exception ex)
            {
                RecordFailure("释放托盘", ex);
            }

            App.Host?.Log.Info("退出应用：即将结束进程");
            try
            {
                // 显式释放热键、日志与单实例互斥体（AppHost 由 App 字段持有）。
                App.Host?.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailed = true;
                System.Diagnostics.Debug.WriteLine($"退出应用：释放 AppHost 失败：{ex}");
            }

            // 不在托盘菜单输入回调或可能已损坏的 XAML 树中调用 Application.Exit()。
            // 进程级退出与页面恢复重启共用同一条已验证路径。
            Environment.Exit(cleanupFailed ? 1 : 0);
        }
        catch (Exception ex)
        {
            // 走到这里说明退出前的准备阶段出现异常。用户已明确要求退出，
            // 不再重置状态或让窗口继续驻留，直接完成进程收尾。
            App.Host?.Log.Error($"退出应用失败，强制结束：{ex}");
            _exiting = true;
            try
            {
                await App.ShutdownAsync();
            }
            catch (Exception shutdownException)
            {
                App.Host?.Log.Error($"退出应用：强制关闭后台失败：{shutdownException}");
            }

            try
            {
                _windowState.Dispose();
            }
            catch (Exception stateException)
            {
                App.Host?.Log.Error($"退出应用：强制保存窗口状态失败：{stateException}");
            }

            try
            {
                _tray.Dispose();
            }
            catch (Exception trayException)
            {
                App.Host?.Log.Error($"退出应用：强制释放托盘失败：{trayException}");
            }

            try
            {
                App.Host?.Dispose();
            }
            catch (Exception hostException)
            {
                System.Diagnostics.Debug.WriteLine($"退出应用：强制释放 AppHost 失败：{hostException}");
            }

            Environment.Exit(1);
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting)
            return;

        // 系统关闭按钮与 Alt+F4 都收进托盘；真正退出只走托盘「退出」。
        // 在事件回调栈里直接切换 presenter 会与 WinUI 的消息处理重入，
        // 因此先取消关闭，再排队收起窗口。
        args.Cancel = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_tray.IsAvailable)
                HideToTray();
            else
                await ExitApplicationAsync();
        });
    }

    private void HideToTray(bool initialStartup = false)
    {
        if (_isHiddenToTray)
            return;

        _isHiddenToTray = true;
        _showRecoveryTimer?.Stop();
        _showRecoveryTimer = null;
        if (initialStartup)
        {
            // 新创建的 WinUI 窗口本就未显示。此时不要最小化或隐藏：在 OnLaunched 栈内
            // 改变从未激活窗口的状态会触发 Microsoft.UI.Xaml.dll 的 0xC000027B。
            LogLifecycle("托盘启动：窗口保持未显示");
            LogLifecycle("托盘收起：完成");
            return;
        }

        IntPtr hwnd = Hwnd;
        if (!Core.Interop.WindowInterop.IsWindow(hwnd))
        {
            LogLifecycle("托盘收起：窗口句柄已失效");
            return;
        }

        // 必须在窗口仍然有效、尚未最小化时移除切换器状态。本机实测反序执行时，
        // AppWindow.IsShownInSwitchers 偶发以 E_INVALIDARG（0x80070578）原生崩溃。
        if (AppWindow.Presenter is OverlappedPresenter)
        {
            try
            {
                LogLifecycle("托盘收起：从切换器移除");
                AppWindow.IsShownInSwitchers = false;
            }
            catch (Exception ex)
            {
                LogLifecycle($"托盘收起：从切换器移除失败：{ex.Message}");
            }
        }

        LogLifecycle("托盘收起：最小化");
        Core.Interop.WindowInterop.Minimize(hwnd);
        LogLifecycle("托盘收起：完成");
    }
}
