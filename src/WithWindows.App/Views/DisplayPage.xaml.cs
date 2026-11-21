using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Theme;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>
/// 切换页：显示模式与 Windows 亮暗主题集中在一页配置。
/// 显示模式提供候选模式；主题提供日出日落自动切换与两套壁纸。
/// </summary>
public sealed partial class DisplayPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "display";

    private bool _initializing;

    public DisplayPage()
    {
        InitializeComponent();
        DisplayModesHint.Text = MinimumSelectionText;
        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private static AppHost? Host => App.Host;

    /// <summary>异步续体只能在页面仍挂载时更新控件；卸载后读 XamlRoot 也可能已经失效。</summary>
    private bool CanTouchUi()
        => IsLoaded && XamlRoot is not null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null)
            return;

        _initializing = true;
        try
        {
            if (ModesHost.Children.Count == 0)
                BuildModeCheckBoxes();
            UpdateModeAvailability();
            UpdateDisplaySubtitle();
            LoadThemeSettings();
        }
        finally
        {
            _initializing = false;
        }

    }

    /// <summary>显示分组标题下的状态：当前 Windows 显示拓扑。</summary>
    private void UpdateDisplaySubtitle()
    {
        string? current = DisplayTopology.GetCurrentMode();
        DisplayGroupSubtitleText.Text = current is null
            ? "当前：无法读取"
            : $"当前：{TitleOfMode(current)}";
    }

    /// <summary>模式标识 → 中文名；不在候选表里的取值原样显示。</summary>
    private static string TitleOfMode(string mode)
        => DisplayModes.DisplayName(mode);

    private static string MinimumSelectionText =>
        $"至少选择 {DisplayModeSelection.MinimumCount} 项；未勾选的模式不会进入切换循环。";

    private static string LockedSelectionText =>
        $"已选 {DisplayModeSelection.MinimumCount} 项；再勾选一项后即可调整当前选择。";

    /// <summary>把候选模式渲染成原生多选框。</summary>
    private void BuildModeCheckBoxes()
    {
        AppConfig config = Host!.Config;

        foreach (DisplayModeInfo mode in DisplayModes.All)
        {
            var checkBox = new CheckBox
            {
                IsChecked = config.DisplayMode.Modes.Contains(mode.Id, StringComparer.Ordinal),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 36,
                Margin = new Thickness(0, 2, 0, 2),
                Content = mode.DisplayName,
            };
            AutomationProperties.SetAutomationId(checkBox, $"ModeCheckBox_{mode.Id}");
            AutomationProperties.SetName(checkBox, mode.DisplayName);
            AutomationProperties.SetHelpText(checkBox, MinimumSelectionText);

            string capturedMode = mode.Id;
            checkBox.Checked += (_, _) => OnModeToggled(capturedMode, isChecked: true, checkBox);
            checkBox.Unchecked += (_, _) => OnModeToggled(capturedMode, isChecked: false, checkBox);

            ModesHost.Children.Add(checkBox);
        }
    }

    private void OnModeToggled(string mode, bool isChecked, CheckBox source)
    {
        if (_initializing || Host is null)
            return;

        AppConfig config = Host.Config;
        if (!DisplayModeSelection.TryChangeSelection(
                config.DisplayMode.Modes,
                mode,
                isChecked,
                out List<string> desired))
        {
            _initializing = true;
            source.IsChecked = !isChecked;
            _initializing = false;
            return;
        }

        config.DisplayMode.Modes = desired;

        UpdateModeAvailability();
        UpdateDisplaySubtitle();
        Host.SaveConfig();
        Host.ReloadBindings();
    }

    /// <summary>
    /// 恰好两项时锁定已选项，避免用户误以为可以直接取消；勾选第三项后恢复全部可操作。
    /// </summary>
    private void UpdateModeAvailability()
    {
        bool locked = Host is { } host
            && DisplayModeSelection.IsAtMinimum(host.Config.DisplayMode.Modes);
        foreach (CheckBox checkBox in ModesHost.Children.OfType<CheckBox>())
        {
            bool selected = checkBox.IsChecked == true;
            checkBox.IsEnabled = !locked || !selected;
            AutomationProperties.SetHelpText(
                checkBox,
                locked ? LockedSelectionText : MinimumSelectionText);
        }

        string hint = locked ? LockedSelectionText : MinimumSelectionText;
        if (!string.Equals(DisplayModesHint.Text, hint, StringComparison.Ordinal))
            DisplayModesHint.Text = hint;
    }

    private void LoadThemeSettings()
    {
        if (Host is null)
            return;

        ThemeScheduleToggle.IsOn = Host.Config.ThemeSwitch.ScheduleEnabled;

        UpdateThemeSubtitle();
        UpdateWallpaperStatus();
    }

    /// <summary>主题分组标题下的状态：当前亮暗主题与自动切换状态。</summary>
    private void UpdateThemeSubtitle()
    {
        ThemeMode current = ThemeToggleAction.CurrentModeOrDefault();
        ThemeGroupSubtitleText.Text =
            $"当前：{ThemeSwitchPolicy.DisplayName(current)} · {ScheduleStatusText(Host?.Config.ThemeSwitch)}";
    }

    private static string ScheduleStatusText(ThemeSwitchConfig? config)
    {
        if (config is null)
            return "定时状态未知";

        if (!config.ScheduleEnabled)
            return "自动切换已关闭";

        if (!config.TryGetCoordinates(out double latitude, out double longitude))
            return $"自动切换：定位不可用，默认 {ThemeScheduleDefaults.DisplayRange}";

        DateTimeOffset now = DateTimeOffset.Now;
        ThemeScheduleDay day = ThemeScheduleCalculator.GetDay(
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime),
            latitude,
            longitude,
            TimeZoneInfo.Local);
        if (day.AllDayMode is { } allDayMode)
        {
            return allDayMode == ThemeMode.Light
                ? "自动切换：极昼"
                : "自动切换：极夜";
        }

        if (day.Sunrise is not { } sunrise || day.Sunset is not { } sunset)
            return "自动切换：无法计算";

        string rise = TimeZoneInfo.ConvertTime(sunrise, TimeZoneInfo.Local).ToString("HH:mm");
        string set = TimeZoneInfo.ConvertTime(sunset, TimeZoneInfo.Local).ToString("HH:mm");
        return $"自动切换：日出 {rise} / 日落 {set}";
    }

    private async void OnScheduleToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || App.Host is not { IsShuttingDown: false } host)
            return;

        if (!ThemeScheduleToggle.IsOn)
        {
            host.Config.ThemeSwitch.ScheduleEnabled = false;
            host.SaveConfig();
            App.ThemeHost?.RefreshSchedule();
            CloseScheduleInfo();
            UpdateThemeSubtitle();
            return;
        }

        ThemeScheduleToggle.IsEnabled = false;
        try
        {
            ShowScheduleInfo(
                "正在获取当前位置…",
                InfoBarSeverity.Informational,
                "自动切换会按当前位置的日出日落安排亮暗主题，可能需要几秒钟。");

            ThemeLocationResult location = await ThemeLocationProvider.GetCurrentAsync();
            if (host.IsShuttingDown)
                return;

            ThemeSwitchConfig config = host.Config.ThemeSwitch;
            if (location.Success)
            {
                config.Latitude = location.Latitude;
                config.Longitude = location.Longitude;
                config.LocationUpdatedAt = DateTimeOffset.UtcNow;
                config.ScheduleEnabled = true;
                host.SaveConfig();
                App.ThemeHost?.RefreshSchedule();
                host.Log.Info(
                    $"自动主题：已保存当前位置 {location.Latitude:F4}, {location.Longitude:F4}");
                if (CanTouchUi())
                {
                    CloseScheduleInfo();
                    UpdateThemeSubtitle();
                }

                return;
            }

            config.ScheduleEnabled = true;
            config.Latitude = null;
            config.Longitude = null;
            config.LocationUpdatedAt = null;
            host.SaveConfig();
            App.ThemeHost?.RefreshSchedule();
            host.Log.Error(
                $"自动主题定位失败，改用默认时间 "
                + $"{ThemeScheduleDefaults.DisplayRange}：{location.Error ?? "未知原因"}");
            if (CanTouchUi())
            {
                ShowScheduleInfo(
                    "无法获取当前位置",
                    InfoBarSeverity.Warning,
                    $"已改为每天 {ThemeScheduleDefaults.DisplayRange} 自动切换亮暗主题。"
                    + $"{location.Error ?? "请检查 Windows 的位置权限和服务。"}");
                UpdateThemeSubtitle();
            }
        }
        catch (Exception ex)
        {
            if (host.IsShuttingDown)
                return;

            ThemeSwitchConfig fallback = host.Config.ThemeSwitch;
            fallback.ScheduleEnabled = true;
            fallback.Latitude = null;
            fallback.Longitude = null;
            fallback.LocationUpdatedAt = null;
            host.SaveConfig();
            App.ThemeHost?.RefreshSchedule();
            if (CanTouchUi())
            {
                ShowScheduleInfo(
                    "无法启用自动切换",
                    InfoBarSeverity.Warning,
                    $"定位时发生异常，已改为每天 "
                    + $"{ThemeScheduleDefaults.DisplayRange} 自动切换亮暗主题。{ex.Message}");
                UpdateThemeSubtitle();
            }

            host.Log.Error($"启用自动主题切换失败：{ex.Message}");
        }
        finally
        {
            if (CanTouchUi() && !host.IsShuttingDown)
            {
                ThemeScheduleToggle.IsEnabled = true;
                UpdateThemeSubtitle();
            }
        }
    }

    private void UpdateWallpaperStatus()
    {
        ThemeSwitchConfig? config = Host?.Config.ThemeSwitch;
        string lightPath = config?.LightWallpaper ?? "";
        string darkPath = config?.DarkWallpaper ?? "";

        UpdateWallpaperTile(
            LightWallpaperPreview,
            LightWallpaperPlaceholder,
            LightWallpaperName,
            lightPath);
        UpdateWallpaperTile(
            DarkWallpaperPreview,
            DarkWallpaperPlaceholder,
            DarkWallpaperName,
            darkPath);
        bool hasCustomWallpaper =
            !string.IsNullOrWhiteSpace(lightPath) || !string.IsNullOrWhiteSpace(darkPath);
        ThemeWallpaperResetButton.Visibility = hasCustomWallpaper
            ? Visibility.Visible
            : Visibility.Collapsed;
        ThemeWallpaperResetButton.IsEnabled = hasCustomWallpaper;
    }

    private void UpdateWallpaperTile(
        Image preview,
        FrameworkElement placeholder,
        TextBlock label,
        string path)
    {
        label.Text = WallpaperRowLabel(path);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            preview.Source = null;
            placeholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 720 };
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            preview.Source = bitmap;
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            preview.Source = null;
            placeholder.Visibility = Visibility.Visible;
            Host?.Log.Error($"读取壁纸预览失败：{ex.Message}");
        }
    }

    private static string WallpaperRowLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "未设置";

        string name = System.IO.Path.GetFileName(path);
        return File.Exists(path) ? name : name + "（文件不存在）";
    }

    private async void OnChooseLightWallpaper(object sender, RoutedEventArgs e)
        => await ChooseWallpaperAsync(isLight: true);

    private async void OnChooseDarkWallpaper(object sender, RoutedEventArgs e)
        => await ChooseWallpaperAsync(isLight: false);

    private async Task ChooseWallpaperAsync(bool isLight)
    {
        if (App.Host is not { IsShuttingDown: false } host || !CanTouchUi())
            return;

        string? importedPath = null;
        bool adopted = false;
        try
        {
            SetWallpaperButtonsEnabled(false);

            string? sourcePath = await PickWallpaperAsync();
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;

            // 选择器是跨进程模态窗口，等待期间用户仍可能用托盘菜单或热键切走页面。
            if (!CanTouchUi() || host.IsShuttingDown)
            {
                host.Log.Info("壁纸选择返回时页面已卸载，取消导入。");
                return;
            }

            string prefix = isLight ? "light" : "dark";
            importedPath = await Task.Run(() =>
                WallpaperStore.Import(
                    sourcePath,
                    AppPaths.WallpapersDirectory,
                    prefix));

            if (!CanTouchUi() || host.IsShuttingDown)
            {
                WallpaperStore.DeleteOwned(importedPath, AppPaths.WallpapersDirectory);
                host.Log.Info("壁纸导入完成时页面已卸载，已丢弃本次导入。");
                return;
            }

            ThemeSwitchConfig config = host.Config.ThemeSwitch;
            string? oldPath = isLight ? config.LightWallpaper : config.DarkWallpaper;
            if (isLight)
                config.LightWallpaper = importedPath;
            else
                config.DarkWallpaper = importedPath;
            host.SaveConfig();
            adopted = true;

            if (CanTouchUi())
            {
                CloseWallpaperError();
                UpdateWallpaperStatus();
            }
            WallpaperStore.DeleteOwned(oldPath, AppPaths.WallpapersDirectory);
            host.Log.Info(
                $"已设置{(isLight ? "亮色" : "暗色")}模式壁纸：{importedPath}");
        }
        catch (Exception ex)
        {
            if (!adopted && importedPath is not null)
                WallpaperStore.DeleteOwned(importedPath, AppPaths.WallpapersDirectory);

            if (CanTouchUi() && !host.IsShuttingDown)
                ShowWallpaperError($"选择壁纸失败：{ex.Message}");
            host.Log.Error($"选择{(isLight ? "亮色" : "暗色")}模式壁纸失败：{ex.Message}");
        }
        finally
        {
            if (CanTouchUi() && !host.IsShuttingDown)
                SetWallpaperButtonsEnabled(true);
        }
    }

    private void OnResetThemeWallpapers(object sender, RoutedEventArgs e)
    {
        if (Host is null)
            return;

        ThemeSwitchConfig config = Host.Config.ThemeSwitch;
        string? lightPath = config.LightWallpaper;
        string? darkPath = config.DarkWallpaper;
        if (string.IsNullOrWhiteSpace(lightPath) && string.IsNullOrWhiteSpace(darkPath))
            return;

        config.LightWallpaper = null;
        config.DarkWallpaper = null;
        Host.SaveConfig();
        CloseWallpaperError();
        UpdateWallpaperStatus();

        foreach (string path in new[] { lightPath, darkPath }
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(path => path!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            WallpaperStore.DeleteOwned(path, AppPaths.WallpapersDirectory);
        }

        Host.Log.Info("已恢复默认主题壁纸设置");
    }

    /// <summary>系统亮暗主题变化后，标题状态立即跟随实际主题。</summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (!_initializing && CanTouchUi())
            UpdateThemeSubtitle();
    }

    private static async Task<string?> PickWallpaperAsync()
    {
        if (App.MainWindow is not { } window)
            return null;

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".bmp");
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(window));

        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private void SetWallpaperButtonsEnabled(bool enabled)
    {
        LightWallpaperButton.IsEnabled = enabled;
        DarkWallpaperButton.IsEnabled = enabled;
        bool hasCustomWallpaper =
            !string.IsNullOrWhiteSpace(Host?.Config.ThemeSwitch.LightWallpaper) ||
            !string.IsNullOrWhiteSpace(Host?.Config.ThemeSwitch.DarkWallpaper);
        ThemeWallpaperResetButton.IsEnabled = enabled && hasCustomWallpaper;
    }

    private void ShowScheduleInfo(
        string title,
        InfoBarSeverity severity,
        string message)
    {
        ThemeScheduleInfo.Title = title;
        ThemeScheduleInfo.Message = message;
        ThemeScheduleInfo.Severity = severity;
        ThemeScheduleInfo.Visibility = Visibility.Visible;
        ThemeScheduleInfo.IsOpen = true;
        ThemeSwitchGroup.IsExpanded = true;
    }

    private void CloseScheduleInfo()
    {
        ThemeScheduleInfo.IsOpen = false;
        ThemeScheduleInfo.Visibility = Visibility.Collapsed;
    }

    private void OnScheduleInfoClosed(InfoBar sender, InfoBarClosedEventArgs args)
        => sender.Visibility = Visibility.Collapsed;

    private void ShowWallpaperError(string message)
    {
        ThemeWallpaperInfo.Title = message;
        ThemeWallpaperInfo.Visibility = Visibility.Visible;
        ThemeWallpaperInfo.IsOpen = true;
    }

    private void CloseWallpaperError()
    {
        ThemeWallpaperInfo.IsOpen = false;
        ThemeWallpaperInfo.Visibility = Visibility.Collapsed;
    }

    private void OnWallpaperInfoClosed(InfoBar sender, InfoBarClosedEventArgs args)
        => sender.Visibility = Visibility.Collapsed;

}
