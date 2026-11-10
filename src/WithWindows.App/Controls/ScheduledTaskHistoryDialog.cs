using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Scheduling;
using WithWindows.Views;

namespace WithWindows.Controls;

/// <summary>只读执行历史对话框。</summary>
public static class ScheduledTaskHistoryDialog
{
    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        ScheduledTask task,
        IReadOnlyList<ScheduledTaskRun> runs)
    {
        var items = new ObservableCollection<ScheduledTaskRunRowViewModel>();
        foreach (ScheduledTaskRun run in runs)
            items.Add(new ScheduledTaskRunRowViewModel(run));

        var runList = new ListView
        {
            Height = 170,
            ItemsSource = items,
            SelectionMode = ListViewSelectionMode.Single,
        };
        AutomationProperties.SetAutomationId(
            runList,
            "ScheduledTaskRunHistoryList");
        AutomationProperties.SetName(runList, "脚本执行历史");
        runList.ContainerContentChanging += (_, args) =>
        {
            if (args.Item is ScheduledTaskRunRowViewModel run
                && args.ItemContainer is ListViewItem item)
            {
                AutomationProperties.SetAutomationId(item, run.AutomationId);
                AutomationProperties.SetName(item, run.DetailText);
            }
        };

        var summary = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = items.Count == 0
                ? "暂无执行记录。"
                : $"共 {items.Count} 条执行记录。",
        };
        AutomationProperties.SetAutomationId(summary, "ScheduledTaskRunSummary");

        var stdout = new TextBox
        {
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Header = "标准输出",
            Height = 130,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(stdout, "ScheduledTaskStdoutOutput");
        AutomationProperties.SetName(stdout, "标准输出");

        var stderr = new TextBox
        {
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Header = "错误输出",
            Height = 130,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(stderr, "ScheduledTaskStderrOutput");
        AutomationProperties.SetName(stderr, "错误输出");

        var form = new StackPanel
        {
            Spacing = 10,
        };
        form.Children.Add(summary);
        form.Children.Add(runList);
        form.Children.Add(stdout);
        form.Children.Add(stderr);

        var scroll = new ScrollViewer
        {
            Content = form,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
        void UpdateContentHeight()
            => scroll.MaxHeight = Math.Clamp(
                xamlRoot.Size.Height - 220,
                min: 320,
                max: 680);
        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentHeight();
        UpdateContentHeight();

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"“{task.Name}”执行历史",
            Content = scroll,
            CloseButtonText = "关闭",
        };
        AutomationProperties.SetAutomationId(
            dialog,
            "ScheduledTaskHistoryDialog");

        bool closed = false;
        int outputGeneration = 0;
        dialog.Closed += (_, _) =>
        {
            closed = true;
            outputGeneration++;
        };
        runList.SelectionChanged += async (_, _) =>
        {
            int generation = ++outputGeneration;
            try
            {
                if (runList.SelectedItem is not ScheduledTaskRunRowViewModel run)
                    return;

                string stdoutText = await ReadOutputAsync(
                    run.TaskId,
                    run.StdoutLogPath);
                string stderrText = await ReadOutputAsync(
                    run.TaskId,
                    run.StderrLogPath);
                if (closed || generation != outputGeneration)
                    return;

                stdout.Text = stdoutText;
                stderr.Text = string.IsNullOrWhiteSpace(run.ErrorMessage)
                    ? stderrText
                    : string.IsNullOrWhiteSpace(stderrText)
                        ? run.ErrorMessage
                        : stderrText + Environment.NewLine + run.ErrorMessage;
                summary.Text = $"{run.StartedText}・{run.StatusText}";
            }
            catch (Exception ex)
            {
                if (!closed && generation == outputGeneration)
                    stderr.Text = $"读取执行输出失败：{ex.Message}";
            }
        };

        try
        {
            xamlRoot.Changed += OnXamlRootChanged;
            await App.ShowDialogAsync(dialog);
        }
        finally
        {
            xamlRoot.Changed -= OnXamlRootChanged;
        }
    }

    private static async Task<string> ReadOutputAsync(
        string taskId,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !PathSafety.IsWithinDirectory(
                path,
                Path.Combine(AppPaths.ScheduledTaskLogsDirectory, taskId))
            || !File.Exists(path))
        {
            return "";
        }

        string text = await File.ReadAllTextAsync(path);
        return text.Length <= 200_000
            ? text
            : text[..200_000] + Environment.NewLine + "界面显示已截断。";
    }
}
