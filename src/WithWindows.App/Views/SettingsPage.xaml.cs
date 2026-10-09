using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Core.App;
using WithWindows.Core.Diagnostics;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>设置页：开机自启、呼出主窗口快捷键、关于（版本与路径入口）。</summary>
public sealed partial class SettingsPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "settings";

    /// <summary>初始化期间抑制写回，避免程序化赋值触发 Toggled。</summary>
    private bool _initializing;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private static AppHost? Host => App.Host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null)
            return;

        _initializing = true;
        try
        {
            AutoStartToggle.IsOn = AutoStart.IsEnabled();
            AutoStartStateText.Text = AutoStartToggle.IsOn ? "开" : "关";
            VersionText.Text = $"With Windows {CurrentVersion()}";
            BuildDateText.Text = $"构建日期 {BuildDate()}";

            // 未打包应用里 ms-appx:/// 解析不可靠（实测渲染成花屏），直接用绝对路径加载
            if (AppIcon.PngExists)
                AboutIconImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(AppIcon.PreferredPng));
        }
        finally
        {
            _initializing = false;
        }
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        try
        {
            if (AutoStartToggle.IsOn)
                AutoStart.Enable();
            else
                AutoStart.Disable();

            AutoStartStateText.Text = AutoStartToggle.IsOn ? "开" : "关";
            Host?.Log.Info($"开机自启已{(AutoStartToggle.IsOn ? "开启" : "关闭")}");
        }
        catch (Exception ex)
        {
            Host?.Log.Error($"开机自启设置失败：{ex.Message}");
        }
    }

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e) => OpenPath(AppPaths.DataRoot);

    private void OnOpenLogFileClick(object sender, RoutedEventArgs e) => OpenPath(AppPaths.LogPath, select: true);

    private void OnOpenRepositoryClick(object sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/Lifeni/with-windows");

    /// <summary>用系统默认浏览器打开网址。</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Host?.Log.Error($"打开链接失败（{url}）：{ex.Message}");
        }
    }

    /// <summary>用资源管理器打开目录（或选中文件）。失败只记日志，不打断界面。</summary>
    private static void OpenPath(string path, bool select = false)
    {
        try
        {
            string arguments = select ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Host?.Log.Error($"打开路径失败（{path}）：{ex.Message}");
        }
    }

    private static string CurrentVersion()
    {
        // 不用 InformationalVersion：它带 SourceLink 的提交哈希（如 0.5.0+b106fe3f…），对用户是噪声
        Version? version = typeof(SettingsPage).Assembly.GetName().Version;
        return version is null ? "未知" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>构建日期：由 csproj 在编译期写入 AssemblyMetadata。</summary>
    private static string BuildDate()
        => typeof(SettingsPage).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildDate")?.Value ?? "未知";
}
