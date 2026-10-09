# 交接说明 · 新会话从这里开始

> 这份文件的目的：让一个**没有任何对话记忆**的新会话在五分钟内接上进度。

## 一分钟上手

```powershell
cd C:\Users\LFN\Documents\Code\with-windows\.worktrees\dev
git log --oneline -12
```

- **工作分支**：`dev`，位于 **worktree** `.worktrees/dev`，**不在主检出里**。
- **主检出** `C:\Users\LFN\Documents\Code\with-windows` 停在 `main`，是**旧实现**（两个独立窗口 + 单文件 exe），**只作参考，不要在上面改**。
- **所有命令都必须在 worktree 目录下执行**。主检出下的 `AGENTS.md` 是过期的旧版，工作区里的 `AGENTS.md` 才是现行规范。
- 新会话应先读：`AGENTS.md`（入口）+ `docs/HANDOFF.md`（本文）+ `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`（设计与全部决策）。

## 一、这个项目是什么

Windows 常驻托盘的工具箱应用：`NavigationView` 导航，每个工具一页。首版两个工具页（记事本、快捷键）+ 设置页。原先只有"切换投屏"和"快捷记事"两个独立窗口，正在重构成工具箱。

**旧实现已退役**：`main` 分支保留原样供查阅；`dev` 分支已在 P2 删除 `src/WithWindows.UI` 与 `tests/WithWindows.UI.Tests`。

## 二、当前进度（截至 2026-10-09）

分支 `dev`，HEAD 见 `git log`。已完成五个批次：

**批次一 · P0 规范与环境**（提交 `b226f26`…`18a7655`）

- 仓库级规范：`.editorconfig`、`.gitattributes`（CRLF）、`global.json`（钉 SDK 10.0.401）、`nuget.config`
- `Directory.Build.props`（`LangVersion`/`Nullable`/`ImplicitUsings`/`PublishTrimmed=false`）、`Directory.Packages.props`（中央包版本）
- `AGENTS.md` 重构为**入口 83 行 + `docs/agents/` 五册**（WinUI 3 硬规则、开发工作流、界面约定、互操作与平台、已知坑清单）

**批次二 · P1 工程骨架**（提交 `d42cfdf`…`cc45335`，修补 `c1f4fad`）

- `src/WithWindows.Core`：纯逻辑类库，**禁止引用任何 `Microsoft.UI.Xaml` 类型**（已用 `rg` 验证为干净）
- `src/WithWindows.App`：**未打包** WinUI 3（`WindowsPackageType=None`），无 `Package.appxmanifest`
- `tests/WithWindows.Core.Tests`：只引用 Core（旧测试工程被 WinUI 拖着跑，这是刻意修的）
- 导航壳：`TitleBar` + `NavigationView` + `Frame`，三个占位页面（记事本 / 快捷键 / 设置）
- `docs/agents/ui-baseline.md`：`winapp ui` 自检流程与基线截图

**批次三 · P2 第一片 + 标题栏修复**（提交 `73f12ce`、`308ef55`、`9babf5e`）

- `src/WithWindows.Core/Hotkeys/`：`Hotkey`、`HotkeyModifiers`、`HotkeyParser`、`HotkeyFormatter`
- 关键改进：**Core 不再依赖 Win32 常量**——新定义了 `HotkeyModifiers` 枚举，映射留到注册边界。旧代码的 `HotkeyParser` 直接引用 `NativeMethods.MOD_*`，测试也被迫依赖互操作层
- 行为契约完整保留：F1–F24 编解码、字母数字必须带修饰键、修饰键大小写不敏感、中文错误文案、按住不重复触发（改为注册层固定策略）
- 标题栏修复：补齐 `ExtendsContentIntoTitleBar = true` / `SetTitleBar` / `PreferredHeightOption` / `SetIcon`，解决"两条标题栏"与图标渲染成棋盘格

**批次四 · P2 Core 迁移与旧项目退役**（提交 `e3d3239`… 见 `git log`）

- 计划：`docs/superpowers/plans/2026-10-09-core-migration.md`
- **CsWin32 接入**：`src/WithWindows.Core/Interop/NativeMethods.txt` 声明，生成 `Windows.Win32.PInvoke`；核心层不再手写 `DllImport`
- **`HotkeyManager`**：独立隐藏消息窗口（`MessageWindow`）+ 自持 `WNDPROC` 委托防 GC；注册失败回传错误码（1409 = 已占用）
- **配置 v1**：`tools[]`（`id`/`enabled`/`hotkey`，默认 `null`）+ `displayMode` + `windowState`；原子写 + `.bak` 回退 + 自愈清理未知工具；旧 v3/数组格式直接重建
- **显示拓扑**：`DisplayTopology`（`QDC_DATABASE_CURRENT` 权威拓扑）+ `DisplayModeAction`（`Changed=false` 语义、toggle 纯函数）
- **平台服务**：`Logger`（1 MB 轮转）、`SingleInstance`（命名 Mutex）、`AutoStart`（`HKCU\...\Run`）迁到 Core
- **旧项目退役**：删除 `src/WithWindows.UI` 与 `tests/WithWindows.UI.Tests`，从解决方案移除；CI/release 同步调整（产物发布待 P5）
- Core 测试从 53 增至 **99**，全部通过；解决方案只剩 3 个工程

**批次五 · P4 工具目录 + P3 页面与托盘**（提交 `4c6ca2f`… 见 `git log`）

- 计划：`docs/superpowers/plans/2026-10-09-shell-pages-and-tray.md`
- **工具目录**：`ToolCatalog`（`notepad` / `hotkeys` / `display_mode`）驱动左栏导航与快捷键行；`HotkeyRouter` 统一分派托盘、热键与「立即执行」
- **生命周期**：`AppHost` 由 `App` 字段持有，负责单实例、日志、配置、热键注册与热重载；支持 `--smoke`（加载配置 + 注册热键后退出，不抢单实例）
- **托盘**：WinUIEx `TrayIcon`（字段持引用防 GC）；关闭窗口 = 隐藏到托盘（`Window.Closed` 不可取消，必须用 `AppWindow.Closing`）
- **快捷键页**：`HotkeyInputBox` 录入 + 启用开关 + 立即执行，注册失败就地提示
- **设置页**：开机自启、投屏候选模式（至少保留一项）、关于
- **记事本页**：`RichEditBox` + 防抖草稿（`%APPDATA%\WithWindows\notepad.txt`）+ 秒级时钟 + 缩放 + 置顶
- Core 测试增至 **100**，全部通过

## 三、已定决策（不要重新讨论）

| 项 | 结论 |
|---|---|
| 技术栈 | WinUI 3 + Windows App SDK 2.5.x，不换框架 |
| 目标框架 | `net10.0-windows10.0.22621.0`（只要 Win11 21H2+，不背 Win7） |
| 打包模型 | **未打包**（`WindowsPackageType=None`） |
| 分发方式 | **Inno Setup EXE 安装器** + GitHub Releases；不签名起步；不上商店 |
| 运行时 | 自包含 + 单文件 exe |
| 默认热键 | **不预置任何默认值**，全部由用户录入 |
| Git Hook | **不装**，提交规范只写在 `AGENTS.md` |
| AGENTS.md | 入口 + 五册分册结构 |
| 数据位置 | `%APPDATA%\WithWindows\` |
| P/Invoke | 一律 CsWin32，禁止手写 `DllImport` |

决策的完整推导（含被否掉的备选方案与理由）在 `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`。

## 四、环境状态

已就绪：

- .NET SDK `10.0.401`；`global.json` 已钉版本
- **`winapp` CLI 0.7.1** 已装（`C:\Users\LFN\AppData\Local\Microsoft\WindowsApps\winapp.exe`），用于界面自检
- `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.7-alpha` 已装（仅作结构参考，工程文件是手工维护的）
- **`multi_agent_v2` 已在 `~/.codex/config.toml` 启用**

尚未安装：

- **Inno Setup**（P5 需要）：`winget install --id JRSoftware.InnoSetup`

## 五、验证命令（改动后必须全跑）

```powershell
# 构建解决方案（3 个工程：Core / App / Core.Tests）
dotnet build WithWindows.sln -p:Platform=x64

# 核心逻辑测试（唯一测试工程）
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64

# 启动（非阻塞！dotnet run 会挂住终端）
$exe = "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe"
$p = Start-Process -FilePath (Resolve-Path $exe).Path -PassThru
Start-Sleep -Seconds 7

# 界面自检
winapp ui list-windows -a WithWindows
winapp ui inspect -a WithWindows -i
winapp ui search NotepadPlaceholder -a WithWindows
winapp ui screenshot -a WithWindows --json

Stop-Process -Id $p.Id -Force
```

当前基线：Core 测试 **100/100**，解决方案构建 **0 错误**。界面基线与截图见 `docs/agents/ui-baseline.md`。

## 六、下一步：P5 打磨与打包（P2/P3/P4 已完成）

三个工具页与托盘已可用：记事本（编辑/草稿/时钟/缩放/置顶）、快捷键（录制/启用/立即执行）、设置（自启/投屏候选/关于）。

按依赖顺序：

1. **深浅色与自适应**：浅色主题截图验证；宽/中/窄三档布局
2. **窗口状态记忆**：尺寸/位置/字号持久化到 `windowState`（含可见性校验，(0, 0) 合法、用 null 表示未保存）
3. **Inno Setup 安装器**：`installer/WithWindows.iss`；`release.yml` 恢复产出（安装器 + 便携 exe + SHA256SUMS）
4. **README 重写**：功能、命令、目录结构、安装说明全部对齐新实现
5. **端到端复核**：干净机器安装、常驻、热键与托盘

**注意**：P5 的实现计划文档还没写。按流程应先补计划再实施（P0/P1、P2、P3/P4 的计划见 `docs/superpowers/plans/`）。

**验证前置**：本机 `%APPDATA%` 与命名互斥体 `Local\WithWindows.SingleInstance` 是全局资源，验证前先确认没有陈旧 `WithWindows.exe` 常驻（否则新实例会静默退出、日志也写不进去），详见 `docs/agents/known-pitfalls.md`。

## 七、操作注意事项（都是实际踩过的）

1. **`.git` 在沙箱里是只读的**：所有 git 写操作（commit/add）都要用 `require_escalated`。
2. **沙箱内 `dotnet restore` 会因断网失败**：需要 `require_escalated`。
3. **沙箱内构建解决方案会失败**（App 的 XAML 编译器无法启动；`--no-restore` 下同样失败且不带具体 error 行），沙箱外（提权）正常。这是沙箱限制，**不是仓库缺陷**——别据此改仓库。单独 `dotnet build src/WithWindows.Core/WithWindows.Core.csproj` 在沙箱内可以正常构建。
4. **不要用 `git checkout -- .` / `git reset`**：会回退未提交的修复。要撤销改动用精确的 `dotnet sln remove` 这类命令，或先提交。
5. **`dotnet run` 会阻塞**：验证界面一律用 `Start-Process` 非阻塞启动，验证完 `Stop-Process`。
6. **验证命令不要一律加 `--no-restore`**：它会掩盖还原层的问题。P0/P1 就是这么漏掉了一个真实的 NU1008 缺陷（`Core.Tests` 违反集中包版本管理），直到终审才被发现。
7. **原生子代理（`spawn_agent`）不可靠**：根因是 `agent_message_board` 特性关闭导致任务消息投递失败。`multi_agent_v2` 已启用（新会话生效）。若仍不可用，**用 `codex exec` 起独立进程替代**，它带 `exec review` 子命令可做独立代码审查。
8. **写文件用 `apply_patch`**，不要用 `cat` / heredoc。

## 八、验证记录在哪

- 执行日志与全部裁决：`.superpowers/sdd/2026-10-09-toolbox-skeleton/progress.md`（该目录被 git 忽略，只在本地磁盘上）
- 界面基线与截图流程：`docs/agents/ui-baseline.md`
- 设计与决策：`docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`
- P0/P1 实现计划：`docs/superpowers/plans/2026-10-09-toolbox-skeleton.md`
