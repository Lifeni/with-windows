# 交接说明 · 新会话从这里开始

> 这份文件的目的：让一个**没有任何对话记忆**的新会话在五分钟内接上进度。

## 一分钟上手

```powershell
cd C:\Users\LFN\Documents\Code\with-windows
git log --oneline -12
git status --short
```

- `main` 是稳定集成分支，主检出为 `C:\Users\LFN\Documents\Code\with-windows`。`0.6.0` 已通过发布提交 `12f1437` 合入 `main`；外部工作区 `C:\Users\LFN\.codex\worktrees\ai-chat-openai-compatible\with-windows` 仍指向同一提交，由 Codex 宿主管理，不再有待合并改动。
- 2026-10-11 已把 `dev` 全部合并进 `main`（合并提交 `538f72d`），并清掉 `.worktrees/dev`、`dev` 分支与 `feat/winui3`（本地与远端）。要隔离开发直接用 `git worktree add` 新建即可。
- 新会话应先读：`AGENTS.md`（入口）+ 本文 + `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`（设计与全部决策）。

## 一、这个项目是什么

Windows 常驻托盘的工具箱：左侧 `NavigationView` 导航，每个工具一页。当前五个页面——**记事**（对齐系统记事本的编辑器 + 浮动查找替换 + 状态栏）、**待办**（增删改、完成状态与一次性到期提醒）、**AI 对话**（OpenAI 兼容流式对话 + Markdown + 剪贴板图片）、**投影**（投屏快捷键与候选模式）、**设置**（开机自启、显示 / 隐藏窗口快捷键、关于）。系统关闭按钮与 `Alt+F4` 隐藏到托盘，单进程常驻；真正退出只走托盘菜单。

## 二、当前进度（截至 2026-10-24）

- **待办页开发中**：待办模型、JSON 持久化、截止时间单次调度、完成 / 删除交互和提醒提示条已实现；提醒到点会唤出主窗口并切到待办页。应用内提醒作为 Windows App SDK 自包含通知兼容问题的可靠替代。
- **长时间停用恢复**：窗口停用超过 10 分钟后再次激活会重建当前页面，普通激活也会检查页面是否仍挂载；用于规避锁屏、休眠或长期闲置后 XAML 内容树失效导致的黑屏与 `0xC000027B`。
- **标题栏已改用系统窗口按钮**：内置 `TitleBar` 只负责图标、标题与折叠导航，最小化、最大化 / 还原、关闭交给系统 caption 区域，关闭按钮恢复红色悬停；关闭后唤回会清除 caption 悬停残留。
- **GitHub Actions 运行环境已升级**：`actions/checkout` 与 `softprops/action-gh-release` 已升级到使用 Node.js 24 的版本，消除 Node.js 20 弃用注解。
- **v0.6.0 已发布**：tag `v0.6.0`，Release 见 [v0.6.0](https://github.com/Lifeni/with-windows/releases/tag/v0.6.0)，发布提交 `12f1437`。GitHub Actions Run [39614281329](https://github.com/Lifeni/with-windows/actions/runs/39614281329) 成功，测试、单文件发布、安装器与 Release 上传全部通过；安装器 44,728,517 字节，便携 zip 66,610,289 字节，`SHA256SUMS.txt` 已上传。
- **v0.5.1 已发布**：tag `v0.5.1`，Release 见 [v0.5.1](https://github.com/Lifeni/with-windows/releases/tag/v0.5.1)，含 Inno Setup 安装器（约 44.5 MB）、便携 zip（约 66.4 MB）与 `SHA256SUMS.txt`。
- 旧实现（两个独立窗口 + 单文件 exe）已退役，源码只留在 git 历史里（`v0.4.0` 及更早）。
- `0.6.0` 的核心新增是「AI 对话」：OpenAI 兼容 Chat Completions、SSE、Markdown、单会话保存、DPAPI 密钥保护、剪贴板图片（最终 1 MiB 上限、自动压缩）与新建会话。标题区显示会话摘要和模型名称；发送请求时历史图片限制为最新最多 4 张、总计 4 MiB。输入框在切入页面后自动聚焦，会话搜索已移除，托盘菜单新增「AI 对话」入口。设置页可选择显示 / 隐藏快捷键的默认页面（记事 / AI 对话），配置升到 v5。`0.6.0` 当时使用自有标题栏，2026-10-22 已改为内置 `TitleBar` + 系统 caption 按钮；托盘收起仍使用 CsWin32 `ShowWindow(SW_MINIMIZE)` + 从切换器移除，唤回使用 `SW_SHOWNOACTIVATE`，不要再改回 `OverlappedPresenter.Minimize()` / `Restore()`、`SW_HIDE` 或屏幕外移动，否则会复现 AI 页黑屏与 `Microsoft.UI.Xaml.dll` 原生崩溃。
- `0.6.0` 的正式变更记在 `CHANGELOG.md` 的 `## [v0.6.0] - 2026-10-21` 章节。

## 三、已定决策（不要重新讨论）

| 项 | 结论 |
|---|---|
| 技术栈 | WinUI 3 + Windows App SDK 2.5.x，不换框架 |
| 目标框架 | `net10.0-windows10.0.22621.0`（Win11 22H2+，不背 Windows 10） |
| 打包模型 | **未打包**（`WindowsPackageType=None`） |
| 分发方式 | **Inno Setup EXE 安装器** + GitHub Releases；不签名起步；不上商店 |
| 运行时 | 自包含 + 单文件 exe（安装器与便携包用的都是它） |
| 默认热键 | **不预置任何默认值**，全部由用户录入 |
| Git Hook | **不装**，提交规范只写在 `AGENTS.md` |
| 数据位置 | `%APPDATA%\WithWindows\` |
| P/Invoke | 一律 CsWin32，禁止手写 `DllImport` |
| MVVM | **暂不引入**，页面保持 code-behind；判断准则见 `AGENTS.md` 的「MVVM（当前不引入）」 |

决策的完整推导（含被否掉的备选方案与理由）在 `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`。

## 四、环境状态

已就绪：

- .NET SDK `10.0.401`（`global.json` 已钉版本）
- `winapp` CLI `0.7.1`（`C:\Users\LFN\AppData\Local\Microsoft\WindowsApps\winapp.exe`），用于界面自检
- Inno Setup 6：**用户级安装**在 `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`（不是 `C:\Program Files (x86)`；CI 里由 `choco install innosetup` 装到机器级路径）
- `gh` 已登录（`repo`、`workflow` 权限），可查 Actions 运行与 Release
- `~/.codex/config.toml` 里 `multi_agent_v2 = true`

## 五、验证命令（改动后必须全跑）

```powershell
# 构建解决方案（3 个工程：Core / App / Core.Tests）
dotnet build WithWindows.sln -p:Platform=x64

# 核心逻辑测试（唯一测试工程）
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64

# 启动（非阻塞！dotnet run 会挂住终端）
$exe = "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe"
$p = Start-Process -FilePath (Resolve-Path $exe).Path -PassThru
Start-Sleep -Seconds 8

# 界面自检
winapp ui list-windows -a WithWindows
winapp ui inspect -a WithWindows -i
winapp ui get-focused -a WithWindows           # 当前键盘焦点落在哪个元素
winapp ui get-value StatusCaret -a WithWindows # 读状态栏文本
winapp ui screenshot -a WithWindows --json
```

验证完成后保持应用运行，让用户直接测试，不要自行结束进程。

当前基线：Core 测试 **187/187**，解决方案构建 **0 错误 0 警告**。界面基线与截图流程见 `docs/agents/ui-baseline.md`。

## 六、操作注意事项（都是实际踩过的）

1. **沙箱内 `.git` 是只读的**：所有 git 写操作（`add` / `commit` / `merge` / `worktree` / `push`）都要提权执行。
2. **沙箱内 `dotnet restore` 会因断网失败，构建解决方案也会失败**（App 的 XAML 编译器起不来；`--no-restore` 下同样失败且不带具体 error 行）。单独构建 `WithWindows.Core` 在沙箱内可以正常构建。**这是沙箱限制，不是仓库缺陷**。
3. **不要用 `git checkout -- .` / `git reset`**：会回退未提交的修复。要撤销改动用精确命令，或先提交。
4. **`dotnet run` 会阻塞**：验证界面一律用 `Start-Process` 非阻塞启动；验证完成后保持应用运行，方便用户直接测试。
5. **常驻实例会锁住 exe**：重新构建前先退出应用（托盘「退出」或结束 `WithWindows.exe`），否则报 `MSB3021` / `MSB3027`——那是文件占用，不是代码错误。
6. **验证不得污染用户数据**：`%APPDATA%\WithWindows\` 下的草稿与配置是用户的真实数据，注入文本、改字号等验证后必须还原，更稳妥的是不碰真实文件。
7. **写文件用 `apply_patch`**，不要用 `cat` / heredoc。AI 写出的文件默认是 LF，提交前跑 `powershell -ExecutionPolicy Bypass -File scripts/normalize-eol.ps1` 对齐 CRLF。
8. **每批改动后跑一次独立审查**（`codex exec review`），做法与当前 CLI 的限制见 `docs/agents/development-workflow.md` 第一节之二。

## 七、下一步

1. **安装器向导中文化**（可选）：Inno Setup 6 官方包不含中文语言文件，把 `ChineseSimplified.isl` 放进 `installer/` 后重新编译即自动切换（脚本已用 `#if FileExists` 处理）。
2. **干净机器验证**：在未装 .NET 与 Windows App Runtime 的机器上实装，确认零依赖启动。
3. **后续工具**：当前已有记事 / 待办 / AI 对话 / 投影四页（另有设置页）；新增工具仍是 `ToolCatalog` 加一条 + 一个页面。只有在出现共享状态或更多可绑定动作时，再把 `ToolCatalog` 抽成 `ITool` 泛化。
4. **代码签名**（可选）：消除 SmartScreen「未知发布者」提示，设计文档第 10.4 节给了 SignPath 等路径。
5. **AI 对话下一阶段**：普通文件附件、Responses API、自动上下文压缩和多会话管理。

## 八、设计与记录在哪

- 界面基线与自检流程：`docs/agents/ui-baseline.md`
- 设计与决策：`docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`
- 各阶段实现计划：`docs/superpowers/plans/`
