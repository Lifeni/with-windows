using System;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using WithWindows.Core.Chat;
using MarkdigBlock = Markdig.Syntax.Block;
using MarkdigInline = Markdig.Syntax.Inlines.Inline;

namespace WithWindows.Controls;

/// <summary>把常见 Markdown 渲染为原生 WinUI 控件，不执行原始 HTML。</summary>
public sealed partial class MarkdownMessageView : UserControl
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .Build();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(MarkdownMessageView),
        new PropertyMetadata("", OnContentChanged));

    public static readonly DependencyProperty IsMarkdownProperty = DependencyProperty.Register(
        nameof(IsMarkdown),
        typeof(bool),
        typeof(MarkdownMessageView),
        new PropertyMetadata(false, OnContentChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsMarkdown
    {
        get => (bool)GetValue(IsMarkdownProperty);
        set => SetValue(IsMarkdownProperty, value);
    }

    public MarkdownMessageView()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Render();
        Render();
    }

    private static void OnContentChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
        => ((MarkdownMessageView)dependencyObject).Render();

    private void Render()
    {
        BlocksHost.Children.Clear();
        if (!IsMarkdown)
        {
            BlocksHost.Children.Add(new TextBlock
            {
                Text = Text,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
            return;
        }

        MarkdownDocument document = Markdown.Parse(Text, Pipeline);
        foreach (MarkdigBlock block in document)
            AddBlock(block);
    }

    private void AddBlock(MarkdigBlock block, int indent = 0)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RichTextBlock headingText = CreateParagraph(
                    heading,
                    24 - Math.Min(8, (heading.Level - 1) * 2));
                headingText.FontWeight = FontWeights.SemiBold;
                AddWithIndent(headingText, indent);
                break;

            case ParagraphBlock paragraph:
                AddWithIndent(CreateParagraph(paragraph, 14), indent);
                break;

            case ListBlock list:
                int index = 1;
                foreach (ListItemBlock item in list)
                {
                    string prefix = list.IsOrdered ? $"{index++}. " : "• ";
                    foreach (MarkdigBlock child in item)
                    {
                        if (child is ParagraphBlock itemParagraph)
                        {
                            AddWithIndent(
                                CreateParagraph(itemParagraph, 14, prefix),
                                indent + 1);
                        }
                        else
                        {
                            AddBlock(child, indent + 1);
                        }
                    }
                }
                break;

            case FencedCodeBlock fenced:
                AddCodeBlock(fenced.Lines.ToString(), indent);
                break;

            case CodeBlock code:
                AddCodeBlock(code.Lines.ToString(), indent);
                break;

            case QuoteBlock quote:
                var quoteBorder = new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(12, 4, 0, 4),
                    BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
                };
                var quotePanel = new StackPanel { Spacing = 6 };
                quoteBorder.Child = quotePanel;
                foreach (MarkdigBlock child in quote)
                {
                    if (child is ParagraphBlock quoteParagraph)
                        quotePanel.Children.Add(CreateParagraph(quoteParagraph, 14));
                }
                AddWithIndent(quoteBorder, indent);
                break;

            case ThematicBreakBlock:
                AddWithIndent(new Border
                {
                    Height = 1,
                    Background = ThemeBrush("CardStrokeColorDefaultBrush"),
                }, indent);
                break;

            case HtmlBlock html:
                AddWithIndent(new TextBlock
                {
                    Text = html.Lines.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                }, indent);
                break;
        }
    }

    private RichTextBlock CreateParagraph(
        LeafBlock block,
        double fontSize,
        string prefix = "")
    {
        var paragraph = new Paragraph();
        if (prefix.Length > 0)
            paragraph.Inlines.Add(new Run { Text = prefix });
        AddInlines(block.Inline, paragraph.Inlines);

        var richText = new RichTextBlock
        {
            FontSize = fontSize,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        richText.Blocks.Add(paragraph);
        return richText;
    }

    private void AddInlines(
        ContainerInline? source,
        InlineCollection target,
        bool bold = false,
        bool italic = false,
        bool strikethrough = false)
    {
        if (source is null)
            return;

        foreach (MarkdigInline inline in source)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(CreateRun(
                        literal.Content.ToString(),
                        bold,
                        italic,
                        strikethrough));
                    break;

                case EmphasisInline emphasis:
                    AddInlines(
                        emphasis,
                        target,
                        bold || emphasis.DelimiterCount >= 2,
                        italic || emphasis.DelimiterCount == 1,
                        strikethrough || emphasis.DelimiterChar == '~');
                    break;

                case CodeInline code:
                    target.Add(new Run
                    {
                        Text = code.Content,
                        FontFamily = new FontFamily("Consolas"),
                    });
                    break;

                case LinkInline link when MarkdownLinkPolicy.IsAllowed(link.Url):
                    target.Add(CreateHyperlink(GetInlineText(link), link.Url!, bold, italic));
                    break;

                case AutolinkInline autoLink
                    when MarkdownLinkPolicy.IsAllowed(autoLink.Url):
                    target.Add(CreateHyperlink(autoLink.Url, autoLink.Url, bold, italic));
                    break;

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;

                case HtmlInline html:
                    target.Add(CreateRun(html.Tag, bold, italic));
                    break;

                case ContainerInline container:
                    AddInlines(container, target, bold, italic, strikethrough);
                    break;
            }
        }
    }

    private static Hyperlink CreateHyperlink(
        string label,
        string url,
        bool bold,
        bool italic)
    {
        var hyperlink = new Hyperlink();
        hyperlink.Inlines.Add(CreateRun(label, bold, italic));
        hyperlink.Click += async (_, _) =>
        {
            if (!MarkdownLinkPolicy.IsAllowed(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return;
            }

            try
            {
                await Launcher.LaunchUriAsync(uri);
            }
            catch (Exception ex)
            {
                App.Host?.Log.Error($"打开 Markdown 链接失败：{ex.GetType().Name}");
            }
        };
        return hyperlink;
    }

    private static Run CreateRun(
        string text,
        bool bold,
        bool italic,
        bool strikethrough = false)
        => new()
        {
            Text = text,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            TextDecorations = strikethrough
                ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None,
        };

    private void AddCodeBlock(string text, int indent)
    {
        var codeText = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = codeText,
        };
        AddWithIndent(new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrush("LayerFillColorDefaultBrush"),
            Child = scroll,
        }, indent);
    }

    private void AddWithIndent(FrameworkElement element, int indent)
    {
        element.Margin = new Thickness(indent * 16, 0, 0, 0);
        BlocksHost.Children.Add(element);
    }

    private static string GetInlineText(ContainerInline? source)
    {
        if (source is null)
            return "";

        var builder = new StringBuilder();
        foreach (MarkdigInline inline in source)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content);
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case ContainerInline container:
                    builder.Append(GetInlineText(container));
                    break;
            }
        }
        return builder.ToString();
    }

    private static Brush ThemeBrush(string key)
        => (Brush)Application.Current.Resources[key];
}
