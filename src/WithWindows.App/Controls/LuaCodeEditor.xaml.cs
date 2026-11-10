using System;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Scheduling;

namespace WithWindows.Controls;

/// <summary>轻量 Lua 编辑器：基础词法高亮与同步行号。</summary>
public sealed partial class LuaCodeEditor : UserControl
{
    private DispatcherQueueTimer? _highlightTimer;
    private ScrollViewer? _editorScroll;
    private bool _applyingFormatting;
    private bool _loaded;
    private bool _highlightPending;

    public LuaCodeEditor()
    {
        InitializeComponent();
    }

    public void SetText(string text)
    {
        _applyingFormatting = true;
        try
        {
            Editor.Document.SetText(TextSetOptions.None, text);
            ApplyParagraphMetrics();
        }
        finally
        {
            _applyingFormatting = false;
        }

        RefreshLineNumbers();
        ScheduleHighlight();
    }

    public string GetText()
    {
        return GetDocumentText().ReplaceLineEndings("\n");
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        _highlightTimer ??= CreateHighlightTimer();
        BindEditorScroll();
        if (_editorScroll is null)
            DispatcherQueue.TryEnqueue(BindEditorScroll);
        RefreshLineNumbers();
        if (_highlightPending)
            ScheduleHighlight();
        else
            ApplySyntaxHighlighting();
    }

    private void OnEditorUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        _highlightTimer?.Stop();
        if (_editorScroll is not null)
            _editorScroll.ViewChanged -= OnEditorScrollViewChanged;
        _editorScroll = null;
    }

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (_applyingFormatting)
            return;

        RefreshLineNumbers();
        ScheduleHighlight();
    }

    private DispatcherQueueTimer CreateHighlightTimer()
    {
        DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(80);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplySyntaxHighlighting();
        };
        return timer;
    }

    private void ScheduleHighlight()
    {
        _highlightPending = true;
        if (!_loaded)
            return;

        _highlightTimer ??= CreateHighlightTimer();
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    private void ApplySyntaxHighlighting()
    {
        if (!_loaded || _applyingFormatting)
            return;

        _highlightPending = false;
        _applyingFormatting = true;
        bool displayUpdatesBatched = false;
        try
        {
            string rawText = GetRawDocumentText();
            string text = TrimFinalParagraphMark(rawText);
            int selectionStart = Editor.Document.Selection.StartPosition;
            int selectionEnd = Editor.Document.Selection.EndPosition;
            Editor.Document.BatchDisplayUpdates();
            displayUpdatesBatched = true;

            var primaryColor =
                (Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush)?.Color
                ?? Microsoft.UI.Colors.White;
            var keywordColor =
                (Application.Current.Resources["SystemFillColorAttentionBrush"] as SolidColorBrush)?.Color
                ?? primaryColor;
            var builtinColor =
                (Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as SolidColorBrush)?.Color
                ?? keywordColor;
            var stringColor =
                (Application.Current.Resources["SystemFillColorSuccessBrush"] as SolidColorBrush)?.Color
                ?? primaryColor;
            var commentColor =
                (Application.Current.Resources["TextFillColorTertiaryBrush"] as SolidColorBrush)?.Color
                ?? primaryColor;
            var numberColor =
                (Application.Current.Resources["SystemFillColorCautionBrush"] as SolidColorBrush)?.Color
                ?? primaryColor;

            ITextRange fullRange = Editor.Document.GetRange(0, text.Length);
            ITextCharacterFormat baseFormat = fullRange.CharacterFormat;
            baseFormat.ForegroundColor = primaryColor;
            baseFormat.Bold = FormatEffect.Off;
            baseFormat.Italic = FormatEffect.Off;
            fullRange.CharacterFormat = baseFormat;

            foreach (LuaSyntaxSpan span in LuaSyntaxHighlighter.Parse(text))
            {
                ITextRange range = Editor.Document.GetRange(
                    span.Start,
                    span.Start + span.Length);
                ITextCharacterFormat format = range.CharacterFormat;
                format.ForegroundColor = span.Kind switch
                {
                    LuaSyntaxKind.Keyword => keywordColor,
                    LuaSyntaxKind.Builtin => builtinColor,
                    LuaSyntaxKind.String => stringColor,
                    LuaSyntaxKind.Comment => commentColor,
                    LuaSyntaxKind.Number => numberColor,
                    _ => primaryColor,
                };
                format.Bold = span.Kind == LuaSyntaxKind.Keyword
                    ? FormatEffect.On
                    : FormatEffect.Off;
                format.Italic = span.Kind == LuaSyntaxKind.Comment
                    ? FormatEffect.On
                    : FormatEffect.Off;
                range.CharacterFormat = format;
            }

            ApplyParagraphMetrics();
            Editor.Document.Selection.SetRange(
                Math.Min(selectionStart, rawText.Length),
                Math.Min(selectionEnd, rawText.Length));
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"Lua 编辑器高亮失败：{ex.Message}");
        }
        finally
        {
            if (displayUpdatesBatched)
                Editor.Document.ApplyDisplayUpdates();
            _applyingFormatting = false;
        }
    }

    private void ApplyParagraphMetrics()
    {
        string text = GetRawDocumentText();
        ITextRange range = Editor.Document.GetRange(0, text.Length);
        ITextParagraphFormat format = range.ParagraphFormat;
        format.SetLineSpacing(LineSpacingRule.Exactly, 15f);
        format.SpaceBefore = 0;
        format.SpaceAfter = 0;
        range.ParagraphFormat = format;
    }

    private string GetDocumentText()
        => TrimFinalParagraphMark(GetRawDocumentText());

    private string GetRawDocumentText()
    {
        Editor.Document.GetText(TextGetOptions.None, out string text);
        return text;
    }

    private static string TrimFinalParagraphMark(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
            return text[..^2];
        if (text.EndsWith('\r') || text.EndsWith('\n'))
            return text[..^1];
        return text;
    }

    private void RefreshLineNumbers()
    {
        string text = GetDocumentText().ReplaceLineEndings("\n");
        int lines = 1;
        foreach (char value in text)
        {
            if (value == '\n')
                lines++;
        }

        var builder = new StringBuilder(lines * 3);
        for (int line = 1; line <= lines; line++)
        {
            if (line > 1)
                builder.Append('\n');
            builder.Append(line);
        }

        LineNumbers.Text = builder.ToString();
    }

    private void BindEditorScroll()
    {
        if (_editorScroll is not null)
            return;

        _editorScroll = FindScrollViewer(Editor);
        if (_editorScroll is not null)
            _editorScroll.ViewChanged += OnEditorScrollViewChanged;
    }

    private void OnEditorScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_editorScroll is not null)
            GutterScroll.ChangeView(null, _editorScroll.VerticalOffset, null, true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
            return scrollViewer;

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (FindScrollViewer(child) is { } nested)
                return nested;
        }

        return null;
    }
}
