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

/// <summary>只读运行日志对话框：左侧记录列表，右侧输出详情。</summary>
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
            ItemsSource = items,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
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
        runList.ItemContainerStyle = new Style(typeof(ListViewItem));
        runList.ItemContainerStyle.Setters.Add(
            new Setter(FrameworkElement.MinHeightProperty, 44d));
        runList.ItemContainerStyle.Setters.Add(
            new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));

        var summary = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = items.Count == 0
                ? "尚无运行记录"
                : $"{items.Count} 条运行记录",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetAutomationId(summary, "ScheduledTaskRunSummary");

        var leftTitle = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = "运行记录",
        };

        var leftHeader = new StackPanel
        {
            Spacing = 2,
        };
        leftHeader.Children.Add(leftTitle);
        leftHeader.Children.Add(summary);

        var leftEmpty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Visibility = items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed,
        };
        leftEmpty.Children.Add(new FontIcon
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 30,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Glyph = "\uE81C",
        });
        leftEmpty.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "运行一次脚本后，这里会显示记录。",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        var leftBody = new Grid();
        leftBody.Children.Add(runList);
        leftBody.Children.Add(leftEmpty);
        var leftGrid = new Grid
        {
            RowSpacing = 10,
        };
        leftGrid.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        leftGrid.Children.Add(leftHeader);
        Grid.SetRow(leftBody, 1);
        leftGrid.Children.Add(leftBody);

        var leftRows = new Border
        {
            Child = leftGrid,
            Padding = new Thickness(10),
            Background = (Brush)Application.Current.Resources[
                "LayerFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources[
                "CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };

        var detailTitle = new TextBlock
        {
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = items.Count == 0 ? "没有可查看的运行记录" : "正在读取运行记录…",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetAutomationId(
            detailTitle,
            "ScheduledTaskRunDetails");

        var detailMeta = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = items.Count == 0
                ? "运行一次 Lua 脚本后即可查看输出。"
                : "正在读取日志…",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(
            detailMeta,
            "ScheduledTaskRunMetadata");

        var detailHeader = new StackPanel
        {
            Spacing = 4,
        };
        detailHeader.Children.Add(detailTitle);
        detailHeader.Children.Add(detailMeta);

        var stdout = new TextBox
        {
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsReadOnly = true,
            PlaceholderText = "没有标准输出。",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(
            stdout,
            ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(stdout, "ScheduledTaskStdoutOutput");
        AutomationProperties.SetName(stdout, "标准输出");
        var stdoutHost = new Grid { MinHeight = 180 };
        stdoutHost.Children.Add(stdout);

        var stderr = new TextBox
        {
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsReadOnly = true,
            PlaceholderText = "没有错误输出。",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(
            stderr,
            ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(stderr, "ScheduledTaskStderrOutput");
        AutomationProperties.SetName(stderr, "错误输出");
        var stderrHost = new Grid { MinHeight = 180 };
        stderrHost.Children.Add(stderr);

        var stdoutTab = new TabViewItem
        {
            Content = stdoutHost,
            Header = "标准输出",
            IsClosable = false,
        };
        AutomationProperties.SetAutomationId(
            stdoutTab,
            "ScheduledTaskStdoutTab");
        var stderrTab = new TabViewItem
        {
            Content = stderrHost,
            Header = "错误输出",
            IsClosable = false,
        };
        AutomationProperties.SetAutomationId(
            stderrTab,
            "ScheduledTaskStderrTab");

        var outputTabs = new TabView
        {
            IsAddTabButtonVisible = false,
        };
        AutomationProperties.SetAutomationId(
            outputTabs,
            "ScheduledTaskOutputTabs");
        outputTabs.TabItems.Add(stdoutTab);
        outputTabs.TabItems.Add(stderrTab);

        var rightEmpty = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Visibility = items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed,
        };
        rightEmpty.Children.Add(new FontIcon
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 34,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Glyph = "\uE81C",
        });
        rightEmpty.Children.Add(new TextBlock
        {
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "还没有运行记录",
            TextAlignment = TextAlignment.Center,
        });
        outputTabs.Visibility = items.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        var rightBody = new Grid();
        rightBody.Children.Add(outputTabs);
        rightBody.Children.Add(rightEmpty);

        var detailPane = new Grid
        {
            RowSpacing = 12,
        };
        detailPane.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        detailPane.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        detailPane.Children.Add(detailHeader);
        Grid.SetRow(rightBody, 1);
        detailPane.Children.Add(rightBody);

        var content = new Grid
        {
            ColumnSpacing = 16,
        };
        content.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(190) });
        content.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(leftRows);
        Grid.SetColumn(detailPane, 1);
        content.Children.Add(detailPane);

        void UpdateContentSize()
        {
            content.Width = Math.Clamp(
                xamlRoot.Size.Width - 280,
                min: 420,
                max: 600);
            content.Height = Math.Clamp(
                xamlRoot.Size.Height - 340,
                min: 280,
                max: 380);
            double outputHeight = Math.Max(180, content.Height - 130);
            stdoutHost.MinHeight = outputHeight;
            stderrHost.MinHeight = outputHeight;
        }
        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentSize();
        UpdateContentSize();

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"“{task.Name}”运行日志",
            Content = content,
            CloseButtonText = "关闭",
        };
        dialog.Resources["ContentDialogMaxWidth"] = 660d;
        AutomationProperties.SetAutomationId(
            dialog,
            "ScheduledTaskHistoryDialog");

        bool closed = false;
        int outputGeneration = 0;
        if (items.Count > 0)
            runList.SelectedIndex = 0;

        dialog.Closed += (_, _) =>
        {
            closed = true;
            outputGeneration++;
        };

        async Task LoadSelectedRunAsync()
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

                detailTitle.Text = run.StartedText;
                detailMeta.Text = run.MetaText;
                stdout.Text = stdoutText;
                stderr.Text = string.IsNullOrWhiteSpace(run.ErrorMessage)
                    ? stderrText
                    : string.IsNullOrWhiteSpace(stderrText)
                        ? run.ErrorMessage
                        : stderrText + Environment.NewLine + run.ErrorMessage;
            }
            catch (Exception ex)
            {
                if (!closed && generation == outputGeneration)
                    stderr.Text = $"读取执行输出失败：{ex.Message}";
            }
        }

        runList.SelectionChanged += async (_, _) =>
        {
            await LoadSelectedRunAsync();
        };

        try
        {
            xamlRoot.Changed += OnXamlRootChanged;
            if (items.Count > 0)
                await LoadSelectedRunAsync();
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
