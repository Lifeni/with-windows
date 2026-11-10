using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using WithWindows.Core.Scheduling;

namespace WithWindows.Controls;

/// <summary>
/// Lua 脚本编辑对话框。脚本只会导入为应用内文本，不关联外部脚本文件。
/// </summary>
public static class ScheduledTaskEditorDialog
{
    public static async Task<ScheduledTask?> ShowAsync(
        XamlRoot xamlRoot,
        ScheduledTask? current)
    {
        DateTimeOffset localNow = DateTimeOffset.Now;
        DateTimeOffset initialRunAt = current?.Schedule.OneTimeRunAt?.ToLocalTime()
            ?? RoundUpToFiveMinutes(localNow.AddHours(1));

        var nameBox = new TextBox
        {
            Header = "脚本名称",
            PlaceholderText = "例如：每日备份",
            Text = current?.Name ?? "",
        };
        AutomationProperties.SetAutomationId(nameBox, "ScheduledTaskNameBox");
        AutomationProperties.SetName(nameBox, "脚本名称");

        var scheduleMode = new ComboBox
        {
            Header = "执行方式",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(
            scheduleMode,
            "ScheduledTaskScheduleTypeComboBox");
        AutomationProperties.SetName(scheduleMode, "执行方式");
        scheduleMode.Items.Add(new ComboBoxItem
        {
            Content = "仅手动运行",
            Tag = ScheduleKind.Manual,
        });
        scheduleMode.Items.Add(new ComboBoxItem
        {
            Content = "单次定时执行",
            Tag = ScheduleKind.Once,
        });
        scheduleMode.SelectedIndex =
            current?.Schedule.Kind == ScheduleKind.Once ? 1 : 0;

        var datePicker = new CalendarDatePicker
        {
            Date = new DateTimeOffset(initialRunAt.Date),
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
        };
        AutomationProperties.SetAutomationId(
            datePicker,
            "ScheduledTaskDatePicker");
        AutomationProperties.SetName(datePicker, "单次执行日期");

        var timePicker = new TimePicker
        {
            ClockIdentifier = "24HourClock",
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
            SelectedTime = initialRunAt.TimeOfDay,
        };
        timePicker.Resources["TimePickerHostPadding"] = new Thickness(0, 9, 0, 9);
        AutomationProperties.SetAutomationId(
            timePicker,
            "ScheduledTaskTimePicker");
        AutomationProperties.SetName(timePicker, "单次执行时间");

        var dateLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "日期",
        };
        var timeLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "时间",
        };

        var scheduleFields = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 4,
        };
        scheduleFields.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        scheduleFields.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        scheduleFields.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scheduleFields.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scheduleFields.Children.Add(dateLabel);
        Grid.SetColumn(timeLabel, 1);
        scheduleFields.Children.Add(timeLabel);
        Grid.SetRow(datePicker, 1);
        scheduleFields.Children.Add(datePicker);
        Grid.SetColumn(timePicker, 1);
        Grid.SetRow(timePicker, 1);
        scheduleFields.Children.Add(timePicker);

        var timeoutBox = new NumberBox
        {
            Header = "超时（秒）",
            Minimum = ScheduledTaskValidation.MinimumTimeoutSeconds,
            Maximum = ScheduledTaskValidation.MaximumTimeoutSeconds,
            SmallChange = 30,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = Math.Clamp(
                current?.TimeoutSeconds ?? 600,
                ScheduledTaskValidation.MinimumTimeoutSeconds,
                ScheduledTaskValidation.MaximumTimeoutSeconds),
        };
        AutomationProperties.SetAutomationId(timeoutBox, "ScheduledTaskTimeoutBox");
        AutomationProperties.SetName(timeoutBox, "脚本超时秒数");

        var scriptEditor = new TextBox
        {
            AcceptsReturn = true,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Height = 320,
            IsSpellCheckEnabled = false,
            PlaceholderText = "在此填写 Lua 脚本，或从 .lua 文件导入",
            Text = current?.ScriptText ?? "",
            TextWrapping = TextWrapping.NoWrap,
        };
        AutomationProperties.SetAutomationId(
            scriptEditor,
            "ScheduledTaskScriptEditor");
        AutomationProperties.SetName(scriptEditor, "Lua 脚本");
        ScrollViewer.SetHorizontalScrollBarVisibility(
            scriptEditor,
            ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(
            scriptEditor,
            ScrollBarVisibility.Auto);

        var scriptHeader = new Grid
        {
            ColumnSpacing = 12,
        };
        scriptHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scriptHeader.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        var scriptTitle = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = "Lua 脚本",
        };
        scriptHeader.Children.Add(scriptTitle);
        var importButton = new Button
        {
            Content = "导入 .lua",
        };
        AutomationProperties.SetAutomationId(importButton, "ScheduledTaskImportButton");
        AutomationProperties.SetName(importButton, "导入 Lua 脚本文件");
        Grid.SetColumn(importButton, 1);
        scriptHeader.Children.Add(importButton);
        var form = new StackPanel
        {
            Spacing = 12,
        };
        form.Children.Add(nameBox);
        form.Children.Add(scheduleMode);
        form.Children.Add(scheduleFields);
        form.Children.Add(scriptHeader);
        form.Children.Add(scriptEditor);
        form.Children.Add(timeoutBox);

        var validation = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(
            validation,
            "ScheduledTaskEditorValidation");
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);

        var scroll = new ScrollViewer
        {
            Content = form,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(
            scroll,
            "ScheduledTaskEditorScroll");

        var content = new Grid();
        content.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(scroll);
        Grid.SetRow(validation, 1);
        content.Children.Add(validation);

        void ShowValidation(string message)
        {
            validation.Text = message;
            validation.Visibility = Visibility.Visible;
        }

        importButton.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".lua");
                if (App.MainWindow is not { } window)
                    throw new InvalidOperationException("主窗口尚未就绪。");
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker,
                    WinRT.Interop.WindowNative.GetWindowHandle(window));
                StorageFile? file = await picker.PickSingleFileAsync();
                if (file is null)
                    return;

                scriptEditor.Text = await FileIO.ReadTextAsync(file);
                validation.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShowValidation($"导入脚本失败：{ex.Message}");
            }
        };

        void UpdateContentHeight()
            => scroll.MaxHeight = Math.Clamp(
                xamlRoot.Size.Height - 220,
                min: 280,
                max: 680);

        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentHeight();

        UpdateContentHeight();
        scheduleFields.Visibility = scheduleMode.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        scheduleMode.SelectionChanged += (_, _) =>
        {
            scheduleFields.Visibility = scheduleMode.SelectedIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;
            validation.Visibility = Visibility.Collapsed;
        };

        ScheduledTask? result = null;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = current is null ? "添加 Lua 脚本" : "编辑 Lua 脚本",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        AutomationProperties.SetAutomationId(
            dialog,
            "ScheduledTaskEditorDialog");

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (current is not null
                && App.Scheduler?.IsTaskRunning(current.Id) == true)
            {
                args.Cancel = true;
                ShowValidation("脚本正在运行，停止后才能保存修改。");
                return;
            }

            string name = nameBox.Text.Trim();
            string script = scriptEditor.Text.ReplaceLineEndings("\n");
            if (name.Length == 0)
            {
                args.Cancel = true;
                ShowValidation("请输入脚本名称。");
                return;
            }
            if (string.IsNullOrWhiteSpace(script))
            {
                args.Cancel = true;
                ShowValidation("请输入或导入 Lua 脚本。");
                return;
            }

            ScheduleKind kind =
                (scheduleMode.SelectedItem as ComboBoxItem)?.Tag as ScheduleKind?
                ?? ScheduleKind.Manual;
            DateTimeOffset? runAt = null;
            if (kind == ScheduleKind.Once)
            {
                if (datePicker.Date is not { } date
                    || timePicker.SelectedTime is not { } time)
                {
                    args.Cancel = true;
                    ShowValidation("请选择单次执行日期和时间。");
                    return;
                }

                DateTime local = DateTime.SpecifyKind(
                    date.Date.Add(time),
                    DateTimeKind.Unspecified);
                runAt = new DateTimeOffset(local);
                if (runAt <= DateTimeOffset.Now)
                {
                    args.Cancel = true;
                    ShowValidation("单次执行时间必须晚于当前时间。");
                    return;
                }
            }

            result = new ScheduledTask
            {
                Id = current?.Id ?? "",
                Name = name,
                ScheduleRevision = current?.ScheduleRevision ?? 1,
                LastHandledScheduledFor = current?.LastHandledScheduledFor,
                LastHandledRunId = current?.LastHandledRunId,
                Schedule = new TaskSchedule
                {
                    Kind = kind,
                    OneTimeRunAt = runAt,
                    OneTimeConsumedAt =
                        kind == ScheduleKind.Once
                        && current?.Schedule.Kind == ScheduleKind.Once
                        && current.Schedule.OneTimeRunAt == runAt
                            ? current.Schedule.OneTimeConsumedAt
                            : null,
                },
                ScriptText = script,
                TimeoutSeconds = (int)Math.Clamp(
                    double.IsNaN(timeoutBox.Value) ? 600 : timeoutBox.Value,
                    ScheduledTaskValidation.MinimumTimeoutSeconds,
                    ScheduledTaskValidation.MaximumTimeoutSeconds),
                CreatedAt = current?.CreatedAt
                    ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
        };

        ContentDialogResult shown;
        try
        {
            xamlRoot.Changed += OnXamlRootChanged;
            shown = await App.ShowDialogAsync(dialog);
        }
        finally
        {
            xamlRoot.Changed -= OnXamlRootChanged;
        }

        return shown == ContentDialogResult.Primary ? result : null;
    }

    private static DateTimeOffset RoundUpToFiveMinutes(DateTimeOffset value)
    {
        int remainder = value.Minute % 5;
        int minutes = remainder == 0 ? 5 : 5 - remainder;
        DateTime rounded = new(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            0,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(rounded.AddMinutes(minutes));
    }
}
