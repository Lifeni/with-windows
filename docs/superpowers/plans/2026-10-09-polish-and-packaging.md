# With Windows 工具箱 · P5 打磨与打包 Implementation Plan

> **⚠️ 历史记录**：本文是 2026-10-09 的实现计划，只描述当时怎么做；其中的第三方依赖白名单、平台版本与目录结构等已被后续改动取代（例如 `CommunityToolkit.Mvvm` 已移除、目标平台为 Win11 22H2），以 `AGENTS.md` 与代码为准。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把可用状态做到可分发状态：窗口状态记忆、浅色/窄宽度适配、Inno Setup 安装器、CI/Release 产出安装器、README 重写。

**Architecture:** 不改分层。窗口状态由 `AppHost`（配置）+ `MainWindow` 落地；`installer/WithWindows.iss` 以自包含单文件 exe + `Assets/` 为载荷；`release.yml` 产出安装器与便携 exe。

**Tech Stack:** C# 14 / .NET 10 / WinUI 3 / Inno Setup 6 / GitHub Actions

**Spec:** `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`（第 10、11 节）

## Global Constraints

- 仅 x64；构建/测试/发布都必须带 `-p:Platform=x64`
- `WithWindows.Core` 中禁止任何 `Microsoft.UI.Xaml` 类型
- 颜色/字体/间距一律 `ThemeResource`；深浅色都要能看
- 新增可交互控件必须有 `AutomationProperties.AutomationId`
- 中文文案用中文标点、中英文间加空格；提交信息不加 AI 署名
- **每完成一个 Task：构建 → 真实启动 → `winapp ui inspect` + `screenshot` → 记入 `docs/agents/ui-baseline.md` → 提交**
- **本批全部完成后跑一次 `codex exec review --base <本批起点>`**（见 `docs/agents/development-workflow.md` 第一节之二）

## 数据说明（已获授权）

用户已明确：**旧版本数据可直接删除重建**。因此不再为「保留旧草稿/配置」做任何兼容或还原动作，`%APPDATA%\WithWindows\` 下内容可在验证前直接清空。

---

### Task 1: 窗口状态记忆与最小尺寸

**Files:**
- Create: `src/WithWindows.App/Services/WindowStateStore.cs`
- Modify: `src/WithWindows.App/MainWindow.xaml.cs`

**Interfaces:**
- `public sealed class WindowStateStore(AppConfig config, Window window)`：`void Restore()`；`void Persist()`（写回 `config.WindowState` 并 `SaveConfig`）

**Steps**

- [ ] Step 1: `Restore()`：按 `config.WindowState` 设置窗口尺寸与位置（`AppWindow.MoveAndResize`），位置为 `null` 时用系统默认；恢复前做**可见性校验**（`DisplayArea.GetFromPoint` / `FindAll`，失败按可见处理），超出屏幕则移回主屏居中。
- [ ] Step 2: 最小尺寸用 `OverlappedPresenter.PreferredMinimumWidth/Height`（**不要**用 `AppWindow.PreferredMinimum*`，该属性不存在）。
- [ ] Step 3: `Persist()`：窗口尺寸/位置变化（`AppWindow.Changed`，去抖 500 ms）与退出前写回配置。
- [ ] Step 4: 构建 + 启动验证 + 记录（改尺寸 → 退出 → 重启 → 尺寸保持）。
- [ ] Step 5: 提交 `feat(窗口): 尺寸位置记忆与最小尺寸`。

---

### Task 2: 浅色主题与窄宽度适配

**Files:**
- Modify: `src/WithWindows.App/Views/*.xaml`（去掉任何硬编码尺寸/间距）
- Modify: `docs/agents/ui-baseline.md`

**Steps**

- [ ] Step 1: 用 `winapp` 或系统设置切到浅色主题，启动应用逐页截图，确认无「白底白字」、无硬编码色值残留（`rg -n "#[0-9A-Fa-f]{6}" src/WithWindows.App` 应无结果）。
- [ ] Step 2: 把窗口压到最小尺寸（约 600×420）逐页截图：确认导航自动折叠为图标、内容不被裁切、`CommandBar`/按钮不重叠。
- [ ] Step 3: 修正发现的问题（优先调整 `MinWidth`/`Grid` 列宽与 `NavigationView` 的 `CompactModeThresholdWidth`）。
- [ ] Step 4: 记录浅色与窄宽度截图；提交 `fix(界面): 适配浅色主题与窄窗口`。

---

### Task 3: Inno Setup 安装器脚本

**Files:**
- Create: `installer/WithWindows.iss`
- Create: `installer/README.md`（编译方法）

**Steps**

- [ ] Step 1: 安装 Inno Setup：`winget install --id JRSoftware.InnoSetup`（需确认）。
- [ ] Step 2: 写 `.iss`：`AppName=With Windows`、`AppVersion` 由 `/DAppVersion=` 传入、`DefaultDirName={autopf}\With Windows`、`PrivilegesRequired=lowest`（用户级，避免强制提权）、`ArchitecturesAllowed=x64compatible`。
- [ ] Step 3: `[Files]` 收 `dist\WithWindows.exe` 与 `Assets\*`；`[Icons]` 建开始菜单项与（可选）桌面快捷方式；`[Tasks]` 提供「开机自启」可选任务（写 `HKCU\...\Run`）；`[Run]` 可选安装后启动。
- [ ] Step 4: 先 `dotnet publish` 出自包含单文件，再 `ISCC.exe /DAppVersion=<ver> installer\WithWindows.iss`，确认产出 `WithWindows-<ver>-setup.exe`；**在干净目录里实装一次**（或至少校验安装包内容清单），确认可启动。
- [ ] Step 5: 记录安装/卸载实测结果；提交 `feat(打包): 新增 Inno Setup 安装器脚本`。

---

### Task 4: CI 与 Release 重写

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Steps**

- [ ] Step 1: `ci.yml` 恢复「发布可用性」校验：publish 自包含单文件 + 构建安装器（`choco install innosetup` 或 `winget`），确认产物存在。
- [ ] Step 2: `release.yml` 恢复产出：安装器 + 便携单文件 exe + `SHA256SUMS.txt`；版本仍由 `src/WithWindows.App/WithWindows.App.csproj` 的 `<Version>` 驱动；CHANGELOG 章节提取逻辑保留。
- [ ] Step 3: 用 `act` 不可行时，至少做 YAML 语法校验（`python -c "import yaml..."` 或 `yamllint`）与逐行对照设计文档第 10.5 节。
- [ ] Step 4: 提交 `ci: 重写为产出安装器`。

---

### Task 5: README 重写

**Files:**
- Modify: `README.md`
- Modify: `CHANGELOG.md`

**Steps**

- [ ] Step 1: 重写「功能」段：工具箱形态（记事本 / 快捷键 / 设置三个页面 + 托盘常驻），去掉「两个独立窗口」的旧描述。
- [ ] Step 2: 重写「快速开始」：构建、测试、运行、发布单文件、编译安装器的命令，全部指向 `src/WithWindows.App`。
- [ ] Step 3: 重写「下载」：安装器与便携 exe 说明、SmartScreen「未知发布者」提示的解释与跳过方法、系统要求（Windows 11 21H2+，x64）。
- [ ] Step 4: 更新目录结构图（`src/WithWindows.App`、`src/WithWindows.Core`、`tests/WithWindows.Core.Tests`、`installer/`）。
- [ ] Step 5: 去掉顶部「重构进行中」的临时警告；`CHANGELOG` 把「未发布」整理为可发布章节。
- [ ] Step 6: 提交 `docs: 重写 README 与更新日志`。

---

## 自检结果

**Spec 覆盖检查：**

| Spec 条目 | 对应任务 |
|---|---|
| 5.2 窗口尺寸/位置记忆、最小尺寸 | Task 1 |
| 5.5 深浅色、响应式 | Task 2 |
| 10.3 Inno Setup 安装器职责 | Task 3 |
| 10.5 CI 调整 | Task 4 |
| 10.6 / DoD：README 与 CHANGELOG 同步 | Task 5 |
| 12 风险：SmartScreen 需说明 | Task 5 |

**已知不确定项（执行时实测确认）：**

1. Inno Setup 能否通过 `winget` 安装成功（需要网络与确认）
2. 单文件 publish 产物里 `Assets/` 是否仍需随包分发（图标由运行期从磁盘读取）
3. `installer` 在 CI 上的安装方式（choco / winget）
