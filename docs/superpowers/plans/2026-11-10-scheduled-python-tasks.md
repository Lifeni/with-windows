# 定时 Python 任务 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 With Windows 中交付应用内定时调度、Python 环境检测、粘贴或关联 `.py` 文件执行、执行历史和完整 WinUI 页面。

**Architecture:** `WithWindows.Core` 负责可测试的任务模型、计划计算、持久化、运行历史和 Win32 Job 薄封装；`WithWindows.App` 负责 Python 探测、进程宿主、调度生命周期、页面和托盘入口。脚本进程通过同一 exe 的隐藏 helper 模式启动，父进程先把 helper 加入 Job Object，再发信号让 helper 启动 Python，从而保证 Python 第一条指令执行前已受控。

**Tech Stack:** .NET 10、C# latest、WinUI 3、xUnit、System.Text.Json、CsWin32、System.Diagnostics.Process。

**Spec:** `docs/design/2026-11-09-scheduled-python-tasks-design.md`

## Global Constraints

- 目标框架固定为 `net10.0-windows10.0.22621.0`，仅 x64，构建与测试必须带 `-p:Platform=x64`。
- 不新增第三方 NuGet 依赖。
- `WithWindows.Core` 禁止引用 `Microsoft.UI.Xaml`。
- 所有运行时数据位于 `%APPDATA%\WithWindows\scheduled-tasks\`。
- Logger、单实例互斥体、调度宿主和 Python 运行器都必须在 `App` 字段生命周期内。
- 所有 P/Invoke 通过 CsWin32，`NativeMethods.txt` 是唯一原生声明入口。
- 中文文案使用中文标点，中英文之间和中文与数字之间保留空格。
- 所有新增交互控件必须有中文 `AutomationProperties.Name` 和稳定 PascalCase `AutomationProperties.AutomationId`。
- 不推送、不发布；只做本地提交。
- 最终构建、测试通过后启动应用并保持运行。

---

### Task 1: Core 任务模型、校验、原子仓储与计划计算

**Files:**
- Create: `src/WithWindows.Core/Scheduling/ScheduledTaskModels.cs`
- Create: `src/WithWindows.Core/Scheduling/ScheduledTaskValidation.cs`
- Create: `src/WithWindows.Core/Scheduling/ScheduledTaskStore.cs`
- Create: `src/WithWindows.Core/Scheduling/ScheduleCalculator.cs`
- Create: `tests/WithWindows.Core.Tests/ScheduledTaskStoreTests.cs`
- Create: `tests/WithWindows.Core.Tests/ScheduleCalculatorTests.cs`

**Interfaces:**
- Produces: `ScheduledTask`、`TaskSchedule`、`ScriptSource`、`ScheduledTaskDocument`。
- Produces: `ScheduledTaskStore.GetAll()`、`Add(...)`、`Update(...)`、`Delete(...)`、`ClaimOccurrence(...)`。
- Produces: `ScheduleCalculator.NextAfter(...)`、`LatestAtOrBefore(...)`、`NeedsLateCatchUp(...)`。
- Produces: `StoreLoadState`，取值为 `Empty`、`Healthy`、`Recovered`、`Faulted`。

- [ ] **Step 1: 写仓储失败测试**

```csharp
[Fact]
public void Load_主文件缺失但备份有效_从备份恢复()
{
    ScheduledTaskStore store = NewStore();
    ScheduledTask task = store.Add(SampleTask("日报"));
    File.Delete(store.Path);

    ScheduledTaskStore reloaded = NewStore();

    Assert.Equal(task.Id, Assert.Single(reloaded.GetAll()).Id);
    Assert.Equal(StoreLoadState.Recovered, reloaded.LoadState);
}

[Fact]
public void Load_主文件和备份均损坏_进入只读故障态且不覆盖()
{
    File.WriteAllText(PathOf("tasks.json"), "{坏");
    File.WriteAllText(PathOf("tasks.json.bak"), "{也坏");
    string before = File.ReadAllText(PathOf("tasks.json"));

    ScheduledTaskStore store = NewStore();

    Assert.Equal(StoreLoadState.Faulted, store.LoadState);
    Assert.Throws<InvalidOperationException>(() => store.Add(SampleTask("禁止写入")));
    Assert.Equal(before, File.ReadAllText(PathOf("tasks.json")));
}
```

- [ ] **Step 2: 运行失败测试**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter ScheduledTaskStoreTests`

Expected: 编译失败，提示 `ScheduledTaskStore` 尚不存在。

- [ ] **Step 3: 实现任务模型与校验**

```csharp
public sealed class ScheduledTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public int ScheduleRevision { get; set; } = 1;
    public DateTimeOffset? LastHandledScheduledFor { get; set; }
    public string? LastHandledRunId { get; set; }
    public TaskSchedule Schedule { get; set; } = new();
    public ScriptSource Source { get; set; } = new();
    public string? PythonPath { get; set; }
    public PythonInterpreterSource PythonSource { get; set; }
    public string? PythonResolvedPath { get; set; }
    public string? PythonPackageFamilyName { get; set; }
    public string? WorkingDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 600;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 4: 实现仓储**

`ScheduledTaskStore` 使用锁、临时文件、`File.Replace` 和 `.bak`。只有主文件与备份都不存在时创建空文档；主文件损坏时移动到带时间戳的 `.corrupt` 文件；恢复失败后设置 `Faulted`，所有写操作抛出 `InvalidOperationException`。

- [ ] **Step 5: 写计划计算失败测试**

```csharp
[Fact]
public void NextAfter_每日计划_返回今天稍后的时间()
{
    var schedule = new TaskSchedule
    {
        Kind = ScheduleKind.Daily,
        TimeOfDay = new TimeOnly(9, 30),
    };

    DateTimeOffset next = ScheduleCalculator.NextAfter(
        schedule,
        new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(8)),
        TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8"))!;

    Assert.Equal(new DateTimeOffset(2026, 11, 10, 9, 30, 0, TimeSpan.FromHours(8)), next);
}
```

- [ ] **Step 6: 实现计划计算**

使用 `TimeZoneInfo` 把本地 `DateOnly + TimeOnly` 转为 `DateTimeOffset`。无效 DST 时间向后寻找首个有效分钟，重复时间选择最早 UTC 时刻。一次性计划使用绝对时间；每天和每周计划使用本地时分。

- [ ] **Step 7: 运行测试并提交**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64`

Expected: 0 failed。

```powershell
git add src/WithWindows.Core/Scheduling tests/WithWindows.Core.Tests/ScheduledTaskStoreTests.cs tests/WithWindows.Core.Tests/ScheduleCalculatorTests.cs
git commit -m "feat(定时任务): 增加任务模型与计划计算"
```

---

### Task 2: Core 运行历史、输出预算与认领状态机

**Files:**
- Create: `src/WithWindows.Core/Scheduling/ScheduledTaskRunModels.cs`
- Create: `src/WithWindows.Core/Scheduling/RunHistoryStore.cs`
- Create: `src/WithWindows.Core/Scheduling/OutputBudget.cs`
- Create: `src/WithWindows.Core/Scheduling/ScheduledTaskClaimPolicy.cs`
- Create: `tests/WithWindows.Core.Tests/RunHistoryStoreTests.cs`
- Create: `tests/WithWindows.Core.Tests/OutputBudgetTests.cs`
- Create: `tests/WithWindows.Core.Tests/ScheduledTaskClaimPolicyTests.cs`

**Interfaces:**
- Produces: `ScheduledTaskRun`、`ScheduledTaskRunStatus`、`ScheduledTaskRunTrigger`。
- Produces: `RunHistoryStore.Upsert(...)`、`GetAll(...)`、`RepairInterruptedRuns(...)`。
- Produces: `OutputBudget.TryAccept(int bytes, out bool exceeded)`。
- Produces: `ScheduledTaskClaimPolicy.GetDueClaims(tasks, now, timeZone)`。

- [ ] **Step 1: 写历史保留测试**

```csharp
[Fact]
public void Upsert_每任务只保留最近一百条()
{
    RunHistoryStore store = NewStore();
    for (int i = 0; i < 105; i++)
        store.Upsert(NewRun("task", i));

    IReadOnlyList<ScheduledTaskRun> runs = store.GetAll("task");
    Assert.Equal(100, runs.Count);
    Assert.Contains(runs, run => run.Id == "run-104");
    Assert.DoesNotContain(runs, run => run.Id == "run-0");
}
```

- [ ] **Step 2: 写输出预算测试**

```csharp
[Fact]
public void OutputBudget_超过上限后仍返回继续消费信号()
{
    var budget = new OutputBudget(limitBytes: 4);
    Assert.True(budget.TryAccept(3, out bool firstExceeded));
    Assert.False(firstExceeded);
    Assert.True(budget.TryAccept(3, out bool secondExceeded));
    Assert.True(secondExceeded);
    Assert.Equal(4, budget.WrittenBytes);
}
```

- [ ] **Step 3: 写到期认领测试**

```csharp
[Fact]
public void GetDueClaims_刷新恰好发生时仍认领今天的每日任务()
{
    ScheduledTask task = DailyTask("task", new TimeOnly(9, 30));
    DateTimeOffset now = AtLocal(2026, 11, 10, 9, 30, 0, 20);

    IReadOnlyList<ScheduledTaskOccurrence> claims =
        ScheduledTaskClaimPolicy.GetDueClaims([task], now, TimeZone);

    ScheduledTaskOccurrence claim = Assert.Single(claims);
    Assert.Equal(AtLocal(2026, 11, 10, 9, 30), claim.ScheduledFor);
}
```

- [ ] **Step 4: 实现历史、预算和认领**

历史写入使用单锁和原子 JSON。`RepairInterruptedRuns` 把遗留 `running` 改为 `interrupted`，并为 `LastHandledRunId` 缺少记录的认领创建 `interrupted` 占位。认领按 `taskId + scheduleRevision + scheduledFor` 去重，并排除 `LastHandledScheduledFor`。

- [ ] **Step 5: 运行测试并提交**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter "RunHistoryStoreTests|OutputBudgetTests|ScheduledTaskClaimPolicyTests"`

Expected: PASS。

```powershell
git add src/WithWindows.Core/Scheduling tests/WithWindows.Core.Tests
git commit -m "feat(定时任务): 增加历史与任务认领"
```

---

### Task 3: Python 环境解析、检测与 Store 包身份

**Files:**
- Create: `src/WithWindows.Core/Scheduling/PythonEnvironmentModels.cs`
- Create: `src/WithWindows.Core/Scheduling/PythonEnvironmentParser.cs`
- Create: `src/WithWindows.App/Services/PythonEnvironmentDetector.cs`
- Create: `tests/WithWindows.Core.Tests/PythonEnvironmentParserTests.cs`

**Interfaces:**
- Produces: `PythonEnvironmentSnapshot`，包含路径、版本、位数、来源、包族名。
- Produces: `PythonEnvironmentParser.ParseProbeJson(string)`。
- Produces: `PythonEnvironmentDetector.DetectAsync(CancellationToken)`。

- [ ] **Step 1: 写解析失败测试**

```csharp
[Fact]
public void ParseProbeJson_拒绝Python2()
{
    const string json =
        """{"executable":"C:\\Python27\\python.exe","major":2,"minor":7,"micro":18,"bits":64}""";

    Assert.Null(PythonEnvironmentParser.ParseProbeJson(json));
}
```

- [ ] **Step 2: 实现结构化探测解析**

探测命令调用：

```text
python.exe -c "import json,struct,sys; print(json.dumps({...}))"
```

输出字段固定为 `executable`、`major`、`minor`、`micro`、`bits`。解析器只接收 Python 3、绝对路径和 32/64 位。

- [ ] **Step 3: 实现检测器**

扫描每个 PATH 段中的 `python.exe`、`py.exe -0p` 输出和常见安装目录。Store 别名只有在注册表包仓库中存在 `PythonSoftwareFoundation.Python.*` 包时才允许探测。所有候选三秒超时并去重。

- [ ] **Step 4: 写 Store 防护测试**

```csharp
[Fact]
public void IsStoreAlias_识别WindowsApps路径()
{
    string path = @"C:\Users\User\AppData\Local\Microsoft\WindowsApps\python.exe";
    Assert.True(PythonEnvironmentParser.IsStoreAliasPath(path));
}
```

- [ ] **Step 5: 运行测试并提交**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter PythonEnvironmentParserTests`

Expected: PASS。

```powershell
git add src tests/WithWindows.Core.Tests/PythonEnvironmentParserTests.cs
git commit -m "feat(定时任务): 检测 Python 解释器"
```

---

### Task 4: Job Object、隐藏 Script Helper 与 Python 输出执行器

**Files:**
- Modify: `src/WithWindows.Core/Interop/NativeMethods.txt`
- Create: `src/WithWindows.Core/Interop/WindowsJob.cs`
- Create: `src/WithWindows.App/Services/ScheduledTaskRunnerModels.cs`
- Create: `src/WithWindows.App/Services/ScriptHelperHost.cs`
- Create: `src/WithWindows.App/Services/PythonScriptRunner.cs`
- Create: `src/WithWindows.App/Services/JobAttachedHelperProcess.cs`
- Modify: `src/WithWindows.App/App.xaml.cs`
- Create: `tests/WithWindows.Core.Tests/WindowsJobTests.cs`

**Interfaces:**
- Produces: `WindowsJob`：创建、加入进程、终止和关闭。
- Produces: `PythonScriptRunner.RunAsync(PythonRunRequest, IProgress<PythonOutputChunk>, CancellationToken)`。
- Produces: `ScriptHelperHost.TryRun(string[] args)`，在 `App.OnLaunched` 的最前面处理。

- [ ] **Step 1: 写 Job 终止失败测试**

```csharp
[Fact]
public async Task Terminate_结束辅助进程树()
{
    using var job = WindowsJob.CreateKillOnClose();
    using var child = Process.Start("cmd.exe", "/c ping 127.0.0.1 -n 30 > nul");
    job.Assign(child);

    job.Terminate();
    Assert.True(await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)));
}
```

- [ ] **Step 2: 在 NativeMethods.txt 声明 Job API**

```text
AssignProcessToJobObject
CreateJobObject
JOBOBJECT_EXTENDED_LIMIT_INFORMATION
JOB_OBJECT_LIMIT
OpenProcess
SetInformationJobObject
TerminateJobObject
```

- [ ] **Step 3: 实现 WindowsJob**

创建带 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` 的匿名 Job；`Assign` 失败时抛异常；`Terminate` 调 `TerminateJobObject` 并等待进程退出；`Dispose` 关闭 Job 句柄。

- [ ] **Step 4: 实现隐藏 helper 模式**

`App.OnLaunched` 在 `--smoke` 之前检测 `--script-helper`。父进程写入一次性 JSON 运行规格，启动同一 exe 的 helper；helper 等待命名事件，父进程加入 Job 并发出事件后，helper 启动 `python.exe -u script.py`，分别把 stdout、stderr 转发到自身标准流。

- [ ] **Step 5: 实现 PythonScriptRunner**

父进程创建命名事件，启动 helper，加入 Job，发出事件，超时或取消时终止 Job。并行读取 helper stdout、stderr，并应用 2 MiB 每流预算。根进程退出后，终止 Job 中残留后代，等待两条读取流 EOF。

- [ ] **Step 6: 运行测试并提交**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter WindowsJobTests`

Expected: PASS。

```powershell
git add src/WithWindows.Core/Interop src/WithWindows.App/Services src/WithWindows.App/App.xaml.cs tests/WithWindows.Core.Tests/WindowsJobTests.cs
git commit -m "feat(定时任务): 增加 Python 受控进程执行"
```

---

### Task 5: 调度协调器、后台宿主与 App 生命周期

**Files:**
- Create: `src/WithWindows.App/Services/ScheduledTaskCoordinator.cs`
- Create: `src/WithWindows.App/Services/ScheduledTaskHost.cs`
- Create: `src/WithWindows.Core/Scheduling/SchedulerLifecycleState.cs`
- Modify: `src/WithWindows.App/Services/AppHost.cs`
- Modify: `src/WithWindows.Core/Diagnostics/AppPaths.cs`
- Modify: `src/WithWindows.App/App.xaml.cs`
- Modify: `src/WithWindows.App/MainWindow.xaml.cs`
- Create: `tests/WithWindows.Core.Tests/SchedulerLifecycleStateTests.cs`

**Interfaces:**
- Produces: `ScheduledTaskCoordinator.Tasks`、`History`、`Changed`、`RunNowAsync(...)`、`SaveTask(...)`、`DeleteTaskAsync(...)`。
- Produces: `ScheduledTaskHost.Start()`、`RefreshSchedule()`、`ShutdownAsync()`。
- Modifies: `App.ShutdownAsync()`，由托盘退出和页面恢复重启共同调用。

- [ ] **Step 1: 写关闭竞态状态测试**

```csharp
[Fact]
public void EnterStopping_拒绝再认领任务()
{
    var state = new SchedulerLifecycleState();
    state.Start();
    state.BeginStop();

    Assert.False(state.TryBeginWork());
    Assert.Equal(SchedulerLifecycle.Stopping, state.Current);
}
```

- [ ] **Step 2: 实现单一写入协调器**

所有任务保存、认领、历史新增/更新、裁剪和删除都通过协调器串行执行。认领先提交到 `tasks.json`，再写入 `runs.json`；认领后历史缺失时由启动修复生成 `interrupted`。

- [ ] **Step 3: 实现后台宿主**

宿主使用单次 `System.Threading.Timer`。每次唤醒枚举到期任务，原子认领后立即重新布防未来时间。刷新、手动运行、关闭均受同一生命周期锁保护。

- [ ] **Step 4: 实现 App 关闭屏障**

`App.ShutdownAsync` 幂等，依次停止待办提醒、停止调度和运行器、等待所有在途任务、再释放窗口状态、托盘和 `AppHost`。托盘退出使用 `async void` 事件处理器并完整捕获异常；页面恢复重启在交接后等待同一屏障再 `Environment.Exit(0)`。

- [ ] **Step 5: 运行测试并提交**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 --filter SchedulerLifecycleStateTests`

Expected: PASS。

```powershell
git add src tests/WithWindows.Core.Tests/SchedulerLifecycleStateTests.cs
git commit -m "feat(定时任务): 接入后台调度与关闭生命周期"
```

---

### Task 6: 定时任务页面与交互

**Files:**
- Create: `src/WithWindows.App/Views/ScheduledTasksPage.xaml`
- Create: `src/WithWindows.App/Views/ScheduledTasksPage.xaml.cs`
- Create: `src/WithWindows.App/Views/ScheduledTaskRowViewModel.cs`
- Create: `src/WithWindows.App/Views/ScheduledTaskRunRowViewModel.cs`
- Create: `src/WithWindows.App/Controls/ScheduledTaskEditorDialog.cs`
- Modify: `src/WithWindows.App/Tools/ToolCatalog.cs`
- Modify: `src/WithWindows.App/Services/TrayHost.cs`

**Interfaces:**
- Consumes: `App.Host.Scheduler`、`ScheduledTaskCoordinator`、`PythonEnvironmentDetector`。
- Produces: 导航入口 `scheduled`、`NavItemScheduled`、页面控件 AutomationId。

- [ ] **Step 1: 增加工具入口**

在 `ToolCatalog` 增加：

```csharp
public const string ScheduledId = "scheduled";
new ToolEntry(ScheduledId, "定时任务", "\uE917", typeof(ScheduledTasksPage), true, false),
```

托盘菜单增加 `TrayItemScheduled`。

- [ ] **Step 2: 构建页面骨架**

页面采用列表加详情。列表显示名称、启用状态、下次执行和最近结果；编辑区包含名称、计划、来源、脚本、解释器、工作目录、超时、运行/停止和历史。

- [ ] **Step 3: 接入文件选择器**

使用 `FileOpenPicker` 选择 `.py`，并通过 `WinRT.Interop.InitializeWithWindow.Initialize` 绑定 `App.MainWindow`。

- [ ] **Step 4: 接入运行状态**

订阅协调器 `Changed`，通过 `DispatcherQueue.TryEnqueue` 刷新列表；页面卸载后解除订阅。所有异步事件处理器用 `try/catch` 包裹。

- [ ] **Step 5: 构建并提交**

Run: `dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64`

Expected: 0 errors。

```powershell
git add src/WithWindows.App docs/agents/ui-baseline.md
git commit -m "feat(定时任务): 增加任务管理页面"
```

---

### Task 7: 数据路径、文档与完整验证

**Files:**
- Modify: `src/WithWindows.Core/Diagnostics/AppPaths.cs`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/development.md`
- Modify: `docs/agents/ui-baseline.md`
- Modify: `docs/design/README.md`

**Interfaces:**
- Produces: `AppPaths.ScheduledTasksDirectory`、`ScheduledTasksPath`、`ScheduledTaskRunsPath`、`ScheduledTaskScriptsDirectory`、`ScheduledTaskLogsDirectory`。

- [ ] **Step 1: 更新数据路径与文档**

记录 Python 不支持时任务不能启用、应用退出后不执行、日志可能含敏感信息。

- [ ] **Step 2: 执行完整测试**

Run: `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64`

Expected: 0 failed。

- [ ] **Step 3: 构建**

Run: `dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64`

Expected: 0 errors。

- [ ] **Step 4: 启动应用并保持运行**

先结束正在运行的旧实例，再使用非阻塞方式启动最终构建。不得在验证结束时关闭应用。

- [ ] **Step 5: 界面自动化检查**

Run:

```powershell
winapp ui inspect -a WithWindows -i
winapp ui screenshot -a WithWindows
```

Expected: 能定位 `NavItemScheduled`、`ScheduledTaskNewButton`、`ScheduledTaskList`、`ScheduledTaskSaveButton`，截图无错乱。

- [ ] **Step 6: 真实脚本验收**

创建一个输出中文并返回 0 的内联脚本，确认历史显示成功；创建一个等待后继续的脚本，确认停止按钮能终止；关联一个外部 `.py`，修改文件后再次执行并确认使用最新内容。

- [ ] **Step 7: 本地提交**

```powershell
git add src tests docs README.md CHANGELOG.md
git commit -m "feat(定时任务): 支持 Python 定时脚本"
```
