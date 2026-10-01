using System.Reflection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows;

/// <summary>
/// 设置窗口：左栏（切换投屏卡片、模式勾选、恢复默认、开机自启、关于），
/// 右栏（快捷记事 / 切换投屏快捷键）。修改即自动保存并热重载。
/// </summary>
public sealed partial class ToggleWindow : Window
{
    private const string Unset = "未设置";
    /// <summary>设计尺寸；小屏/高缩放下会按工作区收缩（内容由 ScrollViewer 滚动）。</summary>
    private const int DesignWidth = 520;
    private const int DesignHeight = 780;

    private readonly ConfigStore _configStore;
    private readonly Func<IReadOnlyList<string>> _onSaved;
    private readonly Logger _log;
    private readonly DispatcherQueueTimer _statusTimer;
    private IntPtr _hwnd;
    private bool _loading; // LoadConfig 期间屏蔽 AutoSave（避免回填触发保存）

    public ToggleWindow(ConfigStore configStore, Func<IReadOnlyList<string>> onSaved, Logger log)
    {
        _configStore = configStore;
        _onSaved = onSaved;
        _log = log;
        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(3);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusBar.IsOpen = false; };

        InitializeComponent();
        SetupTitleBar();
        AppWindow.Title = "设置";
        Closed += (_, _) => SaveWindowState();
        LoadConfig();
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // 窗口类背景画刷 = 内容同色，显示瞬间即灰色无黑框
        NativeMethods.SetClassLongPtr(_hwnd, -10 /* GCLP_HBRBACKGROUND */,
            NativeMethods.CreateSolidBrush(0x00302B2B)); // RGB(0x2B, 0x2B, 0x30)

        string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        // 显式 32x32 帧：避免系统默认尺寸取帧不一致导致拉伸/模糊
        IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 32, 32, 0x10 /* LR_LOADFROMFILE */);
        if (hIcon != IntPtr.Zero)
            AppWindow.SetIcon(new IconId((ulong)hIcon));
    }

    public void ShowAndFocus()
    {
        // 每次打开恢复记忆尺寸（窗口不可调；拓扑变化可能改系统尺寸，这里强制还原）
        RestoreWindowState();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsResizable = false; // 固定尺寸
        EnsureWindowVisible(); // 防拓扑变化后窗口跑到屏幕外
        Activate();
    }

    /// <summary>
    /// 恢复记忆的尺寸/位置。尺寸按所在显示器工作区钳制：窗口不可缩放，
    /// 1366×768 这类小屏（或高缩放）下 780 高会超出屏幕，底部设置项会被裁掉。
    /// </summary>
    private void RestoreWindowState()
    {
        try
        {
            var ws = _configStore.Load().WindowState;
            int width = (int)ws.SettingsWidth;
            int height = (int)ws.SettingsHeight;

            if (WorkAreaFor(ws.SettingsX, ws.SettingsY) is { } work)
            {
                width = (int)Math.Clamp(width, Math.Min(DesignWidth, work.Width), Math.Max(1, work.Width));
                height = (int)Math.Clamp(height, Math.Min(DesignHeight, work.Height), Math.Max(1, work.Height));
            }

            AppWindow.Resize(new SizeInt32(width, height));
            if (ws.SettingsX is double x && ws.SettingsY is double y
                && IsOnAnyDisplay((int)x, (int)y, width, height))
                AppWindow.Move(new PointInt32((int)x, (int)y));
        }
        catch (Exception ex)
        {
            _log.Error($"窗口状态恢复失败: {ex}");
        }
    }

    /// <summary>位置所在显示器（或主屏）的工作区；查询失败返回 null，调用方不钳制尺寸。</summary>
    private static RectInt32? WorkAreaFor(double? x, double? y)
    {
        try
        {
            var point = x is double px && y is double py
                ? new PointInt32((int)px, (int)py)
                : new PointInt32(0, 0);
            return DisplayArea.GetFromPoint(point, DisplayAreaFallback.Primary).WorkArea;
        }
        catch
        {
            return null; // 校验失败不阻塞窗口打开
        }
    }

    /// <summary>若窗口不在任何显示器内，移到主屏居中。</summary>
    private void EnsureWindowVisible()
    {
        var size = AppWindow.Size;
        var pos = AppWindow.Position;
        if (IsOnAnyDisplay(pos.X, pos.Y, size.Width, size.Height)) return;

        var primary = DisplayArea.GetFromPoint(new PointInt32(0, 0), DisplayAreaFallback.Primary);
        AppWindow.Move(new PointInt32(
            primary.WorkArea.X + (primary.WorkArea.Width - size.Width) / 2,
            primary.WorkArea.Y + (primary.WorkArea.Height - size.Height) / 2));
        _log.Info("[toggle] 窗口位置超出屏幕，已移到主屏居中");
    }

    /// <summary>窗口位置可见性：落在任一显示器工作区内才恢复，否则由 ShowAndFocus 移到主屏居中。</summary>
    private static bool IsOnAnyDisplay(int x, int y, int width, int height)
    {
        try
        {
            return DisplayArea.FindAll().Any(da =>
                x < da.WorkArea.X + da.WorkArea.Width &&
                x + width > da.WorkArea.X &&
                y < da.WorkArea.Y + da.WorkArea.Height &&
                y + height > da.WorkArea.Y);
        }
        catch
        {
            return true; // 校验失败视为可见，不阻塞窗口打开
        }
    }

    /// <summary>保存窗口尺寸/位置（关闭时）。</summary>
    private void SaveWindowState()
    {
        try
        {
            var config = _configStore.Load();
            var ws = config.WindowState;
            ws.SettingsWidth = AppWindow.Size.Width;
            ws.SettingsHeight = AppWindow.Size.Height;
            var pos = AppWindow.Position;
            ws.SettingsX = pos.X;
            ws.SettingsY = pos.Y;
            _configStore.Save(config);
        }
        catch (Exception ex)
        {
            _log.Error($"窗口状态保存失败: {ex}");
        }
    }

    private void LoadConfig()
    {
        _loading = true;
        try
        {
            var config = _configStore.Load();

            NotepadHotkeyText.Text = FormatHotkeyText(config.Bindings, "notepad");
            DisplayHotkeyText.Text = FormatHotkeyText(config.Bindings, "display_mode");

            AutoStartToggle.IsOn = AutoStart.IsEnabled();

            ModeInternal.IsChecked = config.DisplayMode.Modes.Contains("internal");
            ModeExtend.IsChecked = config.DisplayMode.Modes.Contains("extend");
            ModeExternal.IsChecked = config.DisplayMode.Modes.Contains("external");
            ModeClone.IsChecked = config.DisplayMode.Modes.Contains("clone");

            AboutText.Text = $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";
        }
        finally
        {
            _loading = false;
        }
    }

    private static string FormatHotkeyText(Dictionary<string, string> bindings, string action)
        => string.IsNullOrWhiteSpace(bindings.GetValueOrDefault(action)) ? Unset : bindings[action];

    // ---- 快捷键设置（弹窗录制） ----

    private async void OnSetNotepadHotkey(object sender, RoutedEventArgs e)
        => await SetHotkey("notepad", "设置记事本快捷键", NotepadHotkeyText);

    private async void OnSetDisplayHotkey(object sender, RoutedEventArgs e)
        => await SetHotkey("display_mode", "设置投屏快捷键", DisplayHotkeyText);

    private async Task SetHotkey(string action, string title, TextBlock display)
    {
        var current = display.Text == Unset ? "" : display.Text;
        var box = new Controls.HotkeyInputBox { HotkeyText = current, MinWidth = 240 };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) => box.FocusInput(); // 打开后自动进入录制模式

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var config = _configStore.Load();
            config.Bindings[action] = box.HotkeyText.Trim();
            _configStore.Save(config);
            display.Text = FormatHotkeyText(config.Bindings, action);
            _log.Info($"[toggle] {action} 快捷键已更新");
            ReloadAndReport("快捷键已更新");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 快捷键保存失败: {ex}");
            ShowStatus($"保存失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ---- 自动保存 ----

    private void OnModeChanged(object sender, RoutedEventArgs e) => AutoSave();

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (AutoStartToggle.IsOn) AutoStart.Enable();
        else AutoStart.Disable();
        ShowStatus(AutoStartToggle.IsOn ? "开机自启已开启" : "开机自启已关闭");
        _log.Info($"[toggle] 开机自启 {(AutoStartToggle.IsOn ? "已启用" : "已停用")}");
    }

    private void AutoSave()
    {
        if (_loading) return;
        try
        {
            var config = _configStore.Load();

            var modes = new List<string>();
            if (ModeInternal.IsChecked == true) modes.Add("internal");
            if (ModeExtend.IsChecked == true) modes.Add("extend");
            if (ModeExternal.IsChecked == true) modes.Add("external");
            if (ModeClone.IsChecked == true) modes.Add("clone");
            config.DisplayMode.Modes = modes.Count > 0 ? modes : new List<string> { "internal", "extend" };

            _configStore.Save(config);
            _log.Info("[toggle] 已自动保存");
            ReloadAndReport("已自动保存");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 保存失败: {ex}");
            ShowStatus($"保存失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ---- 快捷键重置（恢复默认 F13 / F15） ----

    private void OnResetNotepadHotkey(object sender, RoutedEventArgs e) => ResetHotkey("notepad", "F13", NotepadHotkeyText);

    private void OnResetDisplayHotkey(object sender, RoutedEventArgs e) => ResetHotkey("display_mode", "F14", DisplayHotkeyText);

    private void ResetHotkey(string action, string defaultHotkey, TextBlock display)
    {
        try
        {
            var config = _configStore.Load();
            config.Bindings[action] = defaultHotkey;
            _configStore.Save(config);
            display.Text = FormatHotkeyText(config.Bindings, action);
            _log.Info($"[toggle] {action} 快捷键已重置为 {defaultHotkey}");
            ReloadAndReport($"已恢复默认快捷键 {defaultHotkey}");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 快捷键重置失败: {ex}");
            ShowStatus($"重置失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// 保存后热重载；有热键解析/注册失败时用警告条说明具体是哪个键、为什么失败，
    /// 否则用户只会看到"已保存"却按了没反应。
    /// </summary>
    private void ReloadAndReport(string savedMessage)
    {
        IReadOnlyList<string> failures = _onSaved();
        if (failures.Count == 0)
        {
            ShowStatus(savedMessage);
            return;
        }

        _log.Error($"[toggle] 热键未生效（{failures.Count} 个）: {string.Join("；", failures)}");
        ShowStatus($"{savedMessage}，但热键未生效：{string.Join("；", failures)}", InfoBarSeverity.Warning);
    }

    private void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
