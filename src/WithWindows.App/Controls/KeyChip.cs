using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WithWindows.Controls;

/// <summary>
/// 单个按键方块（PowerToys 的 KeyVisual 同款思路）：圆角小方块里放一个键名，如 Win / Ctrl / F14。
/// Border 是密封类，因此做成工厂方法。
/// </summary>
public static class KeyChip
{
    public static Border Create(string text, bool accent)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            MinWidth = 32,
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources[
                accent ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush"],
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources[
                    accent ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush"],
            },
        };

        AutomationProperties.SetAccessibilityView(chip, AccessibilityView.Raw);
        return chip;
    }
}
