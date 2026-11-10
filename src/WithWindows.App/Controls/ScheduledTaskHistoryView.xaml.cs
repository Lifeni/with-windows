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

/// <summary>运行记录列表与合并输出视图。</summary>
public sealed partial class ScheduledTaskHistoryView : UserControl
{
    public ScheduledTaskHistoryView()
    {
        InitializeComponent();
    }

    public ObservableCollection<ScheduledTaskRunRowViewModel> Runs { get; } = [];

    public event EventHandler<ScheduledTaskRunRowViewModel?>? SelectedRunChanged;

    public void FocusInitial()
    {
        if (Runs.Count > 0)
            RunList.Focus(FocusState.Programmatic);
        else
            OutputText.Focus(FocusState.Programmatic);
    }

    public void SetRuns(IReadOnlyList<ScheduledTaskRun> runs)
    {
        Runs.Clear();
        foreach (ScheduledTaskRun run in runs)
            Runs.Add(new ScheduledTaskRunRowViewModel(run));

        EmptyRuns.Visibility = Runs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResultsPanel.Visibility = Runs.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (Runs.Count > 0)
            RunList.SelectedIndex = 0;
        else
            ShowRun(null);
    }

    public void ShowRun(ScheduledTaskRunRowViewModel? run)
    {
        RunDetailsText.Text = run?.HeaderText ?? "暂无运行记录";
        OutputText.Blocks.Clear();
        EmptyOutput.Visibility = Visibility.Collapsed;
    }

    public void ShowOutput(IReadOnlyList<ScheduledTaskOutputLogEntry> entries)
    {
        OutputText.Blocks.Clear();
        foreach (ScheduledTaskOutputLogEntry entry in entries)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 2),
            };
            Symbol symbol = entry.Stream switch
            {
                ScheduledTaskOutputStream.StandardError => Symbol.Important,
                ScheduledTaskOutputStream.System => Symbol.Message,
                _ => Symbol.Play,
            };
            string colorKey = entry.Stream switch
            {
                ScheduledTaskOutputStream.StandardError =>
                    "SystemFillColorCriticalBrush",
                ScheduledTaskOutputStream.System =>
                    "TextFillColorSecondaryBrush",
                _ => "AccentTextFillColorPrimaryBrush",
            };
            Brush iconBrush = (Brush)Application.Current.Resources[colorKey];
            Brush textBrush =
                entry.Stream == ScheduledTaskOutputStream.StandardError
                    ? (Brush)Application.Current.Resources[
                        "SystemFillColorCriticalBrush"]
                    : (Brush)Application.Current.Resources[
                        "TextFillColorPrimaryBrush"];
            string streamName = entry.Stream switch
            {
                ScheduledTaskOutputStream.StandardError => "错误输出",
                ScheduledTaskOutputStream.System => "系统消息",
                _ => "标准输出",
            };
            var icon = new SymbolIcon(symbol)
            {
                Width = 14,
                Height = 14,
                Foreground = iconBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(icon, streamName);

            paragraph.Inlines.Add(new InlineUIContainer
            {
                Child = icon,
            });
            paragraph.Inlines.Add(new Run
            {
                Text = $"  {entry.Timestamp.ToLocalTime():HH:mm:ss.fff}  ",
                Foreground = (Brush)Application.Current.Resources[
                    "TextFillColorSecondaryBrush"],
            });
            paragraph.Inlines.Add(new Run
            {
                Text = entry.Text.Length == 0 ? " " : entry.Text,
                Foreground = textBrush,
            });
            OutputText.Blocks.Add(paragraph);
        }

        EmptyOutput.Visibility = entries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void ShowError(string message)
    {
        ShowOutput(
        [
            new ScheduledTaskOutputLogEntry(
                DateTimeOffset.Now,
                long.MaxValue,
                ScheduledTaskOutputStream.StandardError,
                message),
        ]);
    }

    private void OnRunSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        ScheduledTaskRunRowViewModel? run =
            RunList.SelectedItem as ScheduledTaskRunRowViewModel;
        ShowRun(run);
        SelectedRunChanged?.Invoke(this, run);
    }

    private void OnRunContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is not ScheduledTaskRunRowViewModel run
            || args.ItemContainer is not ListViewItem item)
        {
            return;
        }

        AutomationProperties.SetAutomationId(item, run.AutomationId);
        AutomationProperties.SetName(item, run.HeaderText);
    }
}
