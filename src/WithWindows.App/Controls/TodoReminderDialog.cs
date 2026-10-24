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

        var datePicker = new CalendarDatePicker
        {
            Date = new DateTimeOffset(initial.Date),
            Height = 40,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(datePicker, "TodoReminderDate");
        AutomationProperties.SetName(datePicker, "提醒日期");

        var timePicker = new TimePicker
        {
            ClockIdentifier = "24HourClock",
            Height = 40,
            MinWidth = 0,
            SelectedTime = initial.TimeOfDay,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(timePicker, "TodoReminderTime");
        AutomationProperties.SetName(timePicker, "提醒时间");
        // TimePicker 模板把内部 FlyoutButton 顶对齐，外层 Height 不会撑高可见内容。
        // 用对称内边距把内容框实际撑到与 CalendarDatePicker 相同的 40 DIP。
        timePicker.Resources["TimePickerHostPadding"] = new Thickness(0, 9, 0, 9);

        var dateLabel = CreateFieldLabel("日期");
        var timeLabel = CreateFieldLabel("时间");

        var pickerGrid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 4,
        };
        pickerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pickerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pickerGrid.Children.Add(dateLabel);
        Grid.SetColumn(timeLabel, 1);
        pickerGrid.Children.Add(timeLabel);
        Grid.SetRow(datePicker, 1);
        pickerGrid.Children.Add(datePicker);
        Grid.SetColumn(timePicker, 1);
        Grid.SetRow(timePicker, 1);
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
        content.Children.Add(pickerGrid);

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

        var contentLayout = new Grid { MinWidth = 420 };
        contentLayout.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentLayout.Children.Add(contentScroll);
        Grid.SetRow(validation, 1);
        contentLayout.Children.Add(validation);

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
            Title = hasExisting ? "修改提醒" : "设置提醒",
            Content = contentLayout,
            PrimaryButtonText = "保存",
            SecondaryButtonText = hasExisting ? "移除提醒" : "",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        TodoReminderChoice? choice = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
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

    private static TextBlock CreateFieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

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
