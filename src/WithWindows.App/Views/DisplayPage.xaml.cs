using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>
/// 投屏页：投屏快捷键（含启用开关与立即执行）、当前模式、候选模式。
/// 投屏相关设置集中在这里（设置页不再重复）。
/// </summary>
public sealed partial class DisplayPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "display";

    /// <summary>候选模式的固定顺序与显示名，顺序即切换循环顺序。</summary>
    private static readonly (string Mode, string Title)[] KnownModes =
    {
        ("internal", "仅当前屏幕"),
        ("extend", "扩展模式"),
        ("external", "仅外接屏幕"),
        ("clone", "复制模式"),
    };

    private bool _initializing;

    public DisplayPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private static AppHost? Host => App.Host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null || ModesHost.Children.Count > 0)
            return;

        _initializing = true;
        try
        {
            BuildModeCheckBoxes();
            RefreshCurrentMode();
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>展示当前拓扑；查询失败（或窗口未激活）时不显示。</summary>
    private void RefreshCurrentMode()
    {
        string? mode = DisplayTopology.GetCurrentMode();
        CurrentModeText.Text = mode is null
            ? "当前模式：无法读取"
            : $"当前模式：{KnownModes.FirstOrDefault(m => m.Mode == mode).Title ?? mode}";
    }

    private void BuildModeCheckBoxes()
    {
        AppConfig config = Host!.Config;
        foreach ((string mode, string title) in KnownModes)
        {
            var checkBox = new CheckBox
            {
                Content = title,
                IsChecked = config.DisplayMode.Modes.Contains(mode, StringComparer.Ordinal),
            };
            AutomationProperties.SetAutomationId(checkBox, $"ModeCheckBox_{mode}");

            string capturedMode = mode;
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
        var desired = new List<string>(config.DisplayMode.Modes);

        if (isChecked)
        {
            if (!desired.Contains(mode, StringComparer.Ordinal))
                desired.Add(mode);
        }
        else
        {
            desired.RemoveAll(m => string.Equals(m, mode, StringComparison.Ordinal));
        }

        // 至少保留一项：会清空时回弹复选框，且不改动配置
        if (desired.Count == 0)
        {
            _initializing = true;
            source.IsChecked = true;
            _initializing = false;
            return;
        }

        // 已知项按固定顺序排列（保证循环顺序稳定）；不在已知列表里的取值原样保留
        config.DisplayMode.Modes = KnownModes
            .Select(m => m.Mode)
            .Where(m => desired.Contains(m, StringComparer.Ordinal))
            .Concat(desired.Where(m => !KnownModes.Any(k => string.Equals(k.Mode, m, StringComparison.Ordinal))))
            .ToList();

        Host.SaveConfig();
        Host.ReloadBindings();
    }
}
