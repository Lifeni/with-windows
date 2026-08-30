using System.Globalization;
using System.Reflection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows;

/// <summary>
/// 设置窗口：两栏布局——左（亮暗/屏幕切换卡片、自动亮暗、模式勾选、恢复默认、开机自启、关于），
/// 右（三组快捷键 + AI 设置）。修改即自动保存并热重载。
/// </summary>
public sealed partial class ToggleWindow : Window
{
    private const string Unset = "未设置";

    private readonly ConfigStore _configStore;
    private readonly Action _onSaved;
    private readonly Logger _log;
    private readonly ThemeAction _theme = new();
    private readonly DisplayModeAction _display;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _autoSaveTimer;
    private bool _loading; // LoadConfig 期间屏蔽 AutoSave（避免回填触发保存）
    private bool _sized;

    public ToggleWindow(ConfigStore configStore, Action onSaved, Logger log)
    {
        _configStore = configStore;
        _onSaved = onSaved;
        _log = log;
        _display = new DisplayModeAction(configStore.Load().DisplayMode.Modes.ToArray());

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(3);
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusBar.IsOpen = false; };

        // 文本输入防抖保存（避免每次击键触发热重载）
        _autoSaveTimer = DispatcherQueue.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); SaveConfig(notify: false); };

        InitializeComponent();
        SetupTitleBar();
        AppWindow.Title = "设置";
        LoadConfig();
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 0, 0, 0x10 /* LR_LOADFROMFILE */);
        if (hIcon != IntPtr.Zero)
            AppWindow.SetIcon(new IconId((ulong)hIcon));
    }

    public void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            AppWindow.Resize(new SizeInt32(760, 640));
            _sized = true;
        }
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsResizable = false; // 固定尺寸
        RefreshStatusTexts();
    }

    private void LoadConfig()
    {
        _loading = true;
        try
        {
            var config = _configStore.Load();

            NotepadHotkeyText.Text = FormatHotkeyText(config.Bindings, "notepad");
            ThemeHotkeyText.Text = FormatHotkeyText(config.Bindings, "theme");
            DisplayHotkeyText.Text = FormatHotkeyText(config.Bindings, "display_mode");

            AutoThemeToggle.IsOn = config.Theme.Enabled;
            AutoStartToggle.IsOn = AutoStart.IsEnabled();

            AiBaseUrl.Text = config.Ai.BaseUrl;
            AiApiKey.Text = config.Ai.ApiKey;
            AiModel.Text = config.Ai.Model;

            ModeInternal.IsChecked = config.DisplayMode.Modes.Contains("internal");
            ModeExtend.IsChecked = config.DisplayMode.Modes.Contains("extend");
            ModeExternal.IsChecked = config.DisplayMode.Modes.Contains("external");
            ModeClone.IsChecked = config.DisplayMode.Modes.Contains("clone");

            VersionText.Text = $"版本 {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";
        }
        finally
        {
            _loading = false;
        }
        RefreshStatusTexts();
    }

    private static string FormatHotkeyText(Dictionary<string, string> bindings, string action)
        => string.IsNullOrWhiteSpace(bindings.GetValueOrDefault(action)) ? Unset : bindings[action];

    /// <summary>刷新两张大卡片的当前状态文字。</summary>
    private void RefreshStatusTexts()
    {
        ThemeStatusText.Text = ThemeAction.GetCurrentMode() switch
        {
            "light" => "当前：亮色",
            "dark" => "当前：暗色",
            _ => "当前：未知",
        };
        DisplayStatusText.Text = DisplayTopology.GetCurrentMode() switch
        {
            "internal" => "当前：仅当前屏幕",
            "extend" => "当前：扩展模式",
            "external" => "当前：仅外接屏幕",
            "clone" => "当前：复制模式",
            _ => "当前：未知",
        };
    }

    // ---- 大卡片切换 ----

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        RunAction(() => _theme.Execute("toggle"));
        RefreshStatusTexts();
    }

    private void OnToggleDisplay(object sender, RoutedEventArgs e)
    {
        RunAction(() => _display.Execute("toggle"));
        RefreshStatusTexts();
    }

    private void RunAction(Func<ActionResult> action)
    {
        try
        {
            var result = action();
            ShowStatus(result.Message);
            _log.Info($"[toggle] {result.Message}");
        }
        catch (Exception ex)
        {
            ShowStatus($"切换失败：{ex.Message}");
            _log.Error($"[toggle] 切换失败: {ex}");
        }
    }

    // ---- 快捷键设置（弹窗录制） ----

    private async void OnSetNotepadHotkey(object sender, RoutedEventArgs e)
        => await SetHotkey("notepad", "设置记事本快捷键", NotepadHotkeyText);

    private async void OnSetThemeHotkey(object sender, RoutedEventArgs e)
        => await SetHotkey("theme", "设置亮暗快捷键", ThemeHotkeyText);

    private async void OnSetDisplayHotkey(object sender, RoutedEventArgs e)
        => await SetHotkey("display_mode", "设置屏幕快捷键", DisplayHotkeyText);

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

    private void OnAutoThemeToggled(object sender, RoutedEventArgs e)
        => AutoSave(notify: true);

    private void OnModeChanged(object sender, RoutedEventArgs e) => AutoSave(notify: true);

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (AutoStartToggle.IsOn) AutoStart.Enable();
        else AutoStart.Disable();
        ShowStatus(AutoStartToggle.IsOn ? "开机自启已开启" : "开机自启已关闭");
        _log.Info($"[toggle] 开机自启 {(AutoStartToggle.IsOn ? "已启用" : "已停用")}");
    }

    private void AutoSave(bool notify)
    {
        if (_loading) return;
        SaveConfig(notify);
    }

    private void SaveConfig(bool notify)
    {
        try
        {
            var config = _configStore.Load();

            config.Theme.Enabled = AutoThemeToggle.IsOn;

            var modes = new List<string>();
            if (ModeInternal.IsChecked == true) modes.Add("internal");
            if (ModeExtend.IsChecked == true) modes.Add("extend");
            if (ModeExternal.IsChecked == true) modes.Add("external");
            if (ModeClone.IsChecked == true) modes.Add("clone");
            config.DisplayMode.Modes = modes.Count > 0 ? modes : new List<string> { "internal", "extend" };

            _configStore.Save(config);
            _onSaved(); // 热重载：热键立即生效
            _log.Info("[toggle] 已自动保存");
            if (notify) ShowStatus("已自动保存");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 保存失败: {ex}");
            ShowStatus($"保存失败：{ex.Message}");
        }
    }

    // ---- AI 设置 ----

    private void OnSaveAiConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = _configStore.Load();
            config.Ai.BaseUrl = AiBaseUrl.Text.Trim();
            config.Ai.ApiKey = AiApiKey.Text.Trim();
            config.Ai.Model = AiModel.Text.Trim();
            _configStore.Save(config);
            _log.Info("[ai] 配置已保存");
            ShowStatus("AI 配置已保存");
        }
        catch (Exception ex)
        {
            _log.Error($"[ai] 配置保存失败: {ex}");
            ShowStatus($"保存失败：{ex.Message}");
        }
    }

    // ---- 恢复默认 ----

    private async void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "恢复默认",
            Content = "将恢复默认热键（F13 / F14 / F15）与默认设置，当前修改会被覆盖。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _configStore.Save(new AppConfig());
        _onSaved();
        LoadConfig();
        ShowStatus("已恢复默认设置");
        _log.Info("[toggle] 已恢复默认设置");
    }

    private void ShowStatus(string message)
    {
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
