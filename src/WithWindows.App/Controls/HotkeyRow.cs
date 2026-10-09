using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    private readonly HotkeyChips _chips = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly FontIcon _editIcon = new() { Glyph = "\uE70F", FontSize = 14 };

    private readonly Button _edit = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 6, 10, 6),
    };

    private readonly CardRow _card = new();
    private ToolBinding? _binding;

    public HotkeyRow()
    {
        Build();
        Loaded += OnLoaded;
    }

    /// <summary>工具标识（对应配置 tools[].id），须在加入可视化树之前设置。</summary>
    public string ToolId { get; set; } = "";

    /// <summary>行标题。</summary>
    public string Title
    {
        get => _card.Title;
        set => _card.Title = value;
    }

    /// <summary>行说明。</summary>
    public string Description
    {
        get => _card.Description;
        set => _card.Description = value;
    }

    /// <summary>左侧图标字形。</summary>
    public string Glyph
    {
        get => _card.Glyph;
        set => _card.Glyph = value;
    }

    private static AppHost? Host => App.Host;

    private void Build()
    {
        // 与 PowerToys 一致：次按钮里放按键方块 + 铅笔图标，点击后弹对话框录制
        var editContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        editContent.Children.Add(_chips);
        editContent.Children.Add(_editIcon);
        _edit.Content = editContent;
        _edit.Click += async (_, _) => await EditHotkeyAsync();
        _card.Trailing = _edit;

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(_card);
        content.Children.Add(_info);
        Content = content;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null || string.IsNullOrEmpty(ToolId))
            return;

        _binding = Host.Config.Tools.FirstOrDefault(t => t.Id == ToolId);
        _chips.SetHotkey(_binding?.Hotkey);

        AutomationProperties.SetAutomationId(_edit, $"HotkeyBox_{ToolId}");
        AutomationProperties.SetName(_edit, "设置快捷键");
        AutomationProperties.SetAutomationId(_info, $"HotkeyInfo_{ToolId}");

        ReportFailures(Host.ReloadBindings());
    }

    /// <summary>打开录制对话框；确认后写回配置并热重载。</summary>
    private async Task EditHotkeyAsync()
    {
        if (_binding is null || Host is null)
            return;

        HotkeyDialogResult result = await HotkeyDialog.ShowAsync(XamlRoot, Title, _binding.Hotkey);
        if (!result.Confirmed)
            return;

        _binding.Hotkey = string.IsNullOrWhiteSpace(result.Hotkey) ? null : result.Hotkey;
        _chips.SetHotkey(_binding.Hotkey);
        Host.SaveConfig();
        ReportFailures(Host.ReloadBindings());
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
