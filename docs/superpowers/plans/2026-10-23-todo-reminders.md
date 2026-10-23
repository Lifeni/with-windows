# 待办与提醒实现计划

> 实现偏差：Windows App SDK 2.5.1 自包含应用的通知注册会在隐藏启动路径触发 WinUI 原生崩溃，Task 3 和 Task 7 中的系统 toast 方案未采用。最终提醒改为精确截止计时后自动唤出待办页并显示 `InfoBar`；其余存储、页面与测试任务按计划完成。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增可持久化的待办页面，并让每个待办可以在指定时间自动唤出应用内提醒。

**Architecture:** Core 提供待办模型、仓储和纯函数提醒策略；App 的页面只负责交互与展示，`AppHost` 持有共享仓储；`TodoReminderHost` 使用单次后台计时器睡到最近截止时间，到期后唤出待办页并显示应用内提示条。

**Tech Stack:** .NET 10、WinUI 3、`System.Threading.Timer`、`System.Text.Json`、xunit。

**Spec:** `docs/superpowers/specs/2026-10-23-todo-reminders-design.md`

## Global Constraints

- 仅 x64，所有构建和测试命令带 `-p:Platform=x64`。
- 不新增第三方包；提醒使用已引用的 WinUI 3 与 .NET 计时 API。
- `WithWindows.Core` 不得引用任何 `Microsoft.UI.Xaml` 类型。
- 页面保持 code-behind，不引入 MVVM 框架。
- 运行时待办数据写入 `%APPDATA%\WithWindows\todos.json`，程序目录只放程序与资源。
- 不使用固定间隔轮询；没有待提醒项时不保留活动计时器。
- 所有新增交互控件的 `AutomationProperties.AutomationId` 使用英文 PascalCase，`AutomationProperties.Name` 使用中文。
- 中文文案使用中文标点，中英文与数字之间加空格。
- 提醒只在应用驻留托盘时触发；重启后补发已到期但未提醒的项。
- 每批生产代码必须先有失败的单元测试；UI 和无头系统集成通过构建、启动和 `winapp ui` 验证。

---

### Task 1: Core 待办模型与提醒策略

**Files:**
- Create: `src/WithWindows.Core/Todos/TodoItem.cs`
- Create: `src/WithWindows.Core/Todos/TodoReminderPolicy.cs`
- Create: `tests/WithWindows.Core.Tests/TodoReminderPolicyTests.cs`

**Interfaces:**
- Consumes: 无。
- Produces:
  - `public sealed class TodoItem`
  - `public sealed class TodoDocument`
  - `public static IReadOnlyList<TodoItem> TodoReminderPolicy.GetDue(IEnumerable<TodoItem> items, DateTimeOffset now)`
  - `public static DateTimeOffset? TodoReminderPolicy.NextWakeAt(IEnumerable<TodoItem> items, DateTimeOffset now)`

- [ ] **Step 1: 写失败测试**

```csharp
using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public class TodoReminderPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetDue_只返回未完成且尚未提醒的到期项()
    {
        TodoItem due = Item("due", Now.AddMinutes(-1));
        TodoItem future = Item("future", Now.AddMinutes(1));
        TodoItem completed = Item("completed", Now.AddMinutes(-2));
        completed.IsCompleted = true;
        TodoItem delivered = Item("delivered", Now.AddMinutes(-3));
        delivered.ReminderDeliveredAt = Now.AddMinutes(-2);

        IReadOnlyList<TodoItem> result =
            TodoReminderPolicy.GetDue([due, future, completed, delivered], Now);

        Assert.Equal(["due"], result.Select(item => item.Id));
    }

    [Fact]
    public void NextWakeAt_返回最早待提醒时间()
    {
        TodoItem later = Item("later", Now.AddHours(2));
        TodoItem sooner = Item("sooner", Now.AddHours(1));

        DateTimeOffset? next = TodoReminderPolicy.NextWakeAt([later, sooner], Now);

        Assert.Equal(Now.AddHours(1), next);
    }

    [Fact]
    public void NextWakeAt_没有候选时返回空()
    {
        TodoItem completed = Item("completed", Now.AddHours(1));
        completed.IsCompleted = true;

        Assert.Null(TodoReminderPolicy.NextWakeAt([completed], Now));
    }

    private static TodoItem Item(string id, DateTimeOffset reminderAt) => new()
    {
        Id = id,
        Title = id,
        CreatedAt = Now,
        ReminderAt = reminderAt,
    };
}
```

- [ ] **Step 2: 运行测试并确认因类型不存在而失败**

Run:

```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter TodoReminderPolicyTests
```

Expected: `FAIL`，编译错误指出 `WithWindows.Core.Todos` 或 `TodoItem` 不存在。

- [ ] **Step 3: 写最小模型与策略**

```csharp
namespace WithWindows.Core.Todos;

public sealed class TodoItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ReminderAt { get; set; }
    public DateTimeOffset? ReminderDeliveredAt { get; set; }
}

public sealed class TodoDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public List<TodoItem> Items { get; set; } = [];
}
```

```csharp
namespace WithWindows.Core.Todos;

public static class TodoReminderPolicy
{
    public static IReadOnlyList<TodoItem> GetDue(
        IEnumerable<TodoItem> items,
        DateTimeOffset now)
        => items
            .Where(IsPending)
            .Where(item => item.ReminderAt <= now)
            .OrderBy(item => item.ReminderAt)
            .ToList();

    public static DateTimeOffset? NextWakeAt(
        IEnumerable<TodoItem> items,
        DateTimeOffset now)
        => items
            .Where(IsPending)
            .Select(item => item.ReminderAt)
            .Min();

    private static bool IsPending(TodoItem item)
        => !item.IsCompleted
            && item.ReminderAt is not null
            && item.ReminderDeliveredAt is null;
}
```

- [ ] **Step 4: 运行测试并确认通过**

Run the same filtered test command. Expected: `Passed`，0 failed。

- [ ] **Step 5: 提交**

```powershell
git add src/WithWindows.Core/Todos tests/WithWindows.Core.Tests/TodoReminderPolicyTests.cs
git commit -m "feat(待办): 添加提醒到期策略"
```

---

### Task 2: 待办仓储与持久化

**Files:**
- Create: `src/WithWindows.Core/Todos/TodoStore.cs`
- Create: `tests/WithWindows.Core.Tests/TodoStoreTests.cs`
- Modify: `src/WithWindows.Core/Diagnostics/AppPaths.cs`

**Interfaces:**
- Consumes: `TodoItem`、`TodoDocument`。
- Produces:
  - `public TodoStore(string path, Func<DateTimeOffset>? clock = null)`
  - `public IReadOnlyList<TodoItem> GetAll()`
  - `public TodoItem Add(string title, DateTimeOffset now)`
  - `public bool SetCompleted(string id, bool completed, DateTimeOffset now)`
  - `public bool SetReminder(string id, DateTimeOffset? reminderAt, DateTimeOffset now)`
  - `public bool MarkReminderDelivered(string id, DateTimeOffset deliveredAt)`
  - `public bool Delete(string id)`
  - `public event EventHandler? Changed`
  - `public string? LastLoadError { get; }`

- [ ] **Step 1: 写失败测试**

```csharp
using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public class TodoStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ww-todo-" + Guid.NewGuid().ToString("N"));

    public TodoStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TodoStore NewStore() => new(Path.Combine(_dir, "todos.json"));

    [Fact]
    public void Add_保存后重新加载仍保留标题()
    {
        TodoStore store = NewStore();

        TodoItem added = store.Add("买牛奶", DateTimeOffset.UnixEpoch);
        TodoStore reloaded = NewStore();

        TodoItem item = Assert.Single(reloaded.GetAll());
        Assert.Equal(added.Id, item.Id);
        Assert.Equal("买牛奶", item.Title);
    }

    [Fact]
    public void SetCompleted_完成时清除提醒()
    {
        TodoStore store = NewStore();
        TodoItem item = store.Add("提交周报", DateTimeOffset.UnixEpoch);
        store.SetReminder(item.Id, DateTimeOffset.UnixEpoch.AddHours(1), DateTimeOffset.UnixEpoch);

        store.SetCompleted(item.Id, true, DateTimeOffset.UnixEpoch.AddMinutes(1));

        TodoItem updated = Assert.Single(store.GetAll());
        Assert.True(updated.IsCompleted);
        Assert.Null(updated.ReminderAt);
    }

    [Fact]
    public void Delete_移除待办并持久化()
    {
        TodoStore store = NewStore();
        TodoItem item = store.Add("临时事项", DateTimeOffset.UnixEpoch);

        Assert.True(store.Delete(item.Id));
        Assert.Empty(NewStore().GetAll());
    }

    [Fact]
    public void Load_主文件损坏时从备份恢复()
    {
        TodoStore store = NewStore();
        store.Add("保留我", DateTimeOffset.UnixEpoch);
        store.Add("后一项", DateTimeOffset.UnixEpoch.AddMinutes(1));
        File.WriteAllText(store.Path, "{ 坏");

        TodoItem item = Assert.Single(NewStore().GetAll());
        Assert.Equal("保留我", item.Title);
    }

    [Fact]
    public void MarkReminderDelivered_不再进入到期列表()
    {
        TodoStore store = NewStore();
        TodoItem item = store.Add("提醒", DateTimeOffset.UnixEpoch);
        store.SetReminder(item.Id, DateTimeOffset.UnixEpoch.AddMinutes(1), DateTimeOffset.UnixEpoch);

        store.MarkReminderDelivered(item.Id, DateTimeOffset.UnixEpoch.AddMinutes(1));

        Assert.Empty(TodoReminderPolicy.GetDue(
            store.GetAll(),
            DateTimeOffset.UnixEpoch.AddMinutes(2)));
    }
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter TodoStoreTests
```

Expected: `FAIL`，`TodoStore` 不存在。

- [ ] **Step 3: 实现仓储**

实现要点：

```csharp
public sealed class TodoStore
{
    private readonly object _gate = new();
    private readonly List<TodoItem> _items;
    public event EventHandler? Changed;
    public string Path { get; }
    public string BackupPath => Path + ".bak";
    public string? LastLoadError { get; private set; }

    public IReadOnlyList<TodoItem> GetAll()
    {
        lock (_gate)
            return _items.Select(Clone).ToList();
    }

    public TodoItem Add(string title, DateTimeOffset now)
    {
        string normalized = title.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("待办标题不能为空。", nameof(title));

        TodoItem item = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = normalized,
            CreatedAt = now,
        };

        Mutate(items => items.Add(item));
        return Clone(item);
    }
}
```

同时实现：

- `SetCompleted`：完成时写 `CompletedAt` 并清空 `ReminderAt`、`ReminderDeliveredAt`；重新设为未完成时清空 `CompletedAt`；
- `SetReminder`：只修改未完成项，传入 `null` 时清除提醒与已提醒时间；
- `MarkReminderDelivered`：只更新已存在的未完成项；
- 所有修改先复制当前列表、写入临时 JSON、成功替换后再替换内存快照并触发 `Changed`；
- 新路径不存在时返回空列表；
- 主文件损坏时尝试 `.bak`，两者都失败时返回空列表并设置 `LastLoadError`；
- 不吞掉保存异常，页面负责显示错误。

`AppPaths` 增加：

```csharp
/// <summary>待办数据文件路径。</summary>
public static string TodoPath { get; } = Path.Combine(DataRoot, "todos.json");
```

- [ ] **Step 4: 运行测试并确认通过**

Run the same filtered test command. Expected: all pass。

- [ ] **Step 5: 运行全部 Core 测试**

```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64
```

Expected: 0 failed。

- [ ] **Step 6: 提交**

```powershell
git add src/WithWindows.Core/Todos src/WithWindows.Core/Diagnostics/AppPaths.cs tests/WithWindows.Core.Tests/TodoStoreTests.cs
git commit -m "feat(待办): 添加本地待办存储"
```

---

### Task 3: AppHost 共享仓储与系统通知宿主

**Files:**
- Create: `src/WithWindows.App/Services/TodoReminderHost.cs`
- Modify: `src/WithWindows.App/Services/AppHost.cs`
- Modify: `src/WithWindows.App/App.xaml.cs`

**Interfaces:**
- Consumes: `TodoStore`、`TodoReminderPolicy`、`TodoItem`、`DispatcherQueue`、`AppHost.Log`。
- Produces:
  - `public TodoStore Todos { get; }` on `AppHost`
  - `public TodoReminderHost(AppHost host, DispatcherQueue dispatcher, Action openTodos)`
  - `public void Start()`
  - `public void RefreshSchedule()`
  - `IDisposable`

- [ ] **Step 1: 先接入仓储并确认构建**

修改 `AppHost` 构造函数：

```csharp
Todos = new TodoStore(AppPaths.TodoPath);
if (Todos.LastLoadError is { } todoError)
    Log.Error(todoError);
```

添加属性：

```csharp
public TodoStore Todos { get; }
```

Run:

```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```

Expected: build succeeds。

- [ ] **Step 2: 实现通知宿主**

关键实现：

```csharp
private const string OpenTodosAction = "open_todos";
private readonly DispatcherQueueTimer _timer;
private readonly Action _openTodos;
private bool _registered;

public TodoReminderHost(AppHost host, DispatcherQueue dispatcher, Action openTodos)
{
    _host = host;
    _store = host.Todos;
    _openTodos = openTodos;
    _timer = dispatcher.CreateTimer();
    _timer.IsRepeating = false;
    _timer.Tick += OnTimerTick;
}
```

`Start()`：

1. 订阅 `TodoStore.Changed`；
2. 先订阅 `AppNotificationManager.Default.NotificationInvoked`；
3. 调用 `AppNotificationManager.Default.Register()`；
4. 检查 `AppInstance.GetCurrent().GetActivatedEventArgs()`，若为 `AppNotification` 则排队打开待办页；
5. 调用 `RefreshSchedule()`。

`RefreshSchedule()`：

1. 停止旧计时器；
2. 取 `TodoReminderPolicy.NextWakeAt(...)`；
3. 为空则直接返回；
4. 等待时长为 `max(1 ms, next - UtcNow)`；
5. 等待上限为 1 天，超过则睡 1 天后重新计算；
6. 启动单次计时器。

`OnTimerTick()`：

1. 停止计时器；
2. 对 `GetDue(..., UtcNow)` 中每项调用 `ShowReminder`；
3. 成功显示或确认系统通知不可用后，调用 `MarkReminderDelivered`；
4. 重新计算计划；
5. 捕获异常并写日志，避免 UI 线程异常。

`ShowReminder` 使用：

```csharp
var notification = new AppNotificationBuilder()
    .AddArgument("action", OpenTodosAction)
    .AddText(item.Title)
    .AddText("待办提醒")
    .BuildNotification();
AppNotificationManager.Default.Show(notification);
```

`NotificationInvoked` 处理器通过 `dispatcher.TryEnqueue(_openTodos)` 回到 UI 线程。

不得调用 `UnregisterAll()`；真正退出时仅捕获异常后释放计时器与事件订阅，并保留未打包应用的 COM 激活注册，供下次启动重新订阅。

- [ ] **Step 3: 在 App 中持有宿主**

添加字段：

```csharp
private TodoReminderHost? _todoReminders;
```

主窗口与 `AppHost` 都就绪后创建：

```csharp
_todoReminders = new TodoReminderHost(
    host,
    mainWindow.DispatcherQueue,
    () => mainWindow.ShowAndNavigate(ToolCatalog.TodoId));
_todoReminders.Start();
```

在 `App` 上添加：

```csharp
public static void StopTodoReminders() => ((App)Current)._todoReminders?.Dispose();
```

在 `MainWindow.ExitApplication()` 中，关闭窗口状态与托盘之前调用：

```csharp
App.StopTodoReminders();
```

- [ ] **Step 4: 构建并启动验证通知注册**

```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```

Expected: build succeeds。启动后检查 `%APPDATA%\WithWindows\log.txt` 没有通知注册异常。

- [ ] **Step 5: 提交**

```powershell
git add src/WithWindows.App/Services/TodoReminderHost.cs src/WithWindows.App/Services/AppHost.cs src/WithWindows.App/App.xaml.cs
git commit -m "feat(待办): 添加系统提醒调度"
```

---

### Task 4: 待办页面与基础交互

**Files:**
- Create: `src/WithWindows.App/Views/TodoRowViewModel.cs`
- Create: `src/WithWindows.App/Views/TodoPage.xaml`
- Create: `src/WithWindows.App/Views/TodoPage.xaml.cs`
- Modify: `src/WithWindows.App/Tools/ToolCatalog.cs`

**Interfaces:**
- Consumes: `App.Host.Todos`、`TodoItem`、`TodoDisplayFormatter`（若在 Task 2 中未实现，则在此前补纯函数与测试）。
- Produces:
  - `public sealed class TodoPage : Page`
  - `public static string RouteTag => "todo"`
  - `ToolCatalog.TodoId = "todo"`

- [ ] **Step 1: 写展示格式测试**

在 `TodoReminderPolicyTests.cs` 添加：

```csharp
[Fact]
public void ReminderText_已提醒优先显示已提醒()
{
    TodoItem item = Item("item", Now.AddHours(1));
    item.ReminderDeliveredAt = Now;

    string text = TodoDisplayFormatter.ReminderText(item);

    Assert.Equal("已提醒", text);
}
```

先运行并确认 `TodoDisplayFormatter` 缺失，再实现：

```csharp
public static class TodoDisplayFormatter
{
    public static string ReminderText(TodoItem item)
        => item.ReminderDeliveredAt is not null
            ? "已提醒"
            : item.ReminderAt is { } at
                ? $"提醒：{at.ToLocalTime():M月 d 日 HH:mm}"
                : "未设置提醒";
}
```

- [ ] **Step 2: 注册工具目录**

```csharp
public const string TodoId = "todo";

new ToolEntry(
    TodoId,
    "待办",
    "\uE9D5",
    typeof(TodoPage),
    ShowInNav: true,
    HasHotkey: false),
```

- [ ] **Step 3: 写页面骨架**

页面使用：

- 根 `Grid`，内边距 24；
- 最大宽度 900、水平居中的标题与添加区；
- `TextBox AutomationProperties.AutomationId="TodoNewInput"`；
- `Button AutomationProperties.AutomationId="TodoAddButton"`；
- `ListView AutomationProperties.AutomationId="TodoList"`；
- 空状态 `TextBlock AutomationProperties.AutomationId="TodoEmptyState"`；
- `InfoBar AutomationProperties.AutomationId="TodoErrorBar"`。

行模板包含：

```xml
<CheckBox
    AutomationProperties.AutomationId="{x:Bind CompleteAutomationId}"
    AutomationProperties.Name="完成待办"
    IsChecked="{x:Bind IsCompleted, Mode=OneWay}"
    Tag="{x:Bind Id}"
    Checked="OnCompletedChecked"
    Unchecked="OnCompletedUnchecked" />
```

正文与提醒摘要使用两个 `TextBlock` 分支，已完成版本设置 `TextDecorations="Strikethrough"` 和次要文字色。

- [ ] **Step 4: 实现 code-behind**

`ObservableCollection<TodoRowViewModel> Items` 由 `Reload()` 重建：

```csharp
IEnumerable<TodoItem> items = App.Host!.Todos.GetAll()
    .OrderBy(item => item.IsCompleted)
    .ThenBy(item => item.CreatedAt);
```

添加：

- `Enter` 和「添加」按钮共用 `AddTodo()`；
- 空标题不写入；
- 成功清空输入框并聚焦；
- 仓储异常显示 `TodoErrorBar`。

完成：

- 从 `CheckBox.Tag` 读取 ID；
- 调用 `SetCompleted`；
- 成功后刷新；
- 失败后恢复复选框状态并显示错误。

删除入口先只记录 ID 并调用 `ConfirmDeleteAsync(id)`；Task 5 完成确认对话框。

- [ ] **Step 5: 构建并启动检查**

```powershell
dotnet build WithWindows.sln -p:Platform=x64
```

启动后用：

```powershell
winapp ui inspect -a WithWindows -i
```

确认 `NavItemTodo`、`TodoNewInput`、`TodoAddButton`、`TodoList` 出现在无障碍树。

- [ ] **Step 6: 提交**

```powershell
git add src/WithWindows.App/Views/TodoPage.xaml src/WithWindows.App/Views/TodoPage.xaml.cs src/WithWindows.App/Views/TodoRowViewModel.cs src/WithWindows.App/Tools/ToolCatalog.cs
git commit -m "feat(待办): 添加待办页面"
```

---

### Task 5: 提醒编辑与删除确认

**Files:**
- Create: `src/WithWindows.App/Controls/TodoReminderDialog.cs`
- Modify: `src/WithWindows.App/Views/TodoPage.xaml`
- Modify: `src/WithWindows.App/Views/TodoPage.xaml.cs`

**Interfaces:**
- Consumes: `TodoPage` 中的当前窗口 `XamlRoot`、待办 ID、已有 `ReminderAt`。
- Produces:
  - `public static Task<(bool Save, bool Remove, DateTimeOffset? At)?> ShowAsync(XamlRoot root, TodoItem item)`

- [ ] **Step 1: 实现提醒对话框**

对话框内容：

- `CheckBox AutomationId="TodoReminderEnabled" Content="启用提醒"`；
- `CalendarDatePicker AutomationId="TodoReminderDate"`；
- `TimePicker AutomationId="TodoReminderTime"`；
- 日期和时间控件放在两列 `Grid`；
- 整个内容放进 `ScrollViewer`，`MaxHeight` 根据 `XamlRoot.Size.Height` 限制为 160–520 DIP；
- `ContentDialog.XamlRoot` 必须赋值。

默认值：

- 已有提醒时使用其本地时间；
- 无提醒时使用当前时间加 1 小时，分钟归整到 5 分钟。

保存校验：

- 启用提醒时，所选时间必须晚于当前时间；
- 不满足时设置 `args.Cancel = true` 并显示对话框内错误文本；
- 未勾选时返回移除提醒。

- [ ] **Step 2: 接入铃铛按钮**

`OnReminderClick`：

```csharp
TodoItem? item = App.Host?.Todos.GetAll().FirstOrDefault(x => x.Id == id);
var result = await TodoReminderDialog.ShowAsync(XamlRoot, item);
if (result is { } choice)
{
    App.Host!.Todos.SetReminder(
        id,
        choice.Remove ? null : choice.At,
        DateTimeOffset.UtcNow);
}
```

所有异常写入日志并显示 `TodoErrorBar`。

- [ ] **Step 3: 接入删除确认**

`ConfirmDeleteAsync` 创建 `ContentDialog`：

- 标题「删除待办」；
- 正文「“<标题>”删除后无法恢复。」；
- `PrimaryButtonText="删除"`、`CloseButtonText="取消"`；
- 设置 `XamlRoot`；
- 仅在 `Primary` 结果时调用 `Delete`。

- [ ] **Step 4: 构建并交互验证**

```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```

启动后手动或使用 `winapp ui`：

1. 添加一条待办；
2. 打开提醒对话框；
3. 保存一个未来时间；
4. 重新打开并移除提醒；
5. 删除待办并确认列表恢复空状态。

- [ ] **Step 5: 提交**

```powershell
git add src/WithWindows.App/Controls/TodoReminderDialog.cs src/WithWindows.App/Views/TodoPage.xaml src/WithWindows.App/Views/TodoPage.xaml.cs
git commit -m "feat(待办): 支持提醒设置与删除确认"
```

---

### Task 6: 托盘入口与提醒刷新

**Files:**
- Modify: `src/WithWindows.App/Services/TrayHost.cs`
- Modify: `src/WithWindows.App/Views/TodoPage.xaml.cs`
- Modify: `src/WithWindows.App/Services/TodoReminderHost.cs`

**Interfaces:**
- Consumes: `ToolCatalog.TodoId`、`TodoStore.Changed`。
- Produces: 托盘菜单「待办」以及页面操作后立即重排提醒。

- [ ] **Step 1: 增加托盘入口**

在「记事」与「AI 对话」之后添加：

```csharp
menu.Items.Add(NavigateItem(
    TitleOf(ToolCatalog.TodoId),
    ToolCatalog.TodoId,
    "TrayItemTodo"));
```

- [ ] **Step 2: 订阅存储变更**

`TodoPage.OnLoaded` 订阅 `App.Host.Todos.Changed`，`OnUnloaded` 解除订阅。处理器仅在 `IsLoaded && XamlRoot is not null` 时刷新列表。

`TodoReminderHost.Start()` 同样订阅 `TodoStore.Changed`，每次事件调用 `RefreshSchedule()`。

- [ ] **Step 3: 检查没有待办提醒时计时器停止**

在 `TodoReminderHost` 中保存 `_timer.IsRunning` 的调试日志：

- 有待提醒项：记录下一次唤醒时间；
- 无待提醒项：记录计时器已停止。

日志不得在每次窗口重绘时输出。

- [ ] **Step 4: 构建并检查托盘**

```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```

Expected: build succeeds，托盘菜单代码包含 `TrayItemTodo`。

- [ ] **Step 5: 提交**

```powershell
git add src/WithWindows.App/Services/TrayHost.cs src/WithWindows.App/Views/TodoPage.xaml.cs src/WithWindows.App/Services/TodoReminderHost.cs
git commit -m "feat(待办): 添加托盘入口与提醒重排"
```

---

### Task 7: 文档与最终验证

**Files:**
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/HANDOFF.md`
- Modify: `docs/agents/ui-baseline.md`

**Interfaces:**
- Consumes: 已实现的页面、托盘入口、数据和通知行为。
- Produces: 与代码一致的文档与界面基线。

- [ ] **Step 1: 更新文档**

README 至少补充：

- 当前页面数从四个改为五个；
- 「待办」功能；
- 托盘菜单加入待办；
- `todos.json` 与 `todos.json.bak`；
- 提醒要求应用驻留托盘，重启后补发；
- 核心目录增加 `Todos/`。

CHANGELOG「未发布」的「新增」增加待办与系统提醒。

HANDOFF 更新页面清单、数据目录和待办验证状态。

`ui-baseline.md` 记录 `NavItemTodo`、`TodoNewInput`、`TodoAddButton`、`TodoList`、对话框和托盘入口。

- [ ] **Step 2: 运行完整测试**

```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64
```

Expected: 0 failed。

- [ ] **Step 3: 关闭旧实例并完整构建**

先通过托盘「退出」结束常驻实例，再运行：

```powershell
dotnet build WithWindows.sln -p:Platform=x64
```

Expected: 0 error、0 warning。

- [ ] **Step 4: 启动并验证页面**

```powershell
$exe = "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe"
Start-Process -FilePath (Resolve-Path $exe).Path
```

使用 `winapp ui inspect` 与 `winapp ui screenshot` 检查导航、添加框、按钮、列表和空状态。

- [ ] **Step 5: 验证真实提醒**

1. 添加临时待办「提醒验证」；
2. 设置 1 分钟后提醒；
3. 保持应用运行并收起窗口；
4. 确认系统通知出现；
5. 点击通知，确认主窗口打开并导航到「待办」；
6. 删除临时待办。

若系统通知被系统设置关闭，记录 Windows 通知设置状态和日志，不得虚报端到端通过。

- [ ] **Step 6: 保持应用运行并做独立审查**

应用保持运行。启动独立只读审查：

```powershell
$prompt = @'
你是独立代码审查者。只审 src/** 的待办、存储、计时器与通知改动；docs/** 仅检查明显一致性。不要执行构建，不要结束正在运行的 WithWindows.exe 进程。按 P1/P2/P3 输出发现（文件:行、问题、依据、建议）。
'@
$prompt | codex exec -C "C:\Users\LFN\Documents\Code\with-windows" -s read-only -
```

修复 Critical / Important 发现后重新跑测试、构建和界面检查。

- [ ] **Step 7: 提交最终文档**

```powershell
powershell -ExecutionPolicy Bypass -File scripts/normalize-eol.ps1
git add README.md CHANGELOG.md docs/HANDOFF.md docs/agents/ui-baseline.md
git commit -m "docs(待办): 同步功能与界面基线"
```

