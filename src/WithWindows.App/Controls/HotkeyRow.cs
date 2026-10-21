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
/// 一条可绑热键的工具行：标题 + 说明 + 快捷键按钮（按键方块 + 铅笔 → 弹窗录制），注册失败就地提示。
/// 由工具所在页面直接使用（投影页的「投影快捷键」、设置页的「呼出主窗口」）。
/// </summary>
public sealed class HotkeyRow : UserControl
{
    private readonly InfoBar _info = new()
    {
        Margin = new Thickness(0, 8, 0, 0),
        IsOpen = false,
        IsClosable = true,
        Severity = InfoBarSeverity.Warning,
        Visibility = Visibility.Collapsed,
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
        _info.Closed += (_, _) => _info.Visibility = Visibility.Collapsed;
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

    /// <summary>透传卡片圆角，供多行组合使用。</summary>
    public CornerRadius CardCornerRadius
    {
        get => _card.CardCornerRadius;
        set => _card.CardCornerRadius = value;
    }

    /// <summary>透传卡片边框厚度，供多行组合使用。</summary>
    public Thickness CardBorderThickness
    {
        get => _card.CardBorderThickness;
        set => _card.CardBorderThickness = value;
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

        // 间距放在 InfoBar 自身而不是 StackPanel：InfoBar 折叠时不能留下空白，
        // 否则与下一张卡片组合时会出现可见间隙。
        var content = new StackPanel { Spacing = 0 };
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
        UpdateEditAutomationName();
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
        UpdateEditAutomationName();
        Host.SaveConfig();
        ReportFailures(Host.ReloadBindings());
    }

    /// <summary>读屏名要带上当前绑定值：按键方块本身是 AccessibilityView.Raw，读屏只能从这里拿到。</summary>
    private void UpdateEditAutomationName()
        => AutomationProperties.SetName(_edit, $"设置快捷键，当前 {_binding?.Hotkey ?? "未设置"}");

    /// <summary>把与本行工具相关的热重载失败原因显示出来；没有失败则收起提示。</summary>
    private void ReportFailures(IReadOnlyList<string> failures)
    {
        string? failure = failures.FirstOrDefault(f => f.StartsWith(ToolId + "：", StringComparison.Ordinal));
        if (failure is null)
        {
            if (_info.Severity != InfoBarSeverity.Success)
            {
                _info.IsOpen = false;
                _info.Visibility = Visibility.Collapsed;
            }
            return;
        }

        _info.Severity = InfoBarSeverity.Warning;
        _info.Title = failure;
        _info.IsOpen = true;
        _info.Visibility = Visibility.Visible;
    }
}
