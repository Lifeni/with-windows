using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Services;

namespace WithWindows.Controls;

/// <summary>
/// 一条可绑热键的工具行：标题 + 录制框 + 启用开关 + 立即执行，注册失败就地提示。
/// 由工具所在页面直接使用（投屏页的「投屏」、设置页的「呼出主窗口」）。
/// </summary>
public sealed class HotkeyRow : UserControl
{
    private readonly InfoBar _info = new()
    {
        IsOpen = false,
        IsClosable = true,
        Severity = InfoBarSeverity.Warning,
    };

    private readonly HotkeyInputBox _box = new()
    {
        MinWidth = 160,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ToggleSwitch _enabled = new()
    {
        OnContent = "启用",
        OffContent = "停用",
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Button _run = new()
    {
        Content = "立即执行",
        VerticalAlignment = VerticalAlignment.Center,
    };

    private ToolBinding? _binding;

    public HotkeyRow()
    {
        Build();
        Loaded += OnLoaded;
    }

    /// <summary>工具标识（对应配置 tools[].id），须在加入可视化树之前设置。</summary>
    public string ToolId { get; set; } = "";

    /// <summary>行标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>「立即执行」按钮的文案。</summary>
    public string RunButtonText { get; set; } = "立即执行";

    private static AppHost? Host => App.Host;

    private void Build()
    {
        var title = new TextBlock { Text = Title, VerticalAlignment = VerticalAlignment.Center };
        _run.Content = RunButtonText;

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(_box, 1);
        Grid.SetColumn(_enabled, 2);
        Grid.SetColumn(_run, 3);
        grid.Children.Add(title);
        grid.Children.Add(_box);
        grid.Children.Add(_enabled);
        grid.Children.Add(_run);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(grid);
        content.Children.Add(_info);

        Content = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null || string.IsNullOrEmpty(ToolId))
            return;

        _binding = Host.Config.Tools.FirstOrDefault(t => t.Id == ToolId);
        _box.HotkeyText = _binding?.Hotkey;
        _enabled.IsOn = _binding?.Enabled ?? true;

        AutomationProperties.SetAutomationId(_box, $"HotkeyBox_{ToolId}");
        AutomationProperties.SetAutomationId(_enabled, $"HotkeyEnabled_{ToolId}");
        AutomationProperties.SetAutomationId(_run, $"HotkeyRunNow_{ToolId}");
        AutomationProperties.SetAutomationId(_info, $"HotkeyInfo_{ToolId}");

        _box.HotkeyChanged += (_, value) =>
        {
            if (_binding is null)
                return;

            _binding.Hotkey = value;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        _enabled.Toggled += (_, _) =>
        {
            if (_binding is null)
                return;

            _binding.Enabled = _enabled.IsOn;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        _run.Click += (_, _) => RunNow();

        ReportFailures(Host.ReloadBindings());
    }

    private void RunNow()
    {
        try
        {
            var router = new HotkeyRouter((MainWindow)App.MainWindow!, Host!.Config);
            ActionResult? result = router.Invoke(ToolId);

            _info.Severity = InfoBarSeverity.Success;
            _info.Title = result?.Message ?? $"已打开{Title}";
            _info.IsOpen = true;
        }
        catch (Exception ex)
        {
            _info.Severity = InfoBarSeverity.Error;
            _info.Title = $"执行失败：{ex.Message}";
            _info.IsOpen = true;
        }
    }

    /// <summary>把与本行工具相关的热重载失败原因显示出来；没有失败则收起提示。</summary>
    private void ReportFailures(IReadOnlyList<string> failures)
    {
        string? failure = failures.FirstOrDefault(f => f.StartsWith(ToolId + "：", StringComparison.Ordinal));
        if (failure is null)
        {
            if (_info.Severity != InfoBarSeverity.Success)
                _info.IsOpen = false;
            return;
        }

        _info.Severity = InfoBarSeverity.Warning;
        _info.Title = failure;
        _info.IsOpen = true;
    }
}
