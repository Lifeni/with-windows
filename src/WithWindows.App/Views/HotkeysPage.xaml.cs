using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Controls;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Services;
using WithWindows.Tools;

namespace WithWindows.Views;

/// <summary>
/// 快捷键页：每个可绑热键的工具一行（工具名 + 录制框 + 启用开关 + 立即执行）。
/// 行由 <see cref="ToolCatalog"/> 生成；保存即热重载，注册失败在该行就地提示。
/// </summary>
public sealed partial class HotkeysPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "hotkeys";

    private readonly Dictionary<string, InfoBar> _infos = new(StringComparer.Ordinal);

    public HotkeysPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private static AppHost? Host => App.Host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (RowsHost.Children.Count > 0 || Host is null)
            return;

        foreach (ToolEntry tool in ToolCatalog.Tools.Where(t => t.HasHotkey))
            RowsHost.Children.Add(BuildRow(tool));

        ReportFailures(Host.ReloadBindings());
    }

    private FrameworkElement BuildRow(ToolEntry tool)
    {
        AppConfig config = Host!.Config;
        ToolBinding? binding = config.Tools.FirstOrDefault(t => t.Id == tool.Id);

        var title = new TextBlock
        {
            Text = tool.Title,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var input = new HotkeyInputBox
        {
            HotkeyText = binding?.Hotkey,
            MinWidth = 220,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(input, $"HotkeyBox_{tool.Id}");

        var enabled = new ToggleSwitch
        {
            IsOn = binding?.Enabled ?? true,
            OnContent = "启用",
            OffContent = "停用",
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(enabled, $"HotkeyEnabled_{tool.Id}");

        var runNow = new Button
        {
            Content = "立即执行",
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(runNow, $"HotkeyRunNow_{tool.Id}");

        var info = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            Severity = InfoBarSeverity.Warning,
        };
        AutomationProperties.SetAutomationId(info, $"HotkeyInfo_{tool.Id}");
        _infos[tool.Id] = info;

        input.HotkeyChanged += (_, value) =>
        {
            if (binding is null)
                return;

            binding.Hotkey = value;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        enabled.Toggled += (_, _) =>
        {
            if (binding is null)
                return;

            binding.Enabled = enabled.IsOn;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        runNow.Click += (_, _) => RunNow(tool, info);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(input, 1);
        Grid.SetColumn(enabled, 2);
        Grid.SetColumn(runNow, 3);
        grid.Children.Add(title);
        grid.Children.Add(input);
        grid.Children.Add(enabled);
        grid.Children.Add(runNow);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(grid);
        content.Children.Add(info);

        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private void RunNow(ToolEntry tool, InfoBar info)
    {
        try
        {
            var router = new HotkeyRouter((MainWindow)App.MainWindow!, Host!.Config);
            ActionResult? result = router.Invoke(tool.Id);

            info.Severity = InfoBarSeverity.Success;
            info.Title = result?.Message ?? $"已打开{tool.Title}";
            info.IsOpen = true;
        }
        catch (Exception ex)
        {
            info.Severity = InfoBarSeverity.Error;
            info.Title = $"执行失败：{ex.Message}";
            info.IsOpen = true;
        }
    }

    /// <summary>把热重载失败原因显示到对应行；没有失败的行自动收起提示。</summary>
    private void ReportFailures(IReadOnlyList<string> failures)
    {
        foreach ((string toolId, InfoBar info) in _infos)
        {
            string? failure = failures.FirstOrDefault(f => f.StartsWith(toolId + "：", StringComparison.Ordinal));
            if (failure is null)
            {
                if (info.Severity != InfoBarSeverity.Success)
                    info.IsOpen = false;
                continue;
            }

            info.Severity = InfoBarSeverity.Warning;
            info.Title = failure;
            info.IsOpen = true;
        }
    }
}
