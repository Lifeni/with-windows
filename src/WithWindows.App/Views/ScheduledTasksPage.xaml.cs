using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Scheduling;
using WithWindows.Services;

namespace WithWindows.Views;

public sealed partial class ScheduledTasksPage : Page
{
    private ScheduledTaskCoordinator? _coordinator;
    private ScheduledTask? _editingTask;
    private bool _subscribed;
    private bool _environmentSubscribed;
    private bool _loadingEditor;
    private bool _reloadQueued;
    private int _outputGeneration;
    private CancellationTokenSource? _pythonDetectionCancellation;

    public ScheduledTasksPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static string RouteTag => "scheduled";

    public ObservableCollection<ScheduledTaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<PythonEnvironmentSnapshot> Interpreters { get; } = [];
    public ObservableCollection<ScheduledTaskRunRowViewModel> Runs { get; } = [];

    private static AppHost? Host => App.Host;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateResponsiveLayout(ActualWidth);
            _coordinator = App.Scheduler;
            if (Host is null || _coordinator is null)
            {
                ShowError("定时任务服务尚未启动。");
                return;
            }

            if (_coordinator.Tasks.LoadState == StoreLoadState.Faulted
                || _coordinator.Runs.LoadState == StoreLoadState.Faulted)
            {
                ShowError(
                    _coordinator.Tasks.LastLoadError
                    ?? _coordinator.Runs.LastLoadError
                    ?? "任务或执行历史文件损坏，当前为只读故障态。");
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

            ReloadTasks();
            if (Tasks.Count == 0)
                NewTask();
            else
                SelectFirstTask();

            await DetectPythonAsync(preferredPath: _editingTask?.PythonPath);
        }
        catch (Exception ex)
        {
            ShowError($"加载定时任务失败：{ex.Message}");
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
        _outputGeneration++;
        _pythonDetectionCancellation?.Cancel();
        _pythonDetectionCancellation?.Dispose();
        _pythonDetectionCancellation = null;
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        bool narrow = width < 900;
        TaskListColumn.Width = new GridLength(narrow ? 0 : 280);
        NarrowTaskSelector.Visibility = narrow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScheduleFieldsPanel.Orientation = narrow
            ? Orientation.Vertical
            : Orientation.Horizontal;
        InterpreterButtonsPanel.Orientation = Orientation.Horizontal;
        Grid.SetRow(InterpreterComboBox, narrow ? 0 : 0);
        Grid.SetColumn(InterpreterComboBox, 0);
        Grid.SetColumnSpan(InterpreterComboBox, narrow ? 2 : 1);
        Grid.SetRow(InterpreterButtonsPanel, narrow ? 1 : 0);
        Grid.SetColumn(InterpreterButtonsPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(InterpreterButtonsPanel, narrow ? 2 : 1);
        Grid.SetRow(WorkingDirectoryBox, 0);
        Grid.SetColumn(WorkingDirectoryBox, 0);
        Grid.SetColumnSpan(WorkingDirectoryBox, narrow ? 2 : 1);
        Grid.SetRow(WorkingDirectoryButton, narrow ? 1 : 0);
        Grid.SetColumn(WorkingDirectoryButton, narrow ? 0 : 1);
        Grid.SetColumnSpan(WorkingDirectoryButton, narrow ? 2 : 1);
    }

    private void OnCoordinatorChanged(object? sender, EventArgs args)
    {
        if (_reloadQueued)
            return;

        _reloadQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _reloadQueued = false;
            if (!IsLoaded || XamlRoot is null)
                return;

            ReloadTasks(loadSelectedEditor: false);
            LoadRuns();
        });
    }

    private void OnEnvironmentChanged(object? sender, EventArgs args)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnEnvironmentChanged(sender, args));
            return;
        }

        if (IsLoaded && XamlRoot is not null)
            ReloadTasks(loadSelectedEditor: false);
    }

    private void ReloadTasks(bool loadSelectedEditor = true)
    {
        if (_coordinator is null)
            return;

        string? selectedId = SelectedTaskId();
        ScheduledTask[] tasks = _coordinator.Tasks.GetAll()
            .OrderByDescending(task => task.Enabled)
            .ThenBy(task => task.CreatedAt)
            .ToArray();
        IReadOnlyList<ScheduledTaskRun> allRuns = _coordinator.Runs.GetAll();

        Tasks.Clear();
        foreach (ScheduledTask task in tasks)
        {
            ScheduledTaskRun? latest = allRuns
                .FirstOrDefault(run => run.TaskId == task.Id);
            DateTimeOffset? next = task.Enabled
                ? ScheduleCalculator.NextAfter(
                    task.Schedule,
                    DateTimeOffset.UtcNow,
                    TimeZoneInfo.Local)
                : null;
            Tasks.Add(new ScheduledTaskRowViewModel(task, latest, next));
        }

        EmptyState.Visibility = Tasks.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        TaskList.Visibility = Tasks.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (selectedId is not null)
        {
            if (loadSelectedEditor)
                SelectTask(selectedId);
            else
                SelectTaskWithoutLoadingEditor(selectedId);
        }
        UpdateButtons();
    }

    private void SelectFirstTask()
    {
        if (Tasks.Count == 0)
            return;

        TaskList.SelectedIndex = 0;
    }

    private void SelectTask(string id)
    {
        ScheduledTaskRowViewModel? row = Tasks.FirstOrDefault(
            item => item.Id == id);
        if (row is null)
            return;

        TaskList.SelectedItem = row;
        NarrowTaskSelector.SelectedItem = row;
    }

    private void SelectTaskWithoutLoadingEditor(string id)
    {
        ScheduledTaskRowViewModel? row = Tasks.FirstOrDefault(
            item => item.Id == id);
        _loadingEditor = true;
        try
        {
            TaskList.SelectedItem = row;
            NarrowTaskSelector.SelectedItem = row;
        }
        finally
        {
            _loadingEditor = false;
        }
    }

    private string? SelectedTaskId()
        => (TaskList.SelectedItem as ScheduledTaskRowViewModel)?.Id
            ?? _editingTask?.Id;

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor
            || TaskList.SelectedItem is not ScheduledTaskRowViewModel row
            || _coordinator?.Tasks.Get(row.Id) is not { } task)
        {
            return;
        }

        _editingTask = task;
        NarrowTaskSelector.SelectedItem = row;
        LoadEditor(task);
        LoadRuns();
        UpdateButtons();
    }

    private void NewTask()
    {
        _editingTask = new ScheduledTask
        {
            Enabled = false,
            Schedule = new TaskSchedule
            {
                Kind = ScheduleKind.Daily,
                TimeOfDay = new TimeOnly(9, 30),
            },
            Source = new ScriptSource
            {
                Kind = ScriptSourceKind.Inline,
            },
            TimeoutSeconds = 600,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        TaskList.SelectedItem = null;
        NarrowTaskSelector.SelectedItem = null;
        LoadEditor(_editingTask);
        Runs.Clear();
        StdoutOutput.Text = "";
        StderrOutput.Text = "";
        RunSummaryText.Text = "保存后才能查看执行记录。";
        UpdateButtons();
    }

    private void OnNarrowTaskSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingEditor
            || NarrowTaskSelector.SelectedItem is not ScheduledTaskRowViewModel row
            || _coordinator?.Tasks.Get(row.Id) is not { } task)
        {
            return;
        }

        _editingTask = task;
        TaskList.SelectedItem = row;
        LoadEditor(task);
        LoadRuns();
        UpdateButtons();
    }

    private void OnNewTaskClick(object sender, RoutedEventArgs e)
    {
        try
        {
            NewTask();
        }
        catch (Exception ex)
        {
            ShowError($"新建任务失败：{ex.Message}");
        }
    }

    private void LoadEditor(ScheduledTask task)
    {
        _loadingEditor = true;
        try
        {
            EditorPanel.Visibility = Visibility.Visible;
            TaskNameBox.Text = task.Name;
            EnabledToggle.IsOn = task.Enabled;
            ScheduleTypeComboBox.SelectedIndex = task.Schedule.Kind switch
            {
                ScheduleKind.Once => 0,
                ScheduleKind.Weekly => 2,
                _ => 1,
            };

            DateTimeOffset local = DateTimeOffset.Now;
            ScheduleDatePicker.Date = task.Schedule.OneTimeRunAt?.ToLocalTime()
                ?? local.Date;
            ScheduleTimePicker.SelectedTime = task.Schedule.TimeOfDay?.ToTimeSpan()
                ?? new TimeSpan(9, 30, 0);
            SetWeekdayChecks(task.Schedule.DaysOfWeek);

            SourceKindComboBox.SelectedIndex =
                task.Source.Kind == ScriptSourceKind.File ? 1 : 0;
            ScriptEditor.Text = task.Source.ScriptText ?? "";
            PythonFilePath.Text = task.Source.ScriptPath ?? "";
            WorkingDirectoryBox.Text = task.WorkingDirectory ?? "";
            TimeoutBox.Value = Math.Clamp(
                task.TimeoutSeconds,
                ScheduledTaskValidation.MinimumTimeoutSeconds,
                ScheduledTaskValidation.MaximumTimeoutSeconds);

            SelectInterpreter(task);
            UpdateScheduleVisibility();
            UpdateSourceVisibility();
        }
        finally
        {
            _loadingEditor = false;
        }
    }

    private void SelectInterpreter(ScheduledTask task)
    {
        string? path = task.PythonResolvedPath ?? task.PythonPath;
        PythonEnvironmentSnapshot? selected = Interpreters.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (selected is null && !string.IsNullOrWhiteSpace(path))
        {
            selected = new PythonEnvironmentSnapshot(
                path,
                "未验证",
                0,
                task.PythonSource,
                task.PythonPackageFamilyName);
            Interpreters.Add(selected);
        }

        InterpreterComboBox.SelectedItem = selected;
    }

    private void OnScheduleTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEditor)
            UpdateScheduleVisibility();
    }

    private void UpdateScheduleVisibility()
    {
        string? tag = (ScheduleTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        ScheduleDatePicker.Visibility = tag == "once"
            ? Visibility.Visible
            : Visibility.Collapsed;
        WeekdayPanel.Visibility = tag == "weekly"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnSourceKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingEditor)
            UpdateSourceVisibility();
    }

    private void UpdateSourceVisibility()
    {
        bool isFile = (SourceKindComboBox.SelectedItem as ComboBoxItem)?.Tag as string
            == "file";
        ScriptEditor.Visibility = isFile
            ? Visibility.Collapsed
            : Visibility.Visible;
        FileSourcePanel.Visibility = isFile
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnInterpreterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor || InterpreterComboBox.SelectedItem is not PythonEnvironmentSnapshot item)
            return;

        PythonStatusBar.IsOpen = item.Bits == 0;
        PythonStatusBar.Message = item.Bits == 0
            ? "当前解释器尚未验证或已经失效。"
            : item.DisplayText;
    }

    private async void OnDetectPythonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await DetectPythonAsync(_editingTask?.PythonPath);
        }
        catch (Exception ex)
        {
            ShowError($"检测 Python 失败：{ex.Message}");
        }
    }

    private async Task<IReadOnlyList<PythonEnvironmentSnapshot>> DetectPythonAsync(
        string? preferredPath,
        string? explicitSelection = null)
    {
        if (App.PythonEnvironments is null)
        {
            Host?.Log.Error("定时任务页：Python 环境检测服务不可用。");
            return [];
        }

        _pythonDetectionCancellation?.Cancel();
        _pythonDetectionCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _pythonDetectionCancellation = cancellation;
        Host?.Log.Info("定时任务页：开始检测 Python");
        InterpreterComboBox.IsEnabled = false;
        try
        {
            IReadOnlyList<PythonEnvironmentSnapshot> environments =
                await App.PythonEnvironments.DetectAsync(
                    preferredPath,
                    cancellation.Token);
            if (cancellation.IsCancellationRequested
                || !IsLoaded
                || XamlRoot is null)
            {
                return [];
            }

            Interpreters.Clear();
            foreach (PythonEnvironmentSnapshot environment in environments)
                Interpreters.Add(environment);

            if (Interpreters.Count == 0)
            {
                PythonStatusBar.Severity = InfoBarSeverity.Warning;
                PythonStatusBar.Title = "未检测到 Python 3";
                PythonStatusBar.Message = "请安装 Python 3，或者手动选择 python.exe。";
                PythonStatusBar.IsOpen = true;
                EnabledToggle.IsEnabled = false;
            }
            else
            {
                PythonStatusBar.Severity = InfoBarSeverity.Informational;
                PythonStatusBar.Title = "Python 环境";
                PythonStatusBar.Message = $"已检测到 {Interpreters.Count} 个 Python 3 环境。";
                PythonStatusBar.IsOpen = false;
                EnabledToggle.IsEnabled = true;
            }

            if (!string.IsNullOrWhiteSpace(explicitSelection))
            {
                PythonEnvironmentSnapshot? selected = Interpreters.FirstOrDefault(
                    item => string.Equals(
                        item.Path,
                        explicitSelection,
                        StringComparison.OrdinalIgnoreCase));
                if (selected is null
                    && PythonEnvironmentParser.IsStoreAliasPath(explicitSelection))
                {
                    selected = Interpreters.FirstOrDefault(item =>
                        item.Source == PythonInterpreterSource.StoreAlias);
                }
                selected ??= environments.FirstOrDefault(item =>
                    item.Source == PythonInterpreterSource.Manual);

                InterpreterComboBox.SelectedItem = selected;
            }
            else
            {
                if (_editingTask is not null)
                    SelectInterpreter(_editingTask);

                if (InterpreterComboBox.SelectedItem is null
                    && Interpreters.Count > 0)
                {
                    InterpreterComboBox.SelectedIndex = 0;
                }
            }

            Host?.Log.Info($"定时任务页：检测到 {Interpreters.Count} 个 Python 环境");
            return environments;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        finally
        {
            if (ReferenceEquals(_pythonDetectionCancellation, cancellation))
            {
                _pythonDetectionCancellation = null;
                cancellation.Dispose();
            }

            if (IsLoaded && XamlRoot is not null)
                InterpreterComboBox.IsEnabled = true;
        }
    }

    private async void OnBrowsePythonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null || App.PythonEnvironments is null)
                return;

            if (!IsLoaded || XamlRoot is null)
                return;

            IReadOnlyList<PythonEnvironmentSnapshot> detected =
                await DetectPythonAsync(file.Path, file.Path);
            if (!IsLoaded || XamlRoot is null)
                return;
            PythonEnvironmentSnapshot? chosen =
                InterpreterComboBox.SelectedItem as PythonEnvironmentSnapshot
                ?? detected.FirstOrDefault();
            if (chosen is null)
            {
                ShowError("所选文件不是可用的 Python 3 解释器。");
                return;
            }

            InterpreterComboBox.SelectedItem = chosen;
        }
        catch (Exception ex)
        {
            ShowError($"选择 Python 解释器失败：{ex.Message}");
        }
    }

    private async void OnChoosePythonFileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".py");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is not null && IsLoaded && XamlRoot is not null)
                PythonFilePath.Text = file.Path;
        }
        catch (Exception ex)
        {
            ShowError($"选择 Python 文件失败：{ex.Message}");
        }
    }

    private void OnOpenPythonFolderClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PythonFilePath.Text))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{PythonFilePath.Text}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowError($"打开文件夹失败：{ex.Message}");
        }
    }

    private async void OnChooseWorkingDirectoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));
            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is not null && IsLoaded && XamlRoot is not null)
                WorkingDirectoryBox.Text = folder.Path;
        }
        catch (Exception ex)
        {
            ShowError($"选择工作目录失败：{ex.Message}");
        }
    }

    private async void OnSaveTaskClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveCurrentTaskAsync();
        }
        catch (Exception ex)
        {
            ShowError($"保存任务失败：{ex.Message}");
        }
    }

    private async Task<ScheduledTask?> SaveCurrentTaskAsync()
    {
        if (_coordinator is null)
            return null;

        ScheduledTask task = ReadEditor();
        ScheduledTask saved = _coordinator.SaveTask(task);
        _editingTask = saved;
        ReloadTasks();
        SelectTask(saved.Id);
        LoadEditor(saved);
        LoadRuns();
        Host?.Log.Info($"定时任务已保存：{saved.Name}");
        await Task.CompletedTask;
        return saved;
    }

    private ScheduledTask ReadEditor()
    {
        ScheduledTask task = _editingTask is null
            ? new ScheduledTask { CreatedAt = DateTimeOffset.UtcNow }
            : new ScheduledTask
            {
                Id = _editingTask.Id,
                Name = _editingTask.Name,
                Enabled = _editingTask.Enabled,
                ScheduleRevision = _editingTask.ScheduleRevision,
                LastHandledScheduledFor = _editingTask.LastHandledScheduledFor,
                LastHandledRunId = _editingTask.LastHandledRunId,
                CreatedAt = _editingTask.CreatedAt,
                UpdatedAt = _editingTask.UpdatedAt,
            };

        task.Name = TaskNameBox.Text.Trim();
        task.Enabled = EnabledToggle.IsOn;
        task.TimeoutSeconds = (int)Math.Clamp(
            double.IsNaN(TimeoutBox.Value) ? 600 : TimeoutBox.Value,
            ScheduledTaskValidation.MinimumTimeoutSeconds,
            ScheduledTaskValidation.MaximumTimeoutSeconds);
        task.WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text)
            ? null
            : WorkingDirectoryBox.Text.Trim();

        string scheduleTag =
            (ScheduleTypeComboBox.SelectedItem as ComboBoxItem)?.Tag as string
            ?? "daily";
        task.Schedule = new TaskSchedule
        {
            Kind = scheduleTag switch
            {
                "once" => ScheduleKind.Once,
                "weekly" => ScheduleKind.Weekly,
                _ => ScheduleKind.Daily,
            },
        };
        TimeSpan selectedTime = ScheduleTimePicker.SelectedTime
            ?? new TimeSpan(9, 30, 0);
        task.Schedule.TimeOfDay = TimeOnly.FromTimeSpan(selectedTime);

        if (task.Schedule.Kind == ScheduleKind.Once)
        {
            DateTimeOffset date = ScheduleDatePicker.Date
                ?? DateTimeOffset.Now.Date;
            DateTime local = DateTime.SpecifyKind(
                date.Date.Add(selectedTime),
                DateTimeKind.Unspecified);
            task.Schedule.OneTimeRunAt = new DateTimeOffset(
                local,
                TimeZoneInfo.Local.GetUtcOffset(local));
            if (task.Schedule.OneTimeRunAt <= DateTimeOffset.Now)
                throw new ArgumentException("一次性任务的执行时间必须晚于当前时间。");
        }

        if (task.Schedule.Kind == ScheduleKind.Weekly)
        {
            task.Schedule.DaysOfWeek = SelectedWeekdays().ToList();
        }

        string sourceTag =
            (SourceKindComboBox.SelectedItem as ComboBoxItem)?.Tag as string
            ?? "inline";
        task.Source = new ScriptSource
        {
            Kind = sourceTag == "file"
                ? ScriptSourceKind.File
                : ScriptSourceKind.Inline,
            ScriptText = sourceTag == "file" ? null : ScriptEditor.Text,
            ScriptPath = sourceTag == "file"
                ? EmptyToNull(PythonFilePath.Text)
                : null,
        };

        if (InterpreterComboBox.SelectedItem is PythonEnvironmentSnapshot interpreter)
        {
            task.PythonPath = interpreter.Path;
            task.PythonSource = interpreter.Source;
            task.PythonResolvedPath = interpreter.Path;
            task.PythonPackageFamilyName = interpreter.PackageFamilyName;
        }
        else
        {
            task.Enabled = false;
        }

        return task;
    }

    private async void OnRunNowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ScheduledTask? task = await SaveCurrentTaskAsync();
            if (task is null || _coordinator is null)
                return;

            RunNowButton.IsEnabled = false;
            await _coordinator.RunNowAsync(task.Id);
            if (!IsLoaded || XamlRoot is null)
                return;

            LoadRuns();
            UpdateButtons();
        }
        catch (Exception ex)
        {
            ShowError($"运行任务失败：{ex.Message}");
        }
        finally
        {
            if (IsLoaded && XamlRoot is not null)
                UpdateButtons();
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (_editingTask?.Id is { } id && _coordinator?.StopTask(id) != true)
            ShowError("任务当前没有正在运行的进程。");
    }

    private async void OnDeleteTaskClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_editingTask?.Id is not { } id || _coordinator is null)
                return;

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "删除定时任务",
                Content = $"“{_editingTask.Name}”及其执行历史将被删除，外部 .py 文件不会被删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await App.ShowDialogAsync(dialog) != ContentDialogResult.Primary)
                return;

            await _coordinator.DeleteTaskAsync(id);
            _editingTask = null;
            Runs.Clear();
            ReloadTasks();
            if (Tasks.Count == 0)
                NewTask();
            else
                SelectFirstTask();
        }
        catch (Exception ex)
        {
            ShowError($"删除任务失败：{ex.Message}");
        }
    }

    private void LoadRuns()
    {
        Runs.Clear();
        if (_editingTask?.Id is not { } id || _coordinator is null)
        {
            RunSummaryText.Text = "保存后才能查看执行记录。";
            StdoutOutput.Text = "";
            StderrOutput.Text = "";
            return;
        }

        foreach (ScheduledTaskRun run in _coordinator.Runs.GetAll(id))
            Runs.Add(new ScheduledTaskRunRowViewModel(run));
        RunSummaryText.Text = Runs.Count == 0
            ? "暂无执行记录。"
            : $"共 {Runs.Count} 条执行记录。";
    }

    private async void OnRunSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int generation = ++_outputGeneration;
        try
        {
            if (RunHistoryList.SelectedItem is not ScheduledTaskRunRowViewModel run)
                return;

            string stdout = await ReadOutputAsync(
                run.TaskId,
                run.StdoutLogPath);
            string stderr = await ReadOutputAsync(
                run.TaskId,
                run.StderrLogPath);
            if (generation != _outputGeneration || !IsLoaded)
                return;

            StdoutOutput.Text = stdout;
            StderrOutput.Text = string.IsNullOrWhiteSpace(run.ErrorMessage)
                ? stderr
                : string.IsNullOrWhiteSpace(stderr)
                    ? run.ErrorMessage
                    : stderr + Environment.NewLine + run.ErrorMessage;
            RunSummaryText.Text = $"{run.StartedText}・{run.StatusText}";
        }
        catch (Exception ex)
        {
            ShowError($"读取执行输出失败：{ex.Message}");
        }
    }

    private void OnTaskContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is ScheduledTaskRowViewModel task
            && args.ItemContainer is ListViewItem item)
        {
            AutomationProperties.SetAutomationId(item, task.AutomationId);
            AutomationProperties.SetName(item, task.Title);
        }
    }

    private void OnRunContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is ScheduledTaskRunRowViewModel run
            && args.ItemContainer is ListViewItem item)
        {
            AutomationProperties.SetAutomationId(item, run.AutomationId);
            AutomationProperties.SetName(item, run.DetailText);
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

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ScheduledTaskLogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppPaths.ScheduledTaskLogsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowError($"打开日志目录失败：{ex.Message}");
        }
    }

    private void UpdateButtons()
    {
        bool hasTask = _editingTask is not null;
        bool hasId = !string.IsNullOrWhiteSpace(_editingTask?.Id);
        bool running = hasId && _coordinator?.IsTaskRunning(_editingTask!.Id) == true;
        SaveButton.IsEnabled = hasTask;
        RunNowButton.IsEnabled = hasTask && !running;
        StopButton.IsEnabled = running;
        DeleteButton.IsEnabled = hasId && !running;
    }

    private IEnumerable<DayOfWeek> SelectedWeekdays()
    {
        if (MondayCheck.IsChecked == true) yield return DayOfWeek.Monday;
        if (TuesdayCheck.IsChecked == true) yield return DayOfWeek.Tuesday;
        if (WednesdayCheck.IsChecked == true) yield return DayOfWeek.Wednesday;
        if (ThursdayCheck.IsChecked == true) yield return DayOfWeek.Thursday;
        if (FridayCheck.IsChecked == true) yield return DayOfWeek.Friday;
        if (SaturdayCheck.IsChecked == true) yield return DayOfWeek.Saturday;
        if (SundayCheck.IsChecked == true) yield return DayOfWeek.Sunday;
    }

    private void SetWeekdayChecks(IEnumerable<DayOfWeek> days)
    {
        HashSet<DayOfWeek> selected = days.ToHashSet();
        MondayCheck.IsChecked = selected.Contains(DayOfWeek.Monday);
        TuesdayCheck.IsChecked = selected.Contains(DayOfWeek.Tuesday);
        WednesdayCheck.IsChecked = selected.Contains(DayOfWeek.Wednesday);
        ThursdayCheck.IsChecked = selected.Contains(DayOfWeek.Thursday);
        FridayCheck.IsChecked = selected.Contains(DayOfWeek.Friday);
        SaturdayCheck.IsChecked = selected.Contains(DayOfWeek.Saturday);
        SundayCheck.IsChecked = selected.Contains(DayOfWeek.Sunday);
    }

    private void ShowError(string message)
    {
        if (!IsLoaded || XamlRoot is null)
        {
            Host?.Log.Error($"定时任务页（已卸载）：{message}");
            return;
        }

        ErrorBar.Title = "操作失败";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
        Host?.Log.Error($"定时任务页：{message}");
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
