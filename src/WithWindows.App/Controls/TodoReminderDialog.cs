using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Todos;

namespace WithWindows.Controls;

/// <summary>提醒编辑结果；移除与设置互斥。</summary>
public readonly record struct TodoReminderChoice(bool Remove, DateTimeOffset? At);

/// <summary>用原生日期与时间控件编辑单个待办的一次性提醒。</summary>
public static class TodoReminderDialog
{
    public static async Task<TodoReminderChoice?> ShowAsync(
        XamlRoot xamlRoot,
        TodoItem item)
    {
        DateTimeOffset initial = item.ReminderAt?.ToLocalTime()
            ?? RoundUpToFiveMinutes(DateTimeOffset.Now.AddHours(1));
        bool hasExisting = item.ReminderAt is not null;

        var enabled = new CheckBox
        {
            Content = "启用提醒",
            IsChecked = hasExisting,
        };
        AutomationProperties.SetAutomationId(enabled, "TodoReminderEnabled");
        AutomationProperties.SetName(enabled, "启用提醒");

        var datePicker = new CalendarDatePicker
        {
            Header = "日期",
            Date = new DateTimeOffset(initial.Date),
            IsEnabled = hasExisting,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(datePicker, "TodoReminderDate");
        AutomationProperties.SetName(datePicker, "提醒日期");

        var timePicker = new TimePicker
        {
            Header = "时间",
            ClockIdentifier = "24HourClock",
            SelectedTime = initial.TimeOfDay,
            IsEnabled = hasExisting,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(timePicker, "TodoReminderTime");
        AutomationProperties.SetName(timePicker, "提醒时间");

        enabled.Checked += (_, _) => SetPickersEnabled(isEnabled: true);
        enabled.Unchecked += (_, _) => SetPickersEnabled(isEnabled: false);

        void SetPickersEnabled(bool isEnabled)
        {
            if (datePicker is null || timePicker is null)
                return;

            datePicker.IsEnabled = isEnabled;
            timePicker.IsEnabled = isEnabled;
        }

        var pickerGrid = new Grid
        {
            ColumnSpacing = 12,
            Margin = new Thickness(0, 8, 0, 0),
        };
        pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerGrid.Children.Add(datePicker);
        Grid.SetColumn(timePicker, 1);
        pickerGrid.Children.Add(timePicker);

        var validation = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(validation, "TodoReminderValidation");
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);

        var content = new StackPanel
        {
            MinWidth = 420,
            Spacing = 4,
        };
        content.Children.Add(enabled);
        content.Children.Add(pickerGrid);
        content.Children.Add(validation);

        var contentScroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(contentScroll, "TodoReminderContentScroll");

        void UpdateContentHeight()
            => contentScroll.MaxHeight = Math.Clamp(
                xamlRoot.Size.Height - 200,
                min: 160,
                max: 520);

        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentHeight();

        UpdateContentHeight();

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "设置提醒",
            Content = contentScroll,
            PrimaryButtonText = "保存",
            SecondaryButtonText = hasExisting ? "移除提醒" : "",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        TodoReminderChoice? choice = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (enabled.IsChecked != true)
            {
                choice = new TodoReminderChoice(Remove: true, At: null);
                return;
            }

            if (datePicker.Date is not { } date || timePicker.SelectedTime is not { } time)
            {
                args.Cancel = true;
                validation.Text = "请选择提醒日期和时间。";
                validation.Visibility = Visibility.Visible;
                return;
            }

            DateTime selected = date.Date + time;
            DateTimeOffset at = new(DateTime.SpecifyKind(selected, DateTimeKind.Unspecified));
            if (at <= DateTimeOffset.Now)
            {
                args.Cancel = true;
                validation.Text = "提醒时间必须晚于当前时间。";
                validation.Visibility = Visibility.Visible;
                return;
            }

            choice = new TodoReminderChoice(Remove: false, At: at);
        };

        ContentDialogResult shown;
        try
        {
            xamlRoot.Changed += OnXamlRootChanged;
            shown = await dialog.ShowAsync();
        }
        finally
        {
            xamlRoot.Changed -= OnXamlRootChanged;
        }

        return shown switch
        {
            ContentDialogResult.Primary => choice,
            ContentDialogResult.Secondary => new TodoReminderChoice(Remove: true, At: null),
            _ => null,
        };
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
        DateTime local = rounded.AddMinutes(minutes);
        return new DateTimeOffset(local);
    }
}
