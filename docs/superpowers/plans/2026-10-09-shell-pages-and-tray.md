# With Windows 工具箱 · P4 工具目录 + P3 页面与托盘 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 P1 的占位导航壳接上真实能力：工具目录驱动导航与快捷键列表、生命周期服务组装、托盘宿主、快捷键页、设置页、记事本页。

**Architecture:** `WithWindows.Core` 提供纯逻辑（配置、热键、动作）；`WithWindows.App` 提供 UI 与宿主（Shell / Views / Controls / Services / Tools）。工具用 App 侧 `ToolCatalog` 登记（`Id` / 标题 / 字形 / 页面类型 / 是否有热键），导航项与快捷键行都由目录生成，新增工具不改 `MainWindow`。

**Tech Stack:** C# 14 / .NET 10 / WinUI 3 / WinUIEx（TrayIcon）/ CommunityToolkit.Mvvm / xunit

**Spec:** `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`

## Global Constraints

- 仅 x64：构建与测试都必须带 `-p:Platform=x64`
- `WithWindows.Core` 中禁止出现任何 `Microsoft.UI.Xaml` 类型
- 每个可交互控件必须有 `AutomationProperties.AutomationId`；纯图标按钮补 `AutomationProperties.Name`
- 颜色、字体、间距一律走 `ThemeResource`，禁止硬编码色值
- 注释与用户可见文案使用中文；中文用中文标点；中英文之间、中文与数字之间加空格
- 提交信息用 Conventional Commits，中文描述，不加任何 AI 署名
- **每完成一个 Task：先构建，再真实启动应用，跑 `winapp ui inspect` 与 `winapp ui screenshot`，把观察追加到 `docs/agents/ui-baseline.md`，然后才提交**

## 固定验证流程（每个 Task 都执行）

1. 停掉本工作树的旧实例：`Get-Process -Name WithWindows -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.worktrees\dev\*' } | Stop-Process -Force`（不碰主检出的实例）
2. 构建：`dotnet build WithWindows.sln -p:Platform=x64`
3. 非阻塞启动：`$exe = "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe"`，然后 `Start-Process -FilePath (Resolve-Path $exe).Path -PassThru`，`Start-Sleep -Seconds 7`
4. 自检：`winapp ui list-windows -a WithWindows`、`winapp ui inspect -a WithWindows -i`、`winapp ui screenshot -a WithWindows --json`
5. 把观察追加到 `docs/agents/ui-baseline.md` 的「修订记录」，然后 `Stop-Process -Id $p.Id -Force`

## 范围说明（相对设计文档的一处简化）

设计文档 6.2 节的 `ITool` 含 `Type PageType`。因为「打开记事本页」是 UI 行为、而 `Core` 禁止引用 UI，完整 `ITool` 会横跨两层。首版**不引入跨层接口**，改用 App 侧静态目录 `ToolCatalog`（登记 `Id` / `Title` / `Glyph` / `PageType` / `HasHotkey`），导航项与快捷键行都由它生成；新增「有页面的工具」只需加一条目录项 + 一个页面。等出现第 3、4 个工具时再抽 `ITool` 泛化。

---

### Task 1: 动作接口与工具目录（P4）

**Files:**
- Create: `src/WithWindows.Core/Actions/IAction.cs`
- Modify: `src/WithWindows.Core/Actions/DisplayModeAction.cs`（实现 `IAction`）
- Create: `src/WithWindows.App/Tools/ToolCatalog.cs`
- Create: `src/WithWindows.App/Services/HotkeyRouter.cs`
- Modify: `src/WithWindows.App/MainWindow.xaml`（删掉手写导航项）
- Modify: `src/WithWindows.App/MainWindow.xaml.cs`（导航项由目录生成）
- Test: `tests/WithWindows.Core.Tests/DisplayModeActionTests.cs`（补 `Id` 断言）

**Interfaces:**
- `public interface IAction { string Id { get; } ActionResult Execute(string argument); }`（命名空间 `WithWindows.Core.Actions`）
- `public sealed record ToolEntry(string Id, string Title, string Glyph, Type? PageType, bool HasHotkey);`
- `public static class ToolCatalog { public static IReadOnlyList<ToolEntry> Tools { get; } }`
- `public sealed class HotkeyRouter { public HotkeyRouter(MainWindow window, AppConfig config); public ActionResult? Invoke(string toolId); }`

**Steps**

- [ ] Step 1: 写 `IAction`；`DisplayModeAction` 实现它，`Id => "display_mode"`；测试断言 `((IAction)action).Id == "display_mode"`。
- [ ] Step 2: `ToolCatalog` 两条：`notepad`（标题「记事本」，字形 `E70B`，`PageType = typeof(NotepadPage)`，`HasHotkey = true`）、`display_mode`（标题「切换投屏」，字形 `E7F8`，`PageType = null`，`HasHotkey = true`）。
- [ ] Step 3: `HotkeyRouter.Invoke(toolId)`：`notepad` → 显示主窗口并导航到记事本页，返回 `null`；`display_mode` → `new DisplayModeAction(config.DisplayMode.Modes.ToArray()).Execute("toggle")`，返回该 `ActionResult`。
- [ ] Step 4: `MainWindow` 遍历 `ToolCatalog.Tools` 中 `PageType != null` 的项生成 `NavigationViewItem`（`AutomationProperties.AutomationId = "NavItem" + Id`）。
- [ ] Step 5: 构建 + 固定验证流程 + 记录；确认导航项仍是记事本 / 快捷键，切换正常。
- [ ] Step 6: 提交 `feat(工具): 目录驱动导航与热键分派`。

---

### Task 2: 应用生命周期服务组装

**Files:**
- Create: `src/WithWindows.App/Services/AppHost.cs`
- Modify: `src/WithWindows.App/App.xaml.cs`
- Modify: `src/WithWindows.App/MainWindow.xaml.cs`

**Interfaces:**
- `public sealed class AppHost : IDisposable`
  - `public AppConfig Config { get; }`
  - `public HotkeyManager Hotkeys { get; }`
  - `public Logger Log { get; }`
  - `public static AppHost? Create()`（单实例占用失败返回 `null`）
  - `public IReadOnlyList<string> ReloadBindings()`
  - `public void SaveConfig()`

**Steps**

- [ ] Step 1: `App` 增加实例字段 `_host`；`OnLaunched` 里 `AppHost.Create()`，为 `null` 时弹 `ContentDialog` 后退出（`MainWindow` 需先建好才能当 `XamlRoot`）。
- [ ] Step 2: `--smoke` 参数：加载配置 + 注册热键，把结果写到控制台后退出，不抢单实例、不建托盘。
- [ ] Step 3: `ReloadBindings`：先 `UnregisterAll()`，再遍历 `Config.Tools`（`Enabled` 且 `Hotkey` 非空）解析并 `Register`，收集失败原因。
- [ ] Step 4: 构建 + 固定验证流程 + 记录。
- [ ] Step 5: 提交 `feat(宿主): 组装单实例、配置、日志与热键`。

---

### Task 3: 托盘宿主与关闭到托盘

**Files:**
- Create: `src/WithWindows.App/Services/TrayHost.cs`
- Modify: `src/WithWindows.App/MainWindow.xaml.cs`

**Interfaces:**
- `public sealed class TrayHost : IDisposable { public void Show(); public void UpdateTooltip(string text); }`

**Steps**

- [ ] Step 1: 用 WinUIEx `TrayIcon`（**引用必须由字段持有防 GC**）：左键单击显示窗口，右键菜单「记事本 / 快捷键 / 设置 / 退出」。
- [ ] Step 2: `MainWindow.Closed` 拦截为 `args.Cancel = true` + `AppWindow.Hide()`，不销毁窗口。
- [ ] Step 3: 构建 + 固定验证流程 + 记录；确认托盘图标出现、菜单项可点。
- [ ] Step 4: 提交 `feat(托盘): 常驻托盘与关闭到托盘`。

---

### Task 4: 录入式热键控件与快捷键页

**Files:**
- Create: `src/WithWindows.App/Controls/HotkeyInputBox.xaml(.cs)`
- Rewrite: `src/WithWindows.App/Views/HotkeysPage.xaml(.cs)`

**Interfaces:**
- `HotkeyInputBox`：`public string? HotkeyText { get; set; }`；`event TypedEventHandler<HotkeyInputBox, string?> HotkeyChanged`；聚焦后捕获组合键，Esc 取消，带清除按钮。

**Steps**

- [ ] Step 1: `HotkeyInputBox` 用 `PreviewKeyDown` 捕获修饰键 + F1–F24 / 字母 / 数字，调 `HotkeyParser` 校验，非法显示红色提示，合法则用 `HotkeyFormatter` 回填。
- [ ] Step 2: 快捷键页遍历 `ToolCatalog.Tools`（`HasHotkey`）生成行：工具名 + `HotkeyInputBox` + `ToggleSwitch`（启用）+「立即执行」；空热键显示「未设置」。
- [ ] Step 3: 保存即 `ReloadBindings`；注册失败在该行用 `InfoBar` 显示原因；「立即执行」走 `HotkeyRouter`。
- [ ] Step 4: 构建 + 固定验证流程 + 记录（`invoke NavItemHotkeys` 后能看到 `HotkeyRow_display_mode` 等 AutomationId）+ 截图。
- [ ] Step 5: 提交 `feat(快捷键页): 列表化热键录制与立即执行`。

---

### Task 5: 设置页

**Files:**
- Rewrite: `src/WithWindows.App/Views/SettingsPage.xaml(.cs)`

**Steps**

- [ ] Step 1: 开机自启开关绑定 `AutoStart.IsEnabled / Enable / Disable`（`AutomationId = "AutoStartToggle"`）。
- [ ] Step 2: 投屏候选模式复选（internal / extend / external / clone）写回 `Config.DisplayMode.Modes` 并 `ReloadBindings`。
- [ ] Step 3: 关于区展示程序集版本、`AppPaths.ConfigPath`、`AppPaths.LogPath`（只读 `TextBox` 便于复制）。
- [ ] Step 4: 构建 + 固定验证流程 + 记录；截图（浅色与深色各一张）。
- [ ] Step 5: 提交 `feat(设置页): 自启、投屏候选与关于`。

---

### Task 6: 记事本页

**Files:**
- Create: `src/WithWindows.App/Services/NotepadDraft.cs`
- Rewrite: `src/WithWindows.App/Views/NotepadPage.xaml(.cs)`

**Interfaces:**
- `NotepadDraft`：`public string Load()`；`public void QueueSave(string text)`（防抖 500 ms 写 `AppPaths.NotepadDraftPath`）；`IDisposable`。

**Steps**

- [ ] Step 1: `RichEditBox` 编辑区，打开时读回草稿，编辑后防抖写盘；行距 1.05。
- [ ] Step 2: 页内实时时钟（`DispatcherQueueTimer`，秒级），页面不可见时停表。
- [ ] Step 3: `Ctrl+滚轮` 与 `±` 按钮缩放字体（10–32 px），字号写回 `Config.WindowState.NotepadFontSize`。
- [ ] Step 4: 置顶开关（`AutomationId = "NotepadPinToggle"`），作用于主窗口。
- [ ] Step 5: 构建 + 固定验证流程 + 记录（确认草稿写盘与读回）+ 截图。
- [ ] Step 6: 提交 `feat(记事本页): 编辑、时钟、缩放与草稿`。

---

## 自检结果

**Spec 覆盖检查：**

| Spec 条目 | 对应任务 |
|---|---|
| 5.1 导航壳（目录驱动） | Task 1 |
| 5.2 记事本页 | Task 6 |
| 5.3 快捷键页 | Task 4 |
| 5.4 设置页 | Task 5 |
| 6.3 数据位置 | Task 2、6 |
| 6.4 热键注册 / 热重载 | Task 2、4 |
| 6.7 生命周期（单实例 / 自启 / 日志） | Task 2、3 |
| 7.2 六步闭环 | 每个 Task 的固定验证流程 |
| 8. 托盘（WinUIEx TrayIcon，字段持引用） | Task 3 |

**有意推迟：** Inno Setup 安装器、CI / release 重写、README 完整重写（P5）；`ITool` 泛化（出现第 3 个工具时）。

**已知不确定项（执行时实测确认）：**

1. WinUIEx `TrayIcon` 在当前版本的确切 API（图标、右键菜单、左键单击事件）
2. `RichEditBox` 段落行距 API 在当前 WASDK 的可用性
3. `winapp ui` 的 `invoke` / `click` 对 `ToggleSwitch`、`CheckBox` 的支持
