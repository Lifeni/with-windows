# 定时 Python 任务设计

> 设计日期：2026-11-09
> 状态：方案已确认，待实现

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

`ScheduleCalculator` 根据当前时间和任务计划返回严格晚于当前时间的下一次执行时间。

- 每天与每周任务从当前本地时间之后计算；
- 一次性任务尚未消费且执行时间在未来时返回该时间；
- 一次性任务已消费或执行时间已过去时返回空；
- 系统时区变化后重新计算；
- 夏令时造成不存在的时间时，使用之后第一个有效时刻；
- 夏令时造成重复的时间时，只选择较早的有效时刻。

### 6.3 计时器

`ScheduledTaskHost` 只设置一个单次 `System.Threading.Timer`：

- 每次计算所有启用任务中最早的下一次执行时间；
- 没有待执行任务时停止计时器；
- 单次等待上限为一天，超过上限时先等待一天再重新计算；
- 任务新增、修改、删除、启停、执行完成和应用启动后都重新计算；
- 计时器回调在工作线程执行，不直接操作 XAML；
- UI 更新通过主窗口的 `DispatcherQueue.TryEnqueue` 回到 UI 线程。

### 6.4 应用关闭、退出与睡眠

- 关闭主窗口或按 `Alt+F4` 只是隐藏到托盘，调度继续运行；
- 从托盘选择「退出」时停止调度；
- 应用完全退出期间错过的计划不补跑；
- 应用下一次启动时，一次性任务的执行时间如果已经过去，则标记为「已过期」；
- 每天与每周任务从启动后的下一次未来时间继续，不补跑关闭期间的多次计划；
- 系统睡眠导致已经排定的计时器迟到时，本次任务执行一次，并记录触发类型为「补跑」；
- 同一任务连续错过多个循环时间时，只执行迟到的那一次，不补跑多次；
- 有脚本正在运行时退出应用，先显示确认提示；确认退出后终止所有脚本进程树。

### 6.5 重叠与手动执行

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

Windows Store 的 `WindowsApps\python.exe` 和 `WindowsApps\python3.exe` 默认忽略，因为它们在未安装 Python 时可能是零字节占位文件，并可能打开 Microsoft Store。

### 7.2 有效性验证

每个候选解释器都必须实际启动并验证：

- 进程能在三秒内完成；
- 主版本号为 3；
- 能返回真实的 `sys.executable`；
- 能返回完整版本号；
- 能返回 x64 或 x86 架构。

超时、无输出、非零退出码、Python 2 和 Store 占位程序都不进入可选列表。

候选路径使用不区分大小写的规范化绝对路径去重。

### 7.3 选择与保存

- 每个任务保存解释器的绝对路径；
- 检测结果可以包含同一台机器上的多个 Python 3 版本；
- 选择后显示 Python 版本和来源；
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
- 超时后调用 `Kill(entireProcessTree: true)`；
- 用户点击停止时执行同样的进程树终止；
- 标准输出和标准错误并行读取，避免死锁；
- 输出边运行边写入日志，不先缓存全部内容；
- 单次运行输出上限为 4 MiB，达到上限后继续运行进程，但日志追加「输出已截断」并停止保存后续内容；
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
      "workingDirectory": "C:\\Users\\Example\\Scripts",
      "timeoutSeconds": 600,
      "createdAt": "2026-11-09T10:00:00+08:00",
      "updatedAt": "2026-11-09T10:00:00+08:00"
    }
  ]
}
```

字段约束：

- `id` 使用 `Guid.NewGuid().ToString("N")`；
- 名称去除首尾空白后不能为空；
- `scriptText` 与 `scriptPath` 只在对应的来源模式下有效；
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
      "outputLogPath": "C:\\Users\\Example\\AppData\\Roaming\\WithWindows\\scheduled-tasks\\logs\\d35ec245bf9c4cf1888649d29c84a966\\20261109T093000123_8f10c2d4.log",
      "errorMessage": null
    }
  ]
}
```

状态枚举：

- `running`：进程已启动，尚未结束；
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
      └─ <runId>.log
```

- `tasks.json` 与 `runs.json` 使用临时文件加原子替换；
- 保存时保留上一份 `.bak`；
- 主文件损坏时回退备份；
- 主文件和备份都不可用时从空文档开始，并记录错误日志；
- 每次运行记录写入成功后更新内存状态；
- 应用启动时把所有遗留的 `running` 记录改为 `interrupted`；
- 每个任务默认保留最近 100 条记录，超出后删除最旧记录及对应日志；
- 删除任务时删除粘贴脚本、执行记录和日志，不删除外部 `.py` 文件。

## 10. 代码边界

### 10.1 WithWindows.Core

新增 `Scheduling` 目录：

- `ScheduledTask`、`TaskSchedule`、`ScriptSource`；
- `ScheduledTaskDocument`；
- `ScheduledTaskStore`；
- `ScheduleCalculator`；
- `ScheduledTaskValidation`；
- `ScheduledTaskRun`、`ScheduledTaskRunDocument`；
- `RunHistoryStore`；
- `PythonEnvironmentSnapshot`；
- `PythonEnvironmentParser`；
- `ScheduledTaskDisplayFormatter`。

其中 `ScheduleCalculator`、校验、解析和历史裁剪全部为纯逻辑，由 xunit 覆盖。

### 10.2 WithWindows.App

新增：

- `Views/ScheduledTasksPage.xaml` 与 code-behind；
- `Services/ScheduledTaskHost.cs`；
- `Services/PythonEnvironmentDetector.cs`；
- `Services/PythonScriptRunner.cs`；
- `Controls/ScheduledTaskEditor` 或页面内编辑区；
- 执行历史详情视图。

`AppHost` 持有任务仓储与历史仓储，保证页面卸载后调度仍可用。

`ScheduledTaskHost` 和 `PythonScriptRunner` 都由 `App` 字段持有到进程退出，禁止写成局部对象或因失去引用而停止工作。

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
- 执行历史位于编辑区下方，选择一条记录可查看输出和错误；
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
| 执行输出 | `ScheduledTaskRunOutput` |
| 打开日志目录 | `ScheduledTaskOpenLogFolderButton` |

## 12. 错误处理

- 任务文件或历史文件损坏：回退 `.bak`；仍失败则从空文档启动并记录错误；
- 保存失败：保留内存中的编辑内容，页面显示错误，不假装保存成功；
- Python 未安装：禁止启用和运行，提供重新检测、手动选择和安装提示；
- 解释器失效：执行前失败并记录实际路径；
- 外部脚本不存在：执行前失败，不产生 Python 进程；
- 工作目录不存在：执行前失败；
- 启动进程失败：记录异常消息和失败状态；
- 输出或历史写入失败：记录应用日志，并尽量保留当前运行结果；
- 调度计算异常：跳过该任务并记录错误，不影响其他任务；
- UI 页面已经卸载：后台调度继续，禁止后台线程直接触碰 XAML；
- 应用退出：停止计时器，终止所有活动进程树，再释放仓储和日志。

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
- 主文件和备份都损坏时从空文档启动；
- 一次性任务未来、已过期、已消费和无效时间；
- 每日计划跨午夜、跨日期和时间变更；
- 每周多选日期、星期去重和空星期校验；
- 夏令时不存在与重复时间的处理；
- 超时范围裁剪和无效枚举拒绝；
- 执行历史新增、状态更新、100 条上限和日志裁剪；
- 遗留 `running` 记录转换为 `interrupted`；
- Python 检测输出解析；
- Windows Store 占位路径过滤；
- 不同解释器版本、架构和重复路径去重；
- 关联文件路径、工作目录和 Python 路径校验；
- 执行记录显示文本和下次执行时间格式化。

### 14.2 进程与界面验收

- 自动检测当前机器上的全部有效 Python 3 环境；
- 粘贴脚本可以输出中文、标准错误并以是否零退出码判定结果；
- 关联包含中文和空格的 `.py` 路径可以正常执行；
- 修改外部文件后下一次执行使用最新内容；
- 脚本抛异常时记录非零退出码和错误输出；
- 超时和手动停止都能终止进程树；
- 同一任务重叠触发记录为「跳过」；
- 一次性任务在应用关闭期间过期后不补跑；
- 睡眠后迟到计时器只补跑一次；
- 应用退出时活动脚本被终止；
- `winapp ui inspect` 能找到导航入口和主要自动化标识；
- `winapp ui screenshot` 检查浅色、深色和窄窗口布局无错乱；
- 最终应用保持运行，供用户直接查看。

### 14.3 文档同步

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
