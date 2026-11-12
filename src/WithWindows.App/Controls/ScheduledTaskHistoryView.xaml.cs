using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Scheduling;
using WithWindows.Views;

namespace WithWindows.Controls;

/// <summary>单列折叠的运行历史与合并输出视图。</summary>
public sealed partial class ScheduledTaskHistoryView : UserControl
{
    private readonly Dictionary<string, Expander> _runExpanders =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunOutputPane> _outputPanes =
        new(StringComparer.Ordinal);
    private readonly ScheduledTaskRunExpansionState _expansionState = new();

    public ScheduledTaskHistoryView()
    {
        InitializeComponent();
    }

    public ObservableCollection<ScheduledTaskRunRowViewModel> Runs { get; } = [];

    public event EventHandler<ScheduledTaskRunRowViewModel>? RunExpanded;

    public void FocusInitial()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_expansionState.ExpandedRunId is not { } runId
                || !_runExpanders.TryGetValue(runId, out Expander? expander))
            {
                return;
            }

            expander.Focus(FocusState.Programmatic);
        });
    }

    public void SetRuns(IReadOnlyList<ScheduledTaskRun> runs)
    {
        var runIds = new List<string>(runs.Count);
        foreach (ScheduledTaskRun run in runs)
            runIds.Add(run.Id);

        _expansionState.Reset(runIds);
        _runExpanders.Clear();
        _outputPanes.Clear();
        Runs.Clear();
        foreach (ScheduledTaskRun run in runs)
            Runs.Add(new ScheduledTaskRunRowViewModel(run));

        bool hasRuns = Runs.Count > 0;
        RunScroll.Visibility = hasRuns
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyRuns.Visibility = hasRuns
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public void ShowOutput(
        string runId,
        IReadOnlyList<ScheduledTaskOutputLogEntry> entries)
    {
        if (!_runExpanders.TryGetValue(runId, out Expander? expander))
            return;

        RunOutputPane pane = EnsureOutputPane(runId, expander);
        pane.OutputText.Blocks.Clear();
        foreach (ScheduledTaskOutputLogEntry entry in entries)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 2),
            };
            Brush textBrush =
                entry.Stream switch
                {
                    ScheduledTaskOutputStream.StandardError =>
                        (Brush)Application.Current.Resources[
                            "SystemFillColorCriticalBrush"],
                    ScheduledTaskOutputStream.System =>
                        (Brush)Application.Current.Resources[
                            "TextFillColorSecondaryBrush"],
                    _ => (Brush)Application.Current.Resources[
                        "AccentTextFillColorPrimaryBrush"],
                };

            paragraph.Inlines.Add(new Run
            {
                Text = $"{entry.Timestamp.ToLocalTime():HH:mm:ss.fff}  ",
                Foreground = (Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"],
            });
            paragraph.Inlines.Add(new Run
            {
                Text = entry.Text.Length == 0 ? " " : entry.Text,
                Foreground = textBrush,
            });
            pane.OutputText.Blocks.Add(paragraph);
        }

        pane.EmptyOutput.Visibility = entries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void ShowError(string runId, string message)
    {
        ShowOutput(
            runId,
            [
                new ScheduledTaskOutputLogEntry(
                    DateTimeOffset.Now,
                    long.MaxValue,
                    ScheduledTaskOutputStream.StandardError,
                    message),
            ]);
    }

    private void OnRunExpanderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander
            || expander.Tag is not ScheduledTaskRunRowViewModel run)
        {
            return;
        }

        _runExpanders[run.Id] = expander;
        if (_expansionState.IsExpanded(run.Id)
            && !expander.IsExpanded)
        {
            expander.IsExpanded = true;
        }
    }

    private void OnRunExpanderUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander
            || expander.Tag is not ScheduledTaskRunRowViewModel run
            || !_runExpanders.TryGetValue(run.Id, out Expander? current)
            || !ReferenceEquals(current, expander))
        {
            return;
        }

        _runExpanders.Remove(run.Id);
        _outputPanes.Remove(run.Id);
    }

    private void OnRunExpanderExpanding(
        Expander sender,
        ExpanderExpandingEventArgs args)
    {
        if (sender.Tag is not ScheduledTaskRunRowViewModel run)
            return;

        _expansionState.SetExpanded(run.Id, expanded: true);
        EnsureOutputPane(run.Id, sender);
        RunExpanded?.Invoke(this, run);

        // Expanding 事件早于控件内部提交展开状态，延后滚动到新展开的记录。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_expansionState.IsExpanded(run.Id)
                || !sender.IsExpanded)
            {
                return;
            }

            sender.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0.08,
            });
        });
    }

    private void OnRunExpanderCollapsed(
        Expander sender,
        ExpanderCollapsedEventArgs args)
    {
        if (sender.Tag is ScheduledTaskRunRowViewModel run)
            _expansionState.SetExpanded(run.Id, expanded: false);
    }

    private RunOutputPane EnsureOutputPane(
        string runId,
        Expander expander)
    {
        if (_outputPanes.TryGetValue(runId, out RunOutputPane? pane))
            return pane;

        var outputText = new RichTextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 12,
            IsTextSelectionEnabled = true,
            Padding = new Thickness(10, 8, 10, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(
            outputText,
            $"ScheduledTaskRunOutput_{runId}");
        AutomationProperties.SetName(outputText, "运行输出");

        var emptyOutput = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources[
                "TextFillColorSecondaryBrush"],
            Text = "暂无输出",
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(
            emptyOutput,
            $"ScheduledTaskRunOutputEmpty_{runId}");
        AutomationProperties.SetName(emptyOutput, "运行输出为空");

        var host = new Grid
        {
            MinHeight = 28,
        };
        host.Children.Add(new ScrollViewer
        {
            Content = outputText,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        });
        host.Children.Add(emptyOutput);
        expander.Content = host;

        pane = new RunOutputPane(outputText, emptyOutput);
        _outputPanes[runId] = pane;
        return pane;
    }

    private sealed record RunOutputPane(
        RichTextBlock OutputText,
        TextBlock EmptyOutput);
}
