using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WithWindows.Controls;

/// <summary>
/// 设置项卡片行：左侧图标 + 标题与说明，右侧承载一个控件（开关 / 下拉 / 按钮）。
/// 外观对齐系统记事本设置页的「卡片行」风格。
/// </summary>
public sealed class CardRow : UserControl
{
    private readonly FontIcon _icon = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
    private readonly TextBlock _description = new()
    {
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Grid _root;
    private readonly ContentPresenter _trailing = new() { VerticalAlignment = VerticalAlignment.Center };

    public CardRow()
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_description);

        // 没设说明文字时不留空行：空 TextBlock 会占一行高度，把标题挤到偏上、行也比别人高
        _description.Visibility = Visibility.Collapsed;

        _root = new Grid
        {
            ColumnSpacing = 16,
            Padding = new Thickness(16, 12, 16, 12),
            MinHeight = (double)Application.Current.Resources["SettingCardMinHeight"],
        };
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_icon, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_trailing, 2);
        _root.Children.Add(_icon);
        _root.Children.Add(text);
        _root.Children.Add(_trailing);

        Content = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = _root,
        };
    }

    /// <summary>左侧图标字形（Segoe Fluent Icons）。</summary>
    public string Glyph
    {
        get => _icon.Glyph;
        set => _icon.Glyph = value;
    }

    /// <summary>标题。</summary>
    public string Title
    {
        get => _title.Text;
        set
        {
            _title.Text = value;
            AutomationProperties.SetName(this, value);
        }
    }

    /// <summary>说明文字；为空时隐藏该行。</summary>
    public string Description
    {
        get => _description.Text;
        set
        {
            _description.Text = value;
            _description.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>右侧控件（开关 / 下拉 / 按钮）。</summary>
    public UIElement? Trailing
    {
        get => _trailing.Content as UIElement;
        set => _trailing.Content = value;
    }
}
