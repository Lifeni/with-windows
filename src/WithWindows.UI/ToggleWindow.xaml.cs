using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WithWindows.Config;
using WithWindows.Core;

namespace WithWindows;

/// <summary>
/// 一键切换配置窗口：亮暗（热键 + 日出日落自动切换）与屏幕（热键 + 勾选切换模式）。
/// 保存后写回配置并触发热重载（立即生效，无需重启）。
/// </summary>
public sealed partial class ToggleWindow : Window
{
    private readonly ConfigStore _configStore;
    private readonly Action _onSaved;
    private readonly Logger _log;
    private bool _sized;

    public ToggleWindow(ConfigStore configStore, Action onSaved, Logger log)
    {
        _configStore = configStore;
        _onSaved = onSaved;
        _log = log;
        InitializeComponent();
        LoadConfig();
    }

    public void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            AppWindow.Resize(new SizeInt32(560, 700));
            _sized = true;
        }
    }

    private void LoadConfig()
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

    private void OnAutoThemeToggled(object sender, RoutedEventArgs e)
        => AutoThemeOptions.Visibility = AutoThemeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

    private void OnSave(object sender, RoutedEventArgs e)
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

            // 勾选的模式作为 toggle 循环候选；全不选时回退默认 internal/extend
            var modes = new List<string>();
            if (ModeInternal.IsChecked == true) modes.Add("internal");
            if (ModeExtend.IsChecked == true) modes.Add("extend");
            if (ModeExternal.IsChecked == true) modes.Add("external");
            if (ModeClone.IsChecked == true) modes.Add("clone");
            config.DisplayMode.Modes = modes.Count > 0 ? modes : new List<string> { "internal", "extend" };

            _configStore.Save(config);
            _onSaved(); // 热重载：热键立即生效
            _log.Info("[toggle] 配置已保存并热重载");
        }
        catch (Exception ex)
        {
            _log.Error($"[toggle] 保存失败: {ex}");
        }
    }

    private static double? ParseNullableDouble(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
}
