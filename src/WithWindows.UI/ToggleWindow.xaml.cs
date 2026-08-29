using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;

namespace WithWindows;

/// <summary>
/// 一键切换配置窗口：亮暗（热键 + 立即切换 + 日出日落自动）与屏幕（热键 + 立即切换 + 勾选模式）。
/// 修改即自动保存并热重载；"恢复默认"一键还原。内容随窗口宽度自适应。
/// </summary>
public sealed partial class ToggleWindow : Window
{
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

        // 热键录制控件在代码中订阅（无 XAML 事件属性）
        ThemeHotkey.HotkeyChanged += (_, _) => AutoSave(notify: true);
        DisplayHotkey.HotkeyChanged += (_, _) => AutoSave(notify: true);

        LoadConfig();
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    public void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            AppWindow.Resize(new SizeInt32(560, 640));
            _sized = true;
        }
    }

    private void LoadConfig()
    {
        _loading = true;
        try
        {
            var config = _configStore.Load();

            ThemeHotkey.HotkeyText = config.Bindings.GetValueOrDefault("theme") ?? "";
            DisplayHotkey.HotkeyText = config.Bindings.GetValueOrDefault("display_mode") ?? "";

            AutoThemeToggle.IsOn = config.Theme.Enabled;
            ThemeLatitude.Text = config.Theme.Latitude?.ToString(CultureInfo.InvariantCulture) ?? "";
            ThemeLongitude.Text = config.Theme.Longitude?.ToString(CultureInfo.InvariantCulture) ?? "";
            ThemeSunrise.Text = config.Theme.Sunrise ?? "";
            ThemeSunset.Text = config.Theme.Sunset ?? "";
            ThemeOffset.Text = config.Theme.OffsetMinutes.ToString(CultureInfo.InvariantCulture);

            ModeInternal.IsChecked = config.DisplayMode.Modes.Contains("internal");
            ModeExtend.IsChecked = config.DisplayMode.Modes.Contains("extend");
            ModeExternal.IsChecked = config.DisplayMode.Modes.Contains("external");
            ModeClone.IsChecked = config.DisplayMode.Modes.Contains("clone");
        }
        finally
        {
            _loading = false;
        }
    }

    // ---- 自动保存 ----

    private void OnAutoThemeToggled(object sender, RoutedEventArgs e)
    {
        AutoThemeOptions.Visibility = AutoThemeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        AutoSave(notify: true);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e) => AutoSave(notify: true);

    private void OnAutoSaveTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start(); // 防抖
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

            config.Bindings["theme"] = ThemeHotkey.HotkeyText.Trim();
            config.Bindings["display_mode"] = DisplayHotkey.HotkeyText.Trim();

            config.Theme.Enabled = AutoThemeToggle.IsOn;
            config.Theme.Latitude = ParseNullableDouble(ThemeLatitude.Text);
            config.Theme.Longitude = ParseNullableDouble(ThemeLongitude.Text);
            config.Theme.Sunrise = string.IsNullOrWhiteSpace(ThemeSunrise.Text) ? null : ThemeSunrise.Text.Trim();
            config.Theme.Sunset = string.IsNullOrWhiteSpace(ThemeSunset.Text) ? null : ThemeSunset.Text.Trim();
            config.Theme.OffsetMinutes = int.TryParse(ThemeOffset.Text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int offset) ? offset : 0;

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

    // ---- 立即切换 ----

    private void OnToggleTheme(object sender, RoutedEventArgs e) => RunAction(() => _theme.Execute("toggle"));

    private void OnToggleDisplay(object sender, RoutedEventArgs e) => RunAction(() => _display.Execute("toggle"));

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

    private static double? ParseNullableDouble(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
}
