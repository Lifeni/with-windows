using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Controls;
using WithWindows.Core.Scheduling;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>Lua 脚本列表；添加与编辑统一在独立对话框中完成。</summary>
public sealed partial class ScheduledTasksPage : Page
{
    private ScheduledTaskCoordinator? _coordinator;
    private bool _subscribed;
    private bool _environmentSubscribed;
    private bool _reloadQueued;

    public ScheduledTasksPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static string RouteTag => "scheduled";

    public ObservableCollection<ScheduledTaskRowViewModel> Tasks { get; } = [];

    private static AppHost? Host => App.Host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _coordinator = App.Scheduler;
            if (Host is null || _coordinator is null)
            {
                ShowError("脚本服务尚未启动。");
                return;
            }

            if (_coordinator.Tasks.LoadState == StoreLoadState.Faulted
                || _coordinator.Runs.LoadState == StoreLoadState.Faulted)
            {
                ShowError(
                    _coordinator.Tasks.LastLoadError
                    ?? _coordinator.Runs.LastLoadError
                    ?? "脚本或执行历史文件损坏，当前为只读故障态。");
            }

            if (!_subscribed)
            {
                _coordinator.Changed += OnCoordinatorChanged;
                _subscribed = true;
            }
            if (!_environmentSubscribed && App.TaskHost is { } taskHost)
            {
                taskHost.EnvironmentChanged += OnEnvironmentChanged;
                _environmentSubscribed = true;
            }

            Reload();
        }
        catch (Exception ex)
        {
            ShowError($"加载 Lua 脚本失败：{ex.Message}");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed && _coordinator is not null)
            _coordinator.Changed -= OnCoordinatorChanged;
        _subscribed = false;
        if (_environmentSubscribed)
        {
            if (App.TaskHost is { } taskHost)
                taskHost.EnvironmentChanged -= OnEnvironmentChanged;
            _environmentSubscribed = false;
        }
    }

    private void OnCoordinatorChanged(object? sender, EventArgs args)
    {
        if (_reloadQueued)
            return;

        _reloadQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _reloadQueued = false;
            Reload();
        });
    }

    private void OnEnvironmentChanged(object? sender, EventArgs args)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnEnvironmentChanged(sender, args));
            return;
        }

        Reload();
    }

    private void Reload()
    {
        if (!IsLoaded || XamlRoot is null || _coordinator is null)
            return;

        ScheduledTask[] tasks = _coordinator.Tasks.GetAll()
            .OrderBy(task => task.CreatedAt)
            .ToArray();
        IReadOnlyList<ScheduledTaskRun> runs = _coordinator.Runs.GetAll();
        Tasks.Clear();
        foreach (ScheduledTask task in tasks)
        {
            ScheduledTaskRun? latest = runs.FirstOrDefault(
                run => run.TaskId == task.Id);
            DateTimeOffset? next = ScheduleCalculator.NextAfter(
                task.Schedule,
                DateTimeOffset.UtcNow,
                TimeZoneInfo.Local);
            Tasks.Add(new ScheduledTaskRowViewModel(
                task,
                latest,
                next,
                _coordinator.IsTaskRunning(task.Id)));
        }

        SummaryText.Text = $"{tasks.Length} 个脚本";
        EmptyState.Visibility = tasks.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        TaskList.Visibility = tasks.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void OnAddScriptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_coordinator is null)
                return;

            ScheduledTask? task = await ScheduledTaskEditorDialog.ShowAsync(
                XamlRoot,
                current: null);
            if (task is null)
                return;

            _coordinator.SaveTask(task);
            ErrorBar.IsOpen = false;
            Reload();
        }
        catch (Exception ex)
        {
            ShowError($"添加脚本失败：{ex.Message}");
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }
                || _coordinator?.Tasks.Get(id) is not { } task)
            {
                return;
            }
            if (_coordinator.IsTaskRunning(id))
            {
                ShowError("脚本正在运行，停止后才能编辑。");
                return;
            }

            ScheduledTask? updated = await ScheduledTaskEditorDialog.ShowAsync(
                XamlRoot,
                task);
            if (updated is null)
                return;

            _coordinator.SaveTask(updated);
            ErrorBar.IsOpen = false;
            Reload();
        }
        catch (Exception ex)
        {
            ShowError($"保存脚本失败：{ex.Message}");
        }
    }

    private async void OnRunClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }
                || _coordinator is null)
            {
                return;
            }

            await _coordinator.RunNowAsync(id);
            Reload();
        }
        catch (Exception ex)
        {
            ShowError($"运行脚本失败：{ex.Message}");
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }
            || _coordinator?.StopTask(id) != true)
        {
            ShowError("脚本当前没有正在运行的进程。");
            return;
        }

        Reload();
    }

    private async void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }
                || _coordinator?.Tasks.Get(id) is not { } task)
            {
                return;
            }

            await ScheduledTaskHistoryDialog.ShowAsync(
                XamlRoot,
                task,
                _coordinator.Runs.GetAll(id));
        }
        catch (Exception ex)
        {
            ShowError($"查看运行日志失败：{ex.Message}");
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }
                || _coordinator?.Tasks.Get(id) is not { } task)
            {
                return;
            }
            if (_coordinator.IsTaskRunning(id))
            {
                ShowError("脚本正在运行，停止后才能删除。");
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "删除 Lua 脚本",
                Content = $"“{task.Name}”及其执行历史将被删除。外部 Python、Node.js 文件不会被删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await App.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
                return;

            await _coordinator.DeleteTaskAsync(id);
            ErrorBar.IsOpen = false;
            Reload();
        }
        catch (Exception ex)
        {
            ShowError($"删除脚本失败：{ex.Message}");
        }
    }

    private void OnTaskContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is not ScheduledTaskRowViewModel task
            || args.ItemContainer is not ListViewItem item)
        {
            return;
        }

        AutomationProperties.SetAutomationId(item, task.AutomationId);
        AutomationProperties.SetName(item, task.Title);
    }

    private void ShowError(string message)
    {
        if (!IsLoaded || XamlRoot is null)
        {
            Host?.Log.Error($"Lua 脚本页（已卸载）：{message}");
            return;
        }

        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
        Host?.Log.Error($"Lua 脚本页：{message}");
    }
}
