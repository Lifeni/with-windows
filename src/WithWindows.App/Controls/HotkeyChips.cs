using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WithWindows.Controls;

/// <summary>
/// 把热键文本渲染成一排按键方块（<c>Ctrl+Shift+F14</c> → 三个方块）；为空时显示占位文案。
/// </summary>
public sealed class HotkeyChips : StackPanel
{
    public HotkeyChips() => Orientation = Orientation.Horizontal;

    /// <summary>占位文案，默认「未设置」。</summary>
    public string Placeholder { get; set; } = "未设置";

    /// <summary>更新显示的热键文本；null / 空表示未绑定。</summary>
    public void SetHotkey(string? hotkey)
    {
        Children.Clear();

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            Children.Add(new TextBlock
            {
                Text = Placeholder,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            return;
        }

        string[] parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var chip = KeyChip.Create(parts[i], accent: true);
            chip.Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0);
            Children.Add(chip);
        }
    }
}
