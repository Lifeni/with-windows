using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Scheduling;
using WithWindows.Views;

namespace WithWindows.Controls;

/// <summary>只读运行日志对话框：左侧记录列表，右侧合并输出。</summary>
public static class ScheduledTaskHistoryDialog
{
    private static readonly SemaphoreSlim PopupGate = new(1, 1);

    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        ScheduledTask task,
        IReadOnlyList<ScheduledTaskRun> runs)
    {
        await PopupGate.WaitAsync();
        try
        {
            await ShowCoreAsync(xamlRoot, task, runs);
        }
        finally
        {
            PopupGate.Release();
        }
    }

    private static async Task ShowCoreAsync(
        XamlRoot xamlRoot,
        ScheduledTask task,
        IReadOnlyList<ScheduledTaskRun> runs)
    {
        var view = new ScheduledTaskHistoryView();
        var title = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = $"“{task.Name}”运行日志",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var cardContent = new Grid
        {
            RowSpacing = 12,
        };
        cardContent.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        cardContent.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardContent.Children.Add(title);
        Grid.SetRow(view, 1);
        cardContent.Children.Add(view);

        var card = new Border
        {
            Background = (Brush)Application.Current.Resources[
                "ContentDialogBackground"],
            BorderBrush = (Brush)Application.Current.Resources[
                "ContentDialogBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24),
            Child = cardContent,
        };
        AutomationProperties.SetAutomationId(
            title,
            "ScheduledTaskHistoryDialog");

        var popup = new Popup
        {
            XamlRoot = xamlRoot,
            Child = card,
            IsLightDismissEnabled = true,
            LightDismissOverlayMode = LightDismissOverlayMode.On,
        };

        void UpdateContentSize()
        {
            card.Width = Math.Clamp(
                xamlRoot.Size.Width - 240,
                min: 460,
                max: 660);
            card.Height = Math.Clamp(
                xamlRoot.Size.Height - 220,
                min: 340,
                max: 500);
            popup.HorizontalOffset =
                Math.Max(0, (xamlRoot.Size.Width - card.Width) / 2);
            popup.VerticalOffset =
                Math.Max(0, (xamlRoot.Size.Height - card.Height) / 2);
        }

        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentSize();

        UpdateContentSize();

        bool closed = false;
        int outputGeneration = 0;
        async Task LoadSelectedRunAsync(
            ScheduledTaskRunRowViewModel? run)
        {
            int generation = ++outputGeneration;
            if (run is null)
                return;

            try
            {
                Task<string> stdoutTask = ReadOutputAsync(
                    run.TaskId,
                    run.StdoutLogPath);
                Task<string> stderrTask = ReadOutputAsync(
                    run.TaskId,
                    run.StderrLogPath);
                await Task.WhenAll(stdoutTask, stderrTask);
                if (closed || generation != outputGeneration)
                    return;

                DateTimeOffset fallback =
                    run.StartedAt ?? DateTimeOffset.Now;
                IReadOnlyList<ScheduledTaskOutputLogEntry> stdoutEntries =
                    LimitOutput(ScheduledTaskOutputLogCodec.Parse(
                        stdoutTask.Result,
                        ScheduledTaskOutputStream.StandardOutput,
                        fallback));
                IReadOnlyList<ScheduledTaskOutputLogEntry> stderrEntries =
                    LimitOutput(ScheduledTaskOutputLogCodec.Parse(
                        stderrTask.Result,
                        ScheduledTaskOutputStream.StandardError,
                        fallback));
                var entries = stdoutEntries
                    .Concat(stderrEntries)
                    .ToList();

                string? errorMessage = run.ErrorMessage;
                if (!string.IsNullOrWhiteSpace(errorMessage)
                    && !entries.Any(entry =>
                        entry.Stream == ScheduledTaskOutputStream.StandardError
                        && entry.Text.Contains(
                            errorMessage,
                            StringComparison.Ordinal)))
                {
                    entries.Add(new ScheduledTaskOutputLogEntry(
                        run.FinishedAt ?? DateTimeOffset.Now,
                        long.MaxValue,
                        ScheduledTaskOutputStream.StandardError,
                        errorMessage));
                }

                entries.Sort(static (left, right) =>
                {
                    int time = left.Timestamp.CompareTo(right.Timestamp);
                    return time != 0
                        ? time
                        : left.Sequence.CompareTo(right.Sequence);
                });
                view.ShowOutput(entries);
            }
            catch (Exception ex)
            {
                if (!closed && generation == outputGeneration)
                    view.ShowError($"读取运行输出失败：{ex.Message}");
            }
        }

        view.SelectedRunChanged += async (_, run) =>
        {
            await LoadSelectedRunAsync(run);
        };
        view.SetRuns(runs);
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        popup.Closed += (_, _) =>
        {
            closed = true;
            outputGeneration++;
            completion.TrySetResult(true);
        };

        try
        {
            xamlRoot.Changed += OnXamlRootChanged;
            popup.IsOpen = true;
            view.FocusInitial();
            await completion.Task;
        }
        finally
        {
            xamlRoot.Changed -= OnXamlRootChanged;
            popup.IsOpen = false;
        }
    }

    private static IReadOnlyList<ScheduledTaskOutputLogEntry> LimitOutput(
        IReadOnlyList<ScheduledTaskOutputLogEntry> entries)
    {
        const int maximumCharacters = 200_000;
        var limited = new List<ScheduledTaskOutputLogEntry>();
        int characters = 0;
        foreach (ScheduledTaskOutputLogEntry entry in entries)
        {
            if (characters + entry.Text.Length > maximumCharacters)
            {
                limited.Add(new ScheduledTaskOutputLogEntry(
                    entry.Timestamp,
                    long.MaxValue - 1,
                    entry.Stream,
                    "界面显示已截断。"));
                break;
            }

            limited.Add(entry);
            characters += entry.Text.Length;
        }

        return limited;
    }

    private static async Task<string> ReadOutputAsync(
        string taskId,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !PathSafety.IsWithinDirectory(
                path,
                System.IO.Path.Combine(
                    AppPaths.ScheduledTaskLogsDirectory,
                    taskId))
            || !File.Exists(path))
        {
            return "";
        }

        return await File.ReadAllTextAsync(path);
    }
}
