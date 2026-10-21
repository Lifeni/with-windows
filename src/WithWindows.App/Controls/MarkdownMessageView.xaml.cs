using System;
using System.Collections.Generic;
using System.Text;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
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
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseCustomContainers()
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

        string markdown = MarkdownPreprocessor.NormalizeDetails(Text);
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        foreach (MarkdigBlock block in document)
            AddBlock(block);
    }

    private void AddBlock(MarkdigBlock block, int indent = 0, Panel? target = null)
    {
        target ??= BlocksHost;
        switch (block)
        {
            case HeadingBlock heading:
                RichTextBlock headingText = CreateParagraph(
                    heading,
                    24 - Math.Min(8, (heading.Level - 1) * 2));
                headingText.FontWeight = FontWeights.SemiBold;
                AddWithIndent(headingText, indent, target);
                break;

            case ParagraphBlock paragraph:
                AddWithIndent(CreateParagraph(paragraph, 14), indent, target);
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
                            TaskList? task = FindTaskList(itemParagraph.Inline);
                            FrameworkElement content = task is null
                                ? CreateParagraph(itemParagraph, 14, prefix)
                                : CreateTaskItem(itemParagraph, task.Checked);
                            AddWithIndent(content, indent + 1, target);
                        }
                        else
                        {
                            AddBlock(child, indent + 1, target);
                        }
                    }
                }
                break;

            case Table table:
                AddWithIndent(CreateTable(table), indent, target);
                break;

            case CustomContainer container:
                AddWithIndent(CreateContainer(container), indent, target);
                break;

            case FencedCodeBlock fenced:
                AddCodeBlock(fenced.Lines.ToString(), indent, target);
                break;

            case CodeBlock code:
                AddCodeBlock(code.Lines.ToString(), indent, target);
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
                    AddBlock(child, target: quotePanel);
                AddWithIndent(quoteBorder, indent, target);
                break;

            case ThematicBreakBlock:
                AddWithIndent(new Border
                {
                    Height = 1,
                    Background = ThemeBrush("CardStrokeColorDefaultBrush"),
                }, indent, target);
                break;

            case HtmlBlock html:
                AddWithIndent(new TextBlock
                {
                    Text = html.Lines.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                }, indent, target);
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

    private FrameworkElement CreateTaskItem(ParagraphBlock paragraph, bool isChecked)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        panel.Children.Add(new CheckBox
        {
            IsChecked = isChecked,
            IsHitTestVisible = false,
            IsTabStop = false,
            MinWidth = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, -4, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        });
        panel.Children.Add(CreateParagraph(paragraph, 14));
        return panel;
    }

    private static TaskList? FindTaskList(ContainerInline? source)
    {
        if (source is null)
            return null;

        foreach (MarkdigInline inline in source)
        {
            if (inline is TaskList task)
                return task;
            if (inline is ContainerInline container
                && FindTaskList(container) is TaskList nested)
            {
                return nested;
            }
        }

        return null;
    }

    private FrameworkElement CreateTable(Table table)
    {
        var rows = new List<TableRow>();
        int columnCount = 0;
        foreach (MarkdigBlock child in table)
        {
            if (child is not TableRow row)
                continue;

            rows.Add(row);
            int cellsInRow = 0;
            foreach (MarkdigBlock cellBlock in row)
            {
                if (cellBlock is TableCell cell)
                    cellsInRow += Math.Max(1, cell.ColumnSpan);
            }
            columnCount = Math.Max(columnCount, cellsInRow);
        }

        columnCount = Math.Max(1, columnCount);
        var grid = new Grid();
        for (int column = 0; column < columnCount; column++)
        {
            TableColumnDefinition? definition =
                column < (table.ColumnDefinitions?.Count ?? 0)
                    ? table.ColumnDefinitions![column]
                    : null;
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = definition is { Width: > 0 }
                    ? new GridLength(definition.Width, GridUnitType.Star)
                    : new GridLength(1, GridUnitType.Star),
                MinWidth = 110,
            });
        }

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            TableRow row = rows[rowIndex];
            int cellIndex = 0;
            foreach (MarkdigBlock child in row)
            {
                if (child is not TableCell cell)
                    continue;

                var cellBorder = new Border
                {
                    Padding = new Thickness(8, 6, 8, 6),
                    Background = row.IsHeader
                        ? ThemeBrush("LayerFillColorDefaultBrush")
                        : ThemeBrush("CardBackgroundFillColorDefaultBrush"),
                    BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                };

                var content = new StackPanel { Spacing = 2 };
                foreach (MarkdigBlock cellChild in cell)
                {
                    if (cellChild is ParagraphBlock paragraph)
                    {
                        RichTextBlock text = CreateParagraph(paragraph, 13);
                        if (row.IsHeader)
                            text.FontWeight = FontWeights.SemiBold;
                        content.Children.Add(text);
                    }
                    else
                    {
                        AddBlock(cellChild, target: content);
                    }
                }

                cellBorder.Child = content;
                int column = Math.Clamp(cellIndex, 0, columnCount - 1);
                int span = Math.Clamp(
                    Math.Max(1, cell.ColumnSpan),
                    1,
                    columnCount - column);
                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, column);
                Grid.SetColumnSpan(cellBorder, span);
                grid.Children.Add(cellBorder);
                cellIndex += span;
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = new Border
            {
                BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1, 1, 0, 0),
                CornerRadius = new CornerRadius(6),
                Child = grid,
            },
        };
    }

    private FrameworkElement CreateContainer(CustomContainer container)
    {
        string title = container.Arguments?.Trim() ?? "";
        if (title.Length == 0)
            title = container.Info?.Trim() ?? "";
        if (title.Length == 0)
            title = "详情";

        var content = new StackPanel { Spacing = 6 };
        foreach (MarkdigBlock child in container)
            AddBlock(child, target: content);

        return new Expander
        {
            Header = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
            },
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = false,
        };
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

                case TaskList:
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

    private void AddCodeBlock(string text, int indent, Panel target)
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
        }, indent, target);
    }

    private static void AddWithIndent(FrameworkElement element, int indent, Panel target)
    {
        element.Margin = new Thickness(indent * 16, 0, 0, 0);
        target.Children.Add(element);
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
