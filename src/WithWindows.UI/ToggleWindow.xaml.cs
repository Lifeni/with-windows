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

    private readonly ConfigStore _configStore;
    private readonly Action _onSaved;
    private readonly Logger _log;
    private readonly DispatcherQueueTimer _statusTimer;
    private bool _loading; // LoadConfig 期间屏蔽 AutoSave（避免回填触发保存）
    private bool _sized;

    public ToggleWindow(ConfigStore configStore, Action onSaved, Logger log)
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
        // 窗口类背景画刷 = 内容同色，显示瞬间即灰色无黑框
        NativeMethods.SetClassLongPtr(WinRT.Interop.WindowNative.GetWindowHandle(this), -10 /* GCLP_HBRBACKGROUND */,
            NativeMethods.CreateSolidBrush(0x00302B2B)); // RGB(0x2B, 0x2B, 0x30)
        // 恢复 Win11 窗口过渡动画（WinUI 3 可能默认禁用）
        int enabled = 0;
        NativeMethods.DwmSetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(this), 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref enabled, sizeof(int));

        string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        // 显式 32x32 帧：避免系统默认尺寸取帧不一致导致拉伸/模糊
        IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 32, 32, 0x10 /* LR_LOADFROMFILE */);
        if (hIcon != IntPtr.Zero)
            AppWindow.SetIcon(new IconId((ulong)hIcon));
    }

    public void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            LoadWindowState(); // 首次恢复记忆的尺寸/位置
            _sized = true;
        }
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsResizable = false; // 固定尺寸
    }

    /// <summary>恢复记忆的窗口尺寸/位置。</summary>
    private void LoadWindowState()
    {
        try
        {
            var ws = _configStore.Load().WindowState;
            AppWindow.Resize(new SizeInt32((int)ws.SettingsWidth, (int)ws.SettingsHeight));
            if (ws.SettingsX != 0 || ws.SettingsY != 0)
                AppWindow.Move(new PointInt32((int)ws.SettingsX, (int)ws.SettingsY));
        }
        catch (Exception ex)
        {
            _log.Error($"窗口状态恢复失败: {ex}");
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
            _onSaved(); // 热重载
            display.Text = FormatHotkeyText(config.Bindings, action);
            ShowStatus("快捷键已更新");
            _log.Info($"[toggle] {action} 快捷键已更新");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 快捷键保存失败: {ex}");
            ShowStatus($"保存失败：{ex.Message}");
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
            _onSaved(); // 热重载：热键立即生效
            _log.Info("[toggle] 已自动保存");
            ShowStatus("已自动保存");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 保存失败: {ex}");
            ShowStatus($"保存失败：{ex.Message}");
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
            _onSaved();
            display.Text = FormatHotkeyText(config.Bindings, action);
            ShowStatus($"已恢复默认快捷键 {defaultHotkey}");
            _log.Info($"[toggle] {action} 快捷键已重置为 {defaultHotkey}");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 快捷键重置失败: {ex}");
            ShowStatus($"重置失败：{ex.Message}");
        }
    }

    private void ShowStatus(string message)
    {
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
