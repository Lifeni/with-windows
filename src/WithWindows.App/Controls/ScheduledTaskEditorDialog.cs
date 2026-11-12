using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WithWindows.Core.Scheduling;

namespace WithWindows.Controls;

/// <summary>
/// C# 脚本编辑对话框。脚本只保存在应用内，不关联外部脚本文件。
/// </summary>
public static class ScheduledTaskEditorDialog
{
    public static async Task<ScheduledTask?> ShowAsync(
        XamlRoot xamlRoot,
        ScheduledTask? current)
    {
        DateTimeOffset localNow = DateTimeOffset.Now;
        DateTimeOffset defaultRunAt = current?.Schedule.OneTimeRunAt?.ToLocalTime()
            ?? RoundUpToFiveMinutes(localNow.AddHours(1));
        TimeSpan initialTime = current?.Schedule.TimeOfDay?.ToTimeSpan()
            ?? defaultRunAt.TimeOfDay;
        DateTimeOffset initialRunAt = current?.Schedule.Kind == ScheduleKind.Once
            ? defaultRunAt
            : new DateTimeOffset(DateTime.Today.Add(initialTime));

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
        scheduleMode.Items.Add(new ComboBoxItem
        {
            Content = "每天",
            Tag = ScheduleKind.Daily,
        });
        scheduleMode.Items.Add(new ComboBoxItem
        {
            Content = "每周",
            Tag = ScheduleKind.Weekly,
        });
        scheduleMode.Items.Add(new ComboBoxItem
        {
            Content = "每月",
            Tag = ScheduleKind.Monthly,
        });
        scheduleMode.SelectedIndex = current?.Schedule.Kind switch
        {
            ScheduleKind.Once => 1,
            ScheduleKind.Daily => 2,
            ScheduleKind.Weekly => 3,
            ScheduleKind.Monthly => 4,
            _ => 0,
        };

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
        datePicker.Transitions.Add(new EntranceThemeTransition());

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
        timePicker.Transitions.Add(new EntranceThemeTransition());

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
        var monthlyDateLabel = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "每月日期",
            Visibility = Visibility.Collapsed,
        };

        var scheduleFields = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 8,
        };
        scheduleFields.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        scheduleFields.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        scheduleFields.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        scheduleFields.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scheduleFields.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        scheduleFields.ChildrenTransitions.Add(new RepositionThemeTransition());
        scheduleFields.Children.Add(dateLabel);
        Grid.SetColumn(timeLabel, 1);
        scheduleFields.Children.Add(timeLabel);
        Grid.SetColumn(monthlyDateLabel, 1);
        scheduleFields.Children.Add(monthlyDateLabel);
        Grid.SetRow(datePicker, 1);
        scheduleFields.Children.Add(datePicker);
        Grid.SetColumn(timePicker, 1);
        Grid.SetRow(timePicker, 1);
        scheduleFields.Children.Add(timePicker);

        ToggleButton CreateWeekday(
            string text,
            DayOfWeek day,
            string automationId)
        {
            var toggleButton = new ToggleButton
            {
                Content = text,
                Height = 36,
                MinWidth = 0,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsChecked = current?.Schedule.DaysOfWeek.Contains(day) == true,
            };
            AutomationProperties.SetAutomationId(toggleButton, automationId);
            AutomationProperties.SetName(toggleButton, ScheduledTaskDisplayFormatter.WeekdayText(day));
            return toggleButton;
        }

        ToggleButton mondayCheck = CreateWeekday(
            "一",
            DayOfWeek.Monday,
            "ScheduledTaskWeekdayMondayCheckbox");
        ToggleButton tuesdayCheck = CreateWeekday(
            "二",
            DayOfWeek.Tuesday,
            "ScheduledTaskWeekdayTuesdayCheckbox");
        ToggleButton wednesdayCheck = CreateWeekday(
            "三",
            DayOfWeek.Wednesday,
            "ScheduledTaskWeekdayWednesdayCheckbox");
        ToggleButton thursdayCheck = CreateWeekday(
            "四",
            DayOfWeek.Thursday,
            "ScheduledTaskWeekdayThursdayCheckbox");
        ToggleButton fridayCheck = CreateWeekday(
            "五",
            DayOfWeek.Friday,
            "ScheduledTaskWeekdayFridayCheckbox");
        ToggleButton saturdayCheck = CreateWeekday(
            "六",
            DayOfWeek.Saturday,
            "ScheduledTaskWeekdaySaturdayCheckbox");
        ToggleButton sundayCheck = CreateWeekday(
            "日",
            DayOfWeek.Sunday,
            "ScheduledTaskWeekdaySundayCheckbox");
        var weekdayPanel = new Grid
        {
            ColumnSpacing = 4,
        };
        AutomationProperties.SetAutomationId(
            weekdayPanel,
            "ScheduledTaskWeekdaySelector");
        for (int index = 0; index < 7; index++)
        {
            weekdayPanel.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                });
        }
        int weekdayIndex = 0;
        foreach (ToggleButton toggleButton in new[]
        {
            mondayCheck,
            tuesdayCheck,
            wednesdayCheck,
            thursdayCheck,
            fridayCheck,
            saturdayCheck,
            sundayCheck,
        })
        {
            Grid.SetColumn(toggleButton, weekdayIndex++);
            weekdayPanel.Children.Add(toggleButton);
        }

        var weekdaySection = new StackPanel
        {
            Spacing = 6,
            Visibility = Visibility.Collapsed,
        };
        weekdaySection.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "星期",
        });
        weekdaySection.Children.Add(weekdayPanel);
        weekdaySection.Transitions.Add(new EntranceThemeTransition());

        var dayOfMonthBox = new NumberBox
        {
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Minimum = 1,
            Maximum = 31,
            MinWidth = 0,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = Math.Clamp(
                current?.Schedule.DayOfMonth ?? 1,
                1,
                31),
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(
            dayOfMonthBox,
            "ScheduledTaskDayOfMonthBox");
        AutomationProperties.SetName(dayOfMonthBox, "每月执行日期");
        dayOfMonthBox.Transitions.Add(new EntranceThemeTransition());
        Grid.SetColumn(dayOfMonthBox, 1);
        Grid.SetRow(dayOfMonthBox, 1);
        scheduleFields.Children.Add(dayOfMonthBox);
        Grid.SetColumnSpan(weekdaySection, 2);
        Grid.SetRow(weekdaySection, 2);
        scheduleFields.Children.Add(weekdaySection);

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

        var scriptEditor = new CSharpCodeEditor
        {
            Height = 260,
        };
        scriptEditor.SetText(current?.ScriptText ?? "");
        var scriptTitle = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = "C# 脚本",
        };
        var form = new StackPanel
        {
            Spacing = 12,
        };
        form.ChildrenTransitions.Add(
            new EntranceThemeTransition
            {
                IsStaggeringEnabled = true,
            });
        var schedulePanel = new StackPanel
        {
            Spacing = 8,
        };
        schedulePanel.Children.Add(scheduleMode);
        schedulePanel.Children.Add(scheduleFields);
        form.Children.Add(nameBox);
        form.Children.Add(scriptTitle);
        form.Children.Add(scriptEditor);
        form.Children.Add(schedulePanel);
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

        void UpdateContentHeight()
        {
            scroll.MaxHeight = Math.Clamp(
                xamlRoot.Size.Height - 340,
                min: 240,
                max: 440);
            content.Width = Math.Clamp(
                xamlRoot.Size.Width - 280,
                min: 420,
                max: 600);
        }

        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentHeight();

        ScheduleKind SelectedScheduleKind()
            => (scheduleMode.SelectedItem as ComboBoxItem)?.Tag as ScheduleKind?
                ?? ScheduleKind.Manual;

        void UpdateScheduleVisibility()
        {
            ScheduleKind kind = SelectedScheduleKind();
            bool once = kind == ScheduleKind.Once;
            bool recurring = kind is ScheduleKind.Daily
                or ScheduleKind.Weekly
                or ScheduleKind.Monthly;

            scheduleFields.Visibility = kind == ScheduleKind.Manual
                ? Visibility.Collapsed
                : Visibility.Visible;
            dateLabel.Visibility = once ? Visibility.Visible : Visibility.Collapsed;
            datePicker.Visibility = once ? Visibility.Visible : Visibility.Collapsed;
            timeLabel.Visibility = Visibility.Visible;
            timeLabel.Text = once ? "时间" : "执行时间";
            timePicker.Visibility = Visibility.Visible;
            Grid.SetColumn(timeLabel, once ? 1 : 0);
            Grid.SetColumn(timePicker, once ? 1 : 0);
            monthlyDateLabel.Visibility = kind == ScheduleKind.Monthly
                ? Visibility.Visible
                : Visibility.Collapsed;
            weekdaySection.Visibility = kind == ScheduleKind.Weekly
                ? Visibility.Visible
                : Visibility.Collapsed;
            dayOfMonthBox.Visibility = kind == ScheduleKind.Monthly
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (recurring && timePicker.SelectedTime is null)
                timePicker.SelectedTime = new TimeSpan(9, 30, 0);
        }

        UpdateContentHeight();
        UpdateScheduleVisibility();
        scheduleMode.SelectionChanged += (_, _) =>
        {
            UpdateScheduleVisibility();
            validation.Visibility = Visibility.Collapsed;
        };

        List<DayOfWeek> SelectedWeekdays()
        {
            var days = new List<DayOfWeek>();
            if (mondayCheck.IsChecked == true)
                days.Add(DayOfWeek.Monday);
            if (tuesdayCheck.IsChecked == true)
                days.Add(DayOfWeek.Tuesday);
            if (wednesdayCheck.IsChecked == true)
                days.Add(DayOfWeek.Wednesday);
            if (thursdayCheck.IsChecked == true)
                days.Add(DayOfWeek.Thursday);
            if (fridayCheck.IsChecked == true)
                days.Add(DayOfWeek.Friday);
            if (saturdayCheck.IsChecked == true)
                days.Add(DayOfWeek.Saturday);
            if (sundayCheck.IsChecked == true)
                days.Add(DayOfWeek.Sunday);
            return days;
        }

        ScheduledTask? result = null;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = current is null ? "添加 C# 脚本" : "编辑 C# 脚本",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 660d;
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
            string script = scriptEditor.GetText();
            if (name.Length == 0)
            {
                args.Cancel = true;
                ShowValidation("请输入脚本名称。");
                return;
            }
            if (string.IsNullOrWhiteSpace(script))
            {
                args.Cancel = true;
                ShowValidation("请输入 C# 脚本。");
                return;
            }

            ScheduleKind kind =
                (scheduleMode.SelectedItem as ComboBoxItem)?.Tag as ScheduleKind?
                ?? ScheduleKind.Manual;
            TimeSpan selectedTime = timePicker.SelectedTime
                ?? new TimeSpan(9, 30, 0);
            DateTimeOffset? runAt = null;
            if (kind == ScheduleKind.Once)
            {
                if (datePicker.Date is not { } date
                    || timePicker.SelectedTime is null)
                {
                    args.Cancel = true;
                    ShowValidation("请选择单次执行日期和时间。");
                    return;
                }

                DateTime local = DateTime.SpecifyKind(
                    date.Date.Add(selectedTime),
                    DateTimeKind.Unspecified);
                runAt = new DateTimeOffset(local);
                if (runAt <= DateTimeOffset.Now)
                {
                    args.Cancel = true;
                    ShowValidation("单次执行时间必须晚于当前时间。");
                    return;
                }
            }

            List<DayOfWeek> weekdays = kind == ScheduleKind.Weekly
                ? SelectedWeekdays()
                : [];
            if (kind == ScheduleKind.Weekly && weekdays.Count == 0)
            {
                args.Cancel = true;
                ShowValidation("每周任务至少选择一天。");
                return;
            }

            if (kind == ScheduleKind.Monthly
                && (double.IsNaN(dayOfMonthBox.Value)
                    || dayOfMonthBox.Value is < 1 or > 31
                    || dayOfMonthBox.Value != Math.Truncate(dayOfMonthBox.Value)))
            {
                args.Cancel = true;
                ShowValidation("请选择 1 至 31 之间的整数日期。");
                return;
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
                    TimeOfDay = kind is ScheduleKind.Daily
                        or ScheduleKind.Weekly
                        or ScheduleKind.Monthly
                            ? TimeOnly.FromTimeSpan(selectedTime)
                            : null,
                    DaysOfWeek = weekdays,
                    DayOfMonth = kind == ScheduleKind.Monthly
                        ? (int)dayOfMonthBox.Value
                        : null,
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
