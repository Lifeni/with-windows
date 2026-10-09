using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>
/// 投影页：功能开关、投影快捷键、当前模式，以及「大卡片」形式的候选模式选择。
/// 投影相关设置集中在这里（设置页不再重复）。
/// </summary>
public sealed partial class DisplayPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "display";

    /// <summary>候选模式的固定顺序、显示名、图标与说明；顺序即循环顺序。</summary>
    private static readonly (string Mode, string Title, string Glyph, string Description)[] KnownModes =
    {
        ("internal", "仅当前屏幕", "\uE7F4", "只在电脑屏幕显示"),
        ("extend", "扩展模式", "\uE7F8", "两块屏幕拼成一个桌面"),
        ("external", "仅外接屏幕", "\uE8B2", "只在投影仪或外接屏显示"),
        ("clone", "复制模式", "\uE8C4", "两块屏幕显示相同内容"),
    };

    private bool _initializing;

    public DisplayPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private static AppHost? Host => App.Host;

    private static ToolBinding? DisplayBinding
        => Host?.Config.Tools.FirstOrDefault(t => t.Id == Tools.ToolCatalog.DisplayModeId);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null || ModesHost.Children.Count > 0)
            return;

        _initializing = true;
        try
        {
            EnabledToggle.IsOn = DisplayBinding?.Enabled ?? true;
            BuildModeCards();
            RefreshCurrentMode();
            // 「立即执行」切换后刷新当前模式，避免标签与实际状态互相矛盾
            DisplayHotkeyRow.Executed += (_, _) => RefreshCurrentMode();
        }
        finally
        {
            _initializing = false;
        }
    }

    /// <summary>展示当前拓扑；查询失败时给出说明而不是空白。</summary>
    private void RefreshCurrentMode()
    {
        string? mode = DisplayTopology.GetCurrentMode();
        CurrentModeText.Text = mode is null
            ? "当前模式：无法读取"
            : $"当前模式：{KnownModes.FirstOrDefault(m => m.Mode == mode).Title ?? mode}";
    }

    private void BuildModeCards()
    {
        AppConfig config = Host!.Config;
        StackPanel[] hosts = { ModesHost, ModesHost2, ModesHost3, ModesHost4 };

        for (int i = 0; i < KnownModes.Length; i++)
        {
            (string mode, string title, string glyph, string description) = KnownModes[i];

            var card = new ToggleButton
            {
                IsChecked = config.DisplayMode.Modes.Contains(mode, StringComparer.Ordinal),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 88,
                Padding = new Thickness(16, 12, 16, 12),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            };
            AutomationProperties.SetAutomationId(card, $"ModeCard_{mode}");
            AutomationProperties.SetName(card, title);

            var check = new FontIcon
            {
                Glyph = "\uE73E",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = card.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
            header.Children.Add(check);

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(header);
            content.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            card.Content = content;

            string capturedMode = mode;
            card.Checked += (_, _) =>
            {
                check.Visibility = Visibility.Visible;
                OnModeToggled(capturedMode, isChecked: true, card);
            };
            card.Unchecked += (_, _) =>
            {
                check.Visibility = Visibility.Collapsed;
                OnModeToggled(capturedMode, isChecked: false, card);
            };

            hosts[i].Children.Add(card);
        }
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing || Host is null)
            return;

        ToolBinding? binding = DisplayBinding;
        if (binding is null)
            return;

        binding.Enabled = EnabledToggle.IsOn;
        Host.SaveConfig();
        Host.ReloadBindings();
    }

    private void OnModeToggled(string mode, bool isChecked, ToggleButton source)
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

        // 至少保留一项：会清空时回弹，且不改动配置
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
