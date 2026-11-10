# 定时 Python 任务设计

> 设计日期：2026-11-09
> 修订日期：2026-11-10・补齐生命周期、进程所有权、occurrence 和存储故障边界
> 状态：已被 2026-11-10 的 Lua 脚本方案取代，仅作历史记录

## 1. 目标

在 With Windows 中新增「定时任务」工具页，使用应用内调度执行 Python 脚本，支持以下能力：

- 新建、编辑、启用、停用和删除定时任务；
- 将 Python 代码直接粘贴到应用中；
- 关联外部 `.py` 文件，并在每次执行时使用文件的最新内容；
- 检测本机 Python 3 环境，并为每个任务选择独立解释器；
- 创建一次性、每天和每周计划；
- 立即手动执行、停止正在运行的脚本；
- 记录执行状态、退出码、耗时、标准输出和错误输出；
- 应用启动后恢复调度，应用关闭窗口但驻留托盘时继续执行。

## 2. 非目标

第一版明确不包含以下能力：

- 应用完全退出后继续执行任务；
- 使用 Windows 任务计划程序；
- Cron 表达式和每 N 分钟／小时的间隔计划；
- 自动安装 Python、创建虚拟环境或安装 Python 包；
- 静默提权或以管理员身份运行脚本；
- 脚本沙箱、网络访问限制或文件系统限制；
- Python 2 支持；
- 未安装时自动拉起 Microsoft Store 的 Python 安装页；
- 从远程 URL 下载并执行脚本；
- 独立的密钥管理。脚本中的敏感信息会以明文保存在用户配置中。

## 3. 用户流程

### 3.1 新建粘贴脚本任务

1. 打开「定时任务」页并点击「新建」。
2. 输入任务名称。
3. 选择或确认 Python 解释器。
4. 选择「粘贴脚本」，在等宽字体编辑框中粘贴 Python 代码。
5. 选择一次性、每天或每周计划并设置时间。
6. 设置工作目录和超时时间，或保留默认值。
7. 保存并启用任务。

### 3.2 新建外部文件任务

1. 选择「关联文件」。
2. 点击「选择 Python 文件」，选择 `.py` 文件。
3. 应用保存文件绝对路径，不复制文件。
4. 每次到点后直接运行所选文件的最新内容。
5. 文件被移动或删除时，本次执行记录为「脚本文件不存在」，不执行旧版本缓存。

### 3.3 检测和选择 Python

1. 打开任务编辑区时自动读取已保存的解释器。
2. 用户点击「重新检测」后扫描本机 Python 3 环境。
3. 检测结果以「版本号・架构・来源・路径」展示。
4. 用户可以为每个任务选择不同解释器，以支持虚拟环境。
5. 没有可用解释器时，任务仍可保存，但不能启用或执行。
6. 已启用任务的解释器后来失效时，定时触发会产生一条失败记录，并提示重新选择解释器。

## 4. 方案选择

采用应用内单次截止时间调度和外部 Python 进程执行。

未采用：

- Windows 任务计划程序：可在应用退出后运行，但任务注册、升级、卸载清理、权限和执行记录回传都更复杂，不符合当前「应用内调度」的范围；
- 固定间隔轮询：任务较少时仍然持续唤醒进程，浪费资源；
- 只支持粘贴脚本：修改外部脚本需要重复复制，不符合用户的文件工作流；
- 只关联外部文件：短脚本和独立配置文件没有合适的存放位置；
- 使用 PowerShell 或 CMD：用户明确选择 Python，且 Python 更适合跨文件处理、接口调用和结构化数据。

## 5. 总体架构

```text
ScheduledTasksPage
       │
       ├── 编辑任务与查看历史
       │
ScheduledTaskHost
       │
       ├── ScheduleCalculator
       ├── ScheduledTaskStore
       ├── RunHistoryStore
       └── PythonScriptRunner
               │
               ├── PythonEnvironmentDetector
               └── python.exe
```

`WithWindows.Core` 只包含不依赖 `Microsoft.UI.Xaml` 的模型、计划计算、校验、检测结果解析和存储逻辑。

`WithWindows.App` 负责 Python 进程探测、启动、输出捕获、取消、UI 分发和页面。

## 6. 调度语义

### 6.1 支持的计划

第一版支持三种计划：

- 一次性：指定本地日期和时间；
- 每天：指定本地时分；
- 每周：选择一个或多个星期几，并指定本地时分。

任务按系统当前时区解释和显示。一次性任务保存绝对时间点，每天与每周任务保存本地时分和星期集合。

新任务默认处于停用状态，避免在用户尚未检查解释器和脚本时意外执行。

### 6.2 下一次执行

`ScheduleCalculator` 根据当前时间和任务计划返回下一次执行时间。

计算器提供两类查询：正常布防使用严格未来的 `NextAfter(now)`，睡眠恢复核对时使用 `LatestAtOrBefore(now)`。`LatestAtOrBefore` 不用于应用启动，避免把完全退出期间的周期补跑。

- 正常布防时，每天与每周任务从当前本地时间之后计算；
- 睡眠恢复需要核对已经错过的周期时，使用不晚于当前时间的最近一次有效 occurrence；
- 一次性任务尚未消费且执行时间在未来时返回该时间；
- 一次性任务已消费或执行时间已过去时返回空；
- 系统时区变化后重新计算；
- 夏令时造成不存在的时间时，使用之后第一个有效时刻；
- 夏令时造成重复的时间时，只选择较早的有效时刻。

调度器每次计算都读取当前 `TimeZoneInfo.Local`。系统时间或时区变化时，由独立隐藏消息窗口接收 `WM_TIMECHANGE` 与 `WM_SETTINGCHANGE`，清除时区缓存并重新布防；消息窗口由调度宿主持有并在关闭屏障中释放，不依赖旧计时器的剩余延迟继续推算。睡眠恢复后重新读取实际当前时间，并在统一认领锁内核对所有尚未处理的到期 occurrence。

### 6.3 计时器

`ScheduledTaskHost` 只设置一个单次 `System.Threading.Timer`：

- 每次计算所有启用任务中最早的下一次执行时间；
- 每次布防记录一个只用于唤醒的 `wakeAt` 和布防代次，不把整次唤醒错误地等同于单个任务 occurrence；
- 计时器回调进入统一认领锁后，枚举所有计划中 `scheduledFor <= now` 且尚未处理的 occurrence，并为每个任务认领其中最近的一次；
- 同一任务的认领键是 `taskId + scheduleRevision + scheduledFor`，只能被一个线程持久化认领一次；
- 刷新时先核对当前是否存在尚未认领的到期 occurrence，再决定立即触发还是布防未来时间；
- 旧代次回调只能被新代次替代，不能让新代次的未来计算跳过已经到期的 occurrence；
- 没有待执行任务时停止计时器；
- 单次等待上限为一天，超过上限时先等待一天再重新计算；
- 任务新增、修改、删除、启停、执行完成和应用启动后都重新计算；
- 计时器回调在工作线程执行，不直接操作 XAML；
- UI 更新通过主窗口的 `DispatcherQueue.TryEnqueue` 回到 UI 线程；
- 调度状态机区分 `stopped`、`running`、`stopping` 和 `disposed`，进入 `stopping` 后不再认领 occurrence 或启动新进程。

对于每天和每周任务，如果唤醒时当前时间已经超过 occurrence 的 `scheduledFor`，仍执行一次并标记为 `late`。如果同一任务跨过多个周期，只取当前时间之前最近的一次有效 occurrence；更早的 occurrence 不逐个补跑。

计时器最多提前几毫秒唤醒时不提前执行：若 `scheduledFor` 尚未到达，重新等待剩余时间，不占用 occurrence。超过 `scheduledFor` 在 1 秒内记录为 `scheduled`，超过 1 秒记录为 `late`。

启动时的“不补跑”规则优先：应用新进程第一次布防只选择严格晚于启动快照的未来 occurrence，不把退出期间的周期视为迟到。

宿主同时保存本次启动的 `claimNotBefore`。后续任何任务变更、系统时间事件或刷新都只允许认领不早于该时间下界的新 occurrence，避免一次普通保存把应用关闭期间错过的计划重新补跑。

### 6.4 应用关闭、退出与睡眠

- 关闭主窗口或按 `Alt+F4` 只是隐藏到托盘，调度继续运行；
- 从托盘选择「退出」时执行统一的异步关闭流程；
- 应用完全退出期间错过的计划不补跑；
- 应用下一次启动时，一次性任务的执行时间如果已经过去，则标记为「已过期」；
- 每天与每周任务从启动后的下一次未来时间继续，不补跑关闭期间的多次计划；
- 系统睡眠导致已经排定的计时器迟到时，本次任务执行一次，并记录触发类型为「补跑」；
- 同一任务连续错过多个循环时间时，只执行迟到的那一次，不补跑多次；
- 有脚本正在运行时退出应用，先显示确认提示；确认退出后终止所有脚本进程树；
- 页面自动恢复触发的 `Environment.Exit` 也必须经过同一关闭流程，不能直接绕过调度宿主；
- 应用崩溃或强制终止时，由 Job Object 保证 Python 进程树不会继续存活。

### 6.5 关闭屏障

`ScheduledTaskHost` 由 `App` 唯一持有，应用启动成功后只启动一次。页面只订阅状态和发起编辑，不负责启动或停止宿主。

`App` 提供幂等的 `ShutdownAsync`，把调度宿主、待办提醒宿主和其他后台回调统一登记为可静默对象。托盘退出走 `ExitApplicationAsync`，恢复重启也先等待该关闭屏障，再释放窗口、托盘、`AppHost` 和日志。

新增异步关闭屏障：

1. 把状态原子切换为 `stopping`，拒绝新的计时器认领、手动运行和任务变更；
2. 撤销计时器，并递增布防代次，使已经排队的旧回调失效；
3. 等待待办提醒、调度回调和运行登记任务等在途后台工作退出；
4. 终止全部活动 Python Job Object 并等待进程退出；
5. 把无法正常收尾的运行标记为 `cancelled` 或 `interrupted`；
6. 释放任务仓储、历史仓储、调度宿主，最后再释放 `AppHost` 和日志。

托盘退出、无托盘时的窗口关闭退出、页面恢复重启和应用重新启动前的交接都必须调用这条流程。禁止在 `Timer.Dispose()` 后立即释放日志或仓储。

关闭等待设置上限。超过上限时记录错误并继续强制释放，但不能无限卡住用户退出。

### 6.6 重叠与手动执行

- 同一任务上一次仍在运行时，新触发记录为「跳过」，不启动第二个进程；
- 不同任务允许同时运行；
- 「立即运行」不改变下一次计划；
- 「立即运行」也受同一任务不重叠约束；
- 手动执行可以通过「停止运行」终止进程树；
- 正在运行的任务不能删除，必须先停止。

## 7. Python 环境检测

### 7.1 候选来源

检测器按以下顺序收集候选解释器：

1. 任务已经保存且仍然有效的 `pythonPath`；
2. PATH 中的 `python.exe`；
3. `py.exe -0p` 列出的 Python 3 环境；
4. 常见安装目录中的 `python.exe`；
5. 用户通过文件选择器手动指定的 `python.exe`。

Windows Store 的 `WindowsApps\python.exe` 和 `WindowsApps\python3.exe` 是 App Execution Alias，即使 Python 已安装也可能显示为零字节重解析点，不能只按文件长度判断有效性。

检测 Store Python 时必须先通过 Windows 包注册信息确认对应的 `PythonSoftwareFoundation.Python.*` 包已经安装且状态正常，再启动别名进行探测。未确认包已安装时禁止启动别名，避免误开 Microsoft Store。

如果包查询能力不可用，界面提示“无法确认 Microsoft Store 版 Python”，并继续使用其他候选环境。Store 别名不能绕过解释器有效性验证。

### 7.2 有效性验证

每个候选解释器都必须实际启动并验证：

- 进程能在三秒内完成；
- 主版本号为 3；
- 能返回真实的 `sys.executable`；
- 能返回完整版本号；
- 能返回 x64 或 x86 架构；
- 探测使用结构化输出，包含 `sys.executable`、`sys.version_info` 和 `struct.calcsize("P") * 8`。

超时、无输出、非零退出码、Python 2 和 Store 占位程序都不进入可选列表。

候选路径使用不区分大小写的规范化绝对路径去重。

### 7.3 选择与保存

- 每个任务保存解释器的绝对路径；
- 检测结果可以包含同一台机器上的多个 Python 3 版本；
- 选择后显示 Python 版本和来源；
- 最终保存探测返回且再次验证可用的 `sys.executable`；如果该路径不可访问，则保存已经验证的原解释器路径，并记录来源为 App Execution Alias；
- 保存 Store 别名时同时保存包族名和已验证的实际解释器路径，每次执行前必须先静态确认同包族仍处于正常安装状态；确认失败时禁止启动别名；
- 保存的路径失效时不自动改选另一个环境，避免在用户不知情时替换运行环境；
- 用户可以重新检测、手动选择或继续保留失效路径；
- 检测过程不写注册表、不改 PATH、不安装组件。

## 8. 脚本来源与执行

### 8.1 粘贴脚本

脚本正文以 `tasks.json` 中的 `scriptText` 为唯一数据源。每次执行前，应用从任务文档原子生成运行副本：

```text
%APPDATA%\WithWindows\scheduled-tasks\scripts\<taskId>.py
```

运行副本使用 UTF-8 编码，不能由用户直接编辑。任务保存只写入 `tasks.json`，避免任务文档与脚本文件出现两份相互矛盾的数据。执行前生成副本失败时，本次运行失败，但已保存的任务不受影响。

### 8.2 关联文件

- 任务只保存外部 `.py` 文件的绝对路径；
- 文件选择器只允许 `.py`；
- 每次执行前重新检查文件是否存在；
- 执行时使用磁盘上的最新内容，不缓存脚本正文；
- 文件不存在、无读取权限或不是普通文件时，不启动 Python，直接记录失败；
- 外部文件模式不能把路径修改为空；
- 删除任务不会删除外部 Python 文件。

### 8.3 工作目录

- 关联文件任务默认使用脚本所在目录；
- 粘贴脚本任务默认使用用户主目录；
- 用户可以为每个任务指定其他工作目录；
- 指定目录不存在时，执行前失败并显示明确提示；
- 工作目录只影响子进程，不改变应用自身工作目录。

### 8.4 进程参数与环境

执行器直接启动选定的 `python.exe`，不使用 shell：

```text
<python.exe> -u <script.py>
```

同时设置：

```text
PYTHONUTF8=1
PYTHONIOENCODING=utf-8
PYTHONUNBUFFERED=1
```

进程使用以下配置：

- `UseShellExecute=false`；
- `CreateNoWindow=true`；
- 重定向标准输出、标准错误和标准输入；
- 标准输入立即关闭，脚本不能等待交互输入；
- 脚本路径和参数通过 `ArgumentList` 传递，不手工拼接命令行；
- 父进程不继承不需要的 Shell 行为；
- 退出码为 0 视为成功，其他退出码视为失败。

### 8.5 超时、停止与输出

- 默认超时为 600 秒；
- 允许范围为 5 秒至 86400 秒；
- 每次运行创建独立 Windows Job Object，并设置 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`；
- 父进程启动同一 exe 的隐藏 script helper 模式；helper 在收到命名事件前不会启动 Python；
- 父进程先把 helper 加入并验证 Job Object，再发出启动事件，Python 在受控 helper 下启动，保证脚本第一条指令前已经受控；
- Job 创建、helper 启动、加入 Job 或发出启动事件失败时终止所有已创建句柄并记录失败，不允许在无 Job 状态下继续运行；
- 超时后终止整个 Job，并等待进程和输入输出读取任务退出；
- 用户点击停止时执行同样的 Job 终止流程；
- 标准输出和标准错误分别并行读取，避免死锁；
- 输出边运行边写入日志，不先缓存全部内容；
- stdout 与 stderr 各自保留日志；
- 每个流最多保存 2 MiB，合计 4 MiB；
- 任一流达到上限后追加「输出已截断」，两个读取循环都必须继续读取并丢弃后续字节，避免子进程被管道阻塞；
- 主 Python 进程退出后，给输出排空一个短暂宽限期；如果后代表仍持有输出管道，则终止 Job 中残留进程，再等待两个读取任务 EOF；
- 只有根进程退出、残留进程处理完成且 stdout、stderr 读取任务都到达 EOF 后，才提交最终执行状态；
- 截断按完整 UTF-8 文本行进行，不允许截断在多字节字符中间；
- 输出日志使用 UTF-8。

## 9. 数据模型

### 9.1 任务文档

任务保存在独立文件中，不加入 `AppConfig`，因此配置模型版本保持 v5。

```json
{
  "version": 1,
  "tasks": [
    {
      "id": "d35ec245bf9c4cf1888649d29c84a966",
      "name": "生成日报",
      "enabled": true,
      "scheduleRevision": 1,
      "lastHandledScheduledFor": null,
      "lastHandledRunId": null,
      "schedule": {
        "kind": "weekly",
        "oneTimeRunAt": null,
        "timeOfDay": "09:30:00",
        "daysOfWeek": ["Monday", "Friday"],
        "oneTimeConsumedAt": null
      },
      "source": {
        "kind": "file",
        "scriptText": null,
        "scriptPath": "C:\\Users\\Example\\Scripts\\daily_report.py"
      },
      "pythonPath": "C:\\Users\\Example\\Scripts\\.venv\\Scripts\\python.exe",
      "pythonSource": "manual",
      "pythonResolvedPath": "C:\\Users\\Example\\Scripts\\.venv\\Scripts\\python.exe",
      "pythonPackageFamilyName": null,
      "workingDirectory": "C:\\Users\\Example\\Scripts",
      "timeoutSeconds": 600,
      "createdAt": "2026-11-09T10:00:00+08:00",
      "updatedAt": "2026-11-09T10:00:00+08:00"
    }
  ]
}
```

字段约束：

- `id` 使用 `Guid.NewGuid().ToString("N")`，加载时必须是 32 位 GUID；
- 名称去除首尾空白后不能为空；
- 创建任务时 `scheduleRevision` 为 1，计划本身发生修改时递增；
- 计划修订号变化后，旧修订号的 occurrence 不再参与认领；
- `lastHandledScheduledFor` 与 `lastHandledRunId` 在预写认领时一起持久化，用于时间回拨或重启后的去重；
- `scriptText` 与 `scriptPath` 只在对应的来源模式下有效；
- `pythonSource` 记录 `path`、`py_launcher`、`store_alias` 或 `manual`；
- `pythonResolvedPath` 保存探测返回并验证过的 `sys.executable`；
- `pythonPackageFamilyName` 仅在 `pythonSource=store_alias` 时保存对应包族名；
- `timeoutSeconds` 在加载时限制到 5 至 86400；
- `daysOfWeek` 去重并至少包含一天；
- 一次性计划使用 `oneTimeRunAt`；
- 一次性计划触发后写入 `oneTimeConsumedAt`，即使执行失败或因重叠被跳过也不自动重试；
- 未知枚举值视为无效任务并记录日志，不执行脚本。

### 9.2 执行记录

执行记录保存在独立文档中：

```json
{
  "version": 1,
  "runs": [
    {
      "id": "20261109T093000123_8f10c2d4",
      "taskId": "d35ec245bf9c4cf1888649d29c84a966",
      "taskName": "生成日报",
      "trigger": "scheduled",
      "scheduledFor": "2026-11-09T09:30:00+08:00",
      "startedAt": "2026-11-09T09:30:00.215+08:00",
      "finishedAt": "2026-11-09T09:30:05.830+08:00",
      "status": "succeeded",
      "exitCode": 0,
      "pythonPath": "C:\\Users\\Example\\Scripts\\.venv\\Scripts\\python.exe",
      "pythonVersion": "3.12.8",
      "scriptSource": "file",
      "scriptPath": "C:\\Users\\Example\\Scripts\\daily_report.py",
      "stdoutLogPath": "C:\\Users\\Example\\AppData\\Roaming\\WithWindows\\scheduled-tasks\\logs\\d35ec245bf9c4cf1888649d29c84a966\\20261109T093000123_8f10c2d4.stdout.log",
      "stderrLogPath": "C:\\Users\\Example\\AppData\\Roaming\\WithWindows\\scheduled-tasks\\logs\\d35ec245bf9c4cf1888649d29c84a966\\20261109T093000123_8f10c2d4.stderr.log",
      "errorMessage": null
    }
  ]
}
```

状态枚举：

- `running`：启动记录已经持久化，正在准备或运行 Python 进程；
- `succeeded`：退出码为 0；
- `failed`：退出码非 0或执行前校验失败；
- `timedOut`：超过任务超时时间；
- `cancelled`：用户主动停止；
- `skipped`：同一任务已有运行实例；
- `interrupted`：应用异常退出，启动时修复遗留的运行中记录。

触发类型：

- `scheduled`：按计划准时触发；
- `late`：计时器因睡眠或系统繁忙迟到后补跑一次；
- `manual`：用户点击立即运行。

每次新执行使用「UTC 时间戳 + 随机后缀」生成唯一 ID。

执行记录采用预写顺序：

1. 生成运行 ID；
2. 对定时触发，在 `tasks.json` 中原子写入 `scheduleRevision`、`lastHandledScheduledFor` 和 `lastHandledRunId`，完成 occurrence 认领；
3. 在 `runs.json` 中原子写入 `running`；
4. 只有第 2、3 步成功后，才生成内联脚本副本、创建受控 Job 并启动 Python；
5. 进程启动失败时原子更新为 `failed`；
6. 进程正常结束、超时或取消后，等待完整进程树和输出排空，再原子写入最终状态。

应用启动时如果发现 `lastHandledRunId` 不存在对应执行记录，创建一条 `interrupted` 记录并注明“认领后未完成启动”；任务仍视为已消费该 occurrence，不重复执行。

如果无法持久化认领或 `running` 记录，本次执行不启动。这样不会出现“磁盘上完全没有执行记录但脚本已经运行”的情况。

### 9.3 存储路径

```text
%APPDATA%\WithWindows\scheduled-tasks\
├─ tasks.json
├─ tasks.json.bak
├─ runs.json
├─ runs.json.bak
├─ scripts\
│  └─ <taskId>.py
└─ logs\
   └─ <taskId>\
      ├─ <runId>.stdout.log
      └─ <runId>.stderr.log
```

- `tasks.json` 与 `runs.json` 使用临时文件加原子替换；
- 保存时保留上一份 `.bak`；
- 只有主文件和备份都不存在时才创建空文档；
- 主文件不存在但备份有效时，从备份恢复；
- 主文件损坏时先隔离为带时间戳的 `.corrupt` 文件，再回退备份，并在界面提示曾从备份恢复；
- 从备份恢复时直接生成新的健康主文件，不能把损坏主文件轮换覆盖为新的 `.bak`；
- 主文件和备份均损坏、被占用或无权限时进入只读故障态，保留原文件并禁止自动保存；
- 只读故障态提供「重试」「导入」「显式重置」操作，用户确认重置前不得覆盖 `tasks.json` 或 `runs.json`；
- 删除脚本和日志前必须规范化绝对路径，并确认仍位于对应的 `scripts/` 或 `logs/<taskId>/` 根目录内；
- 每次运行记录写入成功后更新内存状态；
- 应用启动时把所有遗留的 `running` 记录改为 `interrupted`；
- 每个任务默认保留最近 100 条记录，超出后删除最旧记录及对应日志；
- 裁剪时先原子提交新的 `runs.json`，再尽力删除不再被引用的日志；启动时清理孤立日志；
- 删除任务时删除粘贴脚本、执行记录和日志，不删除外部 `.py` 文件。

所有任务和运行状态变更都进入单一协调器，避免两个任务同时改写 `runs.json`。删除正在运行的任务必须先停止并等待 Job 结束；任务定义的删除提交之后，才清理运行记录、运行副本和日志。删除两步之间崩溃时，由启动清理移除已无任务引用的孤儿数据。

## 10. 代码边界

### 10.1 WithWindows.Core

新增 `Scheduling` 目录：

- `ScheduledTask`、`TaskSchedule`、`ScriptSource`；
- `ScheduledTaskDocument`；
- `ScheduledTaskStore`；
- `ScheduleCalculator`；
- `ScheduledOccurrence`；
- `SchedulerLifecycleState`；
- `ScheduledTaskValidation`；
- `ScheduledTaskRun`、`ScheduledTaskRunDocument`；
- `RunHistoryStore`；
- `RunStateMachine`；
- `OutputBudget`；
- `StoreLoadState`；
- `PythonEnvironmentSnapshot`；
- `PythonEnvironmentParser`；
- `ScheduledTaskDisplayFormatter`。

其中 occurrence 计算与认领、状态机、校验、解析、输出预算和历史裁剪全部为纯逻辑，由 xunit 覆盖。

### 10.2 WithWindows.App

新增：

- `Views/ScheduledTasksPage.xaml` 与 code-behind；
- `Services/ScheduledTaskHost.cs`；
- `Services/ScheduledTaskCoordinator.cs`；
- `Services/PythonEnvironmentDetector.cs`；
- `Services/PythonScriptRunner.cs`；
- `Services/ScriptHelperHost.cs`；
- `Interop/WindowsJob.cs`；
- `Interop/SystemTimeChangeMonitor.cs`；
- `Controls/ScheduledTaskEditor` 或页面内编辑区；
- 执行历史详情视图。

`AppHost` 持有任务仓储与历史仓储，保证页面卸载后调度仍可用。

`ScheduledTaskHost` 和 `PythonScriptRunner` 都由 `App` 字段持有到进程退出，禁止写成局部对象或因失去引用而停止工作。

`ScheduledTaskCoordinator` 是任务、认领和运行历史的唯一写入者。调度、页面和进程完成回调只能向协调器提交命令，不能各自读写共享 JSON。

调度器通过可注入的时钟、时区信息和进程适配器测试。进程适配器只封装 `System.Diagnostics.Process`、流读取和 Job Object，不包含调度决策。

Job Object 和进程加入所需的 Win32 API 必须声明在 `NativeMethods.txt` 中并由 CsWin32 生成，禁止手写 `DllImport`。如果 Job 创建、helper 启动、配置或进程加入失败，不得退化为“无 Job 继续运行”。

### 10.3 工具入口

- `ToolCatalog` 增加 `scheduled` 页面项，标题为「定时任务」；
- 托盘菜单增加「定时任务」入口；
- 第一版不增加全局热键；
- 不修改 `AppConfig.KnownToolIds`；
- 不修改窗口默认页面选项。

图标实现前必须从 WinUI Gallery 的官方图标表选择，不凭记忆填写码点。

## 11. 页面设计

「定时任务」页使用列表加详情的结构：

- 左侧显示任务名称、启用状态、下一次执行时间和最近执行结果；
- 右侧编辑当前任务；
- 没有任何任务时显示空状态和「新建定时任务」；
- 页面顶部提供新建、保存、启用开关、立即运行和停止运行；
- Python 不可用、解释器失效、脚本文件缺失或应用未设置开机自启时，显示 `InfoBar`；
- 脚本编辑区使用等宽字体，支持多行文本和直接粘贴；
- 关联文件模式显示完整路径以及「选择文件」和「打开所在文件夹」；
- 执行历史位于编辑区下方，选择一条记录可分别查看 stdout 和 stderr；
- 运行中的任务显示进度环和持续时间；
- 窄窗口下列表与详情按顺序纵向排列，不横向挤压脚本编辑区。

新增可交互控件必须同时提供中文 `AutomationProperties.Name` 和稳定的英文 PascalCase `AutomationProperties.AutomationId`。

主要自动化标识约定：

| 控件 | AutomationId |
|---|---|
| 导航入口 | `NavItemScheduled` |
| 新建任务 | `ScheduledTaskNewButton` |
| 任务列表 | `ScheduledTaskList` |
| 任务名称 | `ScheduledTaskNameBox` |
| 启用开关 | `ScheduledTaskEnabledToggle` |
| 保存 | `ScheduledTaskSaveButton` |
| 删除 | `ScheduledTaskDeleteButton` |
| 立即运行 | `ScheduledTaskRunNowButton` |
| 停止运行 | `ScheduledTaskStopButton` |
| 计划类型 | `ScheduledTaskScheduleTypeComboBox` |
| 一次性日期 | `ScheduledTaskDatePicker` |
| 执行时间 | `ScheduledTaskTimePicker` |
| 星期选择 | `ScheduledTaskWeekdaySelector` |
| 脚本来源 | `ScheduledTaskSourceSelector` |
| 粘贴脚本编辑区 | `ScheduledTaskScriptEditor` |
| 选择 Python 文件 | `ScheduledTaskFilePickerButton` |
| Python 文件路径 | `ScheduledTaskFilePath` |
| 打开所在文件夹 | `ScheduledTaskOpenFolderButton` |
| Python 解释器 | `ScheduledTaskInterpreterComboBox` |
| 重新检测 Python | `ScheduledTaskDetectPythonButton` |
| 手动选择解释器 | `ScheduledTaskBrowsePythonButton` |
| 工作目录 | `ScheduledTaskWorkingDirectoryBox` |
| 超时时间 | `ScheduledTaskTimeoutBox` |
| Python 状态提示 | `ScheduledTaskPythonStatusBar` |
| 页面错误提示 | `ScheduledTaskErrorBar` |
| 执行历史 | `ScheduledTaskRunHistoryList` |
| 执行标准输出 | `ScheduledTaskStdoutOutput` |
| 执行错误输出 | `ScheduledTaskStderrOutput` |
| 打开日志目录 | `ScheduledTaskOpenLogFolderButton` |
| 托盘入口 | `TrayItemScheduled` |
| 任务列表项 | `ScheduledTaskListItem_<taskId>` |
| 执行历史项 | `ScheduledTaskRunItem_<runId>` |

## 12. 错误处理

- 任务文件或历史文件损坏：回退 `.bak`；仍失败则进入只读故障态，不允许自动覆盖；
- 保存失败：保留内存中的编辑内容，页面显示错误，不假装保存成功；
- Python 未安装：禁止启用和运行，提供重新检测、手动选择和安装提示；
- 解释器失效：执行前失败并记录实际路径；
- 外部脚本不存在：执行前失败，不产生 Python 进程；
- 工作目录不存在：执行前失败；
- 启动进程失败：记录异常消息和失败状态；
- 加入 Job Object 失败：立即终止尚未受控的 Python 进程并记录失败，不继续执行脚本；
- 输出或历史写入失败：记录应用日志，并尽量保留当前运行结果；
- 调度计算异常：跳过该任务并记录错误，不影响其他任务；
- UI 页面已经卸载：后台调度继续，禁止后台线程直接触碰 XAML；
- 应用退出：阻止新触发，等待在途回调，终止所有活动 Job Object，再释放仓储和日志；
- 系统时间或时区变化：清除时区缓存，重新布防，不执行已经失效的旧 occurrence；
- 输出达到上限：继续排空 stdout／stderr，只停止写入磁盘；
- 应用异常终止：依赖 Windows Job Object 回收 Python 进程树，下次启动修复遗留的 `running` 记录。

## 13. 安全与隐私

- Python 脚本以当前用户权限运行，拥有该用户可访问的文件、网络和注册表权限；
- 应用不是脚本沙箱，界面首次创建任务时必须明确提示；
- 第一版不提供自动提权，也不会绕过 UAC；
- 关联文件模式只执行用户明确选择的本地 `.py` 文件；
- 不提供远程脚本 URL；
- 粘贴脚本和外部脚本路径会以明文保存在 `%APPDATA%\WithWindows\`；
- 执行日志可能包含脚本输出的敏感信息，界面必须提示用户检查后再分享日志；
- 第一版不负责安装依赖，脚本缺少包时由 Python 的异常和错误输出体现。

## 14. 测试与验收

### 14.1 Core 单元测试

- 新建任务、编辑、启停、删除和保存往返；
- 主文件损坏时从 `.bak` 恢复；
- 主文件缺失但备份有效时从备份恢复；
- 损坏主文件被隔离，恢复过程不会用损坏内容覆盖有效备份；
- 主文件和备份都损坏时进入只读故障态，且后续保存不会覆盖原文件；
- 一次性任务未来、已过期、已消费和无效时间；
- 每日计划跨午夜、跨日期和时间变更；
- 每周多选日期、星期去重和空星期校验；
- 夏令时不存在与重复时间的处理；
- 计时器回调在 `scheduledFor` 之后数毫秒到达仍能认领原 occurrence；
- 计时器提前数毫秒唤醒时重新等待，不提前执行或消费 occurrence；
- 同一 occurrence 被重复刷新或并发回调时只能认领一次；
- 刷新恰好发生时不会跳过已经到期的 occurrence；
- 同一时刻多个任务到期时全部进入认领集合，而不是只保留一个任务；
- 睡眠跨过多个周期时选择最近一次有效 occurrence，并只运行一次；
- 系统时间前后调整和时区变化使旧布防代次失效；
- 系统时间回拨并重启后，`lastHandledScheduledFor` 阻止同一 occurrence 再次执行；
- 超时范围裁剪和无效枚举拒绝；
- 执行历史新增、状态更新、100 条上限和日志裁剪；
- 遗留 `running` 记录转换为 `interrupted`；
- Python 检测输出解析；
- Store 包未安装时不启动 App Execution Alias；
- Store 包已安装时允许探测并保存经过验证的解释器；
- Store Python 选中后包被卸载时，执行前静态失败且不启动别名；
- 不同解释器版本、架构和重复路径去重；
- 输出预算达到上限后仍继续排空并保持纯状态机可测试；
- 关联文件路径、工作目录和 Python 路径校验；
- 执行记录显示文本和下次执行时间格式化。

### 14.2 平台与集成验收

- Job Object 的直接子进程终止由 xunit 覆盖；
- helper 握手、真实 Python 输出、计划触发、外部文件、手动停止和删除通过构建产物手工集成验证；
- 自动化测试不依赖开发机上的 Python 安装。

### 14.3 进程与界面验收

- 自动检测当前机器上支持范围内的有效 Python 3 环境；
- 粘贴脚本可以输出中文、标准错误并以是否零退出码判定结果；
- 关联包含中文和空格的 `.py` 路径可以正常执行；
- 修改外部文件后下一次执行使用最新内容；
- 脚本抛异常时记录非零退出码和错误输出；
- 超时和手动停止都能终止进程树；
- 同一任务重叠触发记录为「跳过」；
- 进程输出超过 2 MiB 后继续排空，不阻塞进程；
- stdout 与 stderr 分别可在历史中查看；
- 一次性任务在应用关闭期间过期后不补跑；
- 睡眠后迟到计时器只补跑一次；
- 应用退出时活动脚本被终止；
- 页面恢复重启时活动脚本同样被终止，不留下孤儿进程；
- `winapp ui inspect` 能找到导航入口和主要自动化标识；
- `winapp ui screenshot` 检查浅色、深色和窄窗口布局无错乱；
- 最终应用保持运行，供用户直接查看。

### 14.4 文档同步

- README 增加定时 Python 任务、Python 依赖和应用退出后不执行的边界；
- CHANGELOG 最新版本记录新增功能；
- `docs/development.md` 增加任务、脚本、历史和日志数据路径；
- `docs/agents/ui-baseline.md` 记录新页面和稳定自动化标识；
- 实现完成后复查本文与代码是否一致。

## 15. 实施顺序

1. 建立 Core 模型、计划计算、校验、仓储和执行历史测试；
2. 实现 Python 环境检测及其解析测试；
3. 实现 Python 进程运行器、输出捕获、超时和取消；
4. 实现 `ScheduledTaskHost` 并接入 Application 生命周期；
5. 实现任务列表、编辑区、解释器选择、文件选择和执行历史；
6. 接入工具目录与托盘入口；
7. 完成构建、测试、真实 Python 运行、睡眠与退出边界验证；
8. 同步 README、CHANGELOG、开发文档和界面基线；
9. 启动并保持应用运行，交给用户查看。
