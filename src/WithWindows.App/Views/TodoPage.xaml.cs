using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WithWindows.Controls;
using WithWindows.Core.Todos;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>待办页：快速添加、完成、删除，并为每项设置一次性提醒。</summary>
public sealed partial class TodoPage : Page
{
    private bool _subscribed;
    private bool _reloadQueued;

    public TodoPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static string RouteTag => "todo";

    public ObservableCollection<TodoRowViewModel> Items { get; } = [];

    private static AppHost? Host => App.Host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null)
            return;

        if (!_subscribed)
        {
            Host.Todos.Changed += OnTodosChanged;
            _subscribed = true;
        }

        Reload();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (IsLoaded && XamlRoot is not null)
                NewTodoInput.Focus(FocusState.Programmatic);
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed && Host is not null)
            Host.Todos.Changed -= OnTodosChanged;
        _subscribed = false;
    }

    private void OnTodosChanged(object? sender, EventArgs args)
    {
        if (_reloadQueued)
            return;

        // 对话框关闭或控件事件尚未退出时不同步重建 XAML 集合；排到下一轮 UI 队列，
        // 避免 ContentDialog 与 ListView 在同一事件栈中重入导致 WinUI 原生退出。
        _reloadQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _reloadQueued = false;
            Reload();
        });
    }

    private void Reload()
    {
        if (Host is null || !IsLoaded || XamlRoot is null)
            return;

        TodoItem[] todos = Host.Todos.GetAll()
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.CreatedAt)
            .ToArray();

        Items.Clear();
        foreach (TodoItem item in todos)
            Items.Add(new TodoRowViewModel(item));

        int pending = todos.Count(item => !item.IsCompleted);
        int completed = todos.Length - pending;
        SummaryText.Text = todos.Length == 0
            ? "暂无待办"
            : $"{pending} 项待完成 · {completed} 项已完成";
        EmptyState.Visibility = todos.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnNewInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        AddTodo();
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        string title = NewTodoInput.Text.Trim();
        if (title.Length == 0)
        {
            NewTodoInput.Focus(FocusState.Programmatic);
            return;
        }

        try
        {
            Host!.Todos.Add(title, DateTimeOffset.UtcNow);
            NewTodoInput.Text = "";
            NewTodoInput.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            ShowError($"无法添加待办：{ex.Message}");
        }
    }

    private void OnCompletedChecked(object sender, RoutedEventArgs e)
        => SetCompleted(sender, completed: true);

    private void OnCompletedUnchecked(object sender, RoutedEventArgs e)
        => SetCompleted(sender, completed: false);

    private void SetCompleted(object sender, bool completed)
    {
        if (sender is not CheckBox { Tag: string id })
            return;

        try
        {
            Host?.Todos.SetCompleted(id, completed, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            ShowError($"无法更新待办：{ex.Message}");
            Reload();
        }
    }

    private async void OnReminderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }
                || GetTodo(id) is not { } item)
            {
                return;
            }

            TodoReminderChoice? choice = await TodoReminderDialog.ShowAsync(XamlRoot, item);
            if (choice is not { } result)
                return;

            Host!.Todos.SetReminder(
                id,
                result.Remove ? null : result.At,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            ShowError($"无法保存提醒：{ex.Message}");
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string id }
                || GetTodo(id) is not { } item)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "删除待办",
                Content = $"“{item.Title}”删除后无法恢复。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                Host!.Todos.Delete(id);
        }
        catch (Exception ex)
        {
            ShowError($"无法删除待办：{ex.Message}");
        }
    }

    private TodoItem? GetTodo(string id)
        => Host?.Todos.GetAll().FirstOrDefault(item => item.Id == id);

    /// <summary>提醒到期后由主窗口调用，切换到本页时显示可见提示。</summary>
    public void ShowReminderNotice(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0)
            return;

        string content = titles.Count == 1
            ? $"“{titles[0]}”已到期。"
            : string.Join(
                Environment.NewLine,
                titles.Select(title => $"• {title}"));
        TodoReminderBar.Message = content;
        TodoReminderBar.IsOpen = true;
    }

    private void ShowError(string message)
    {
        TodoErrorBar.Title = "操作失败";
        TodoErrorBar.Message = message;
        TodoErrorBar.IsOpen = true;
        Host?.Log.Error($"待办页：{message}");
    }
}
