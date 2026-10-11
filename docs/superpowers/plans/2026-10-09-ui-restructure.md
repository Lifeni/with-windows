# With Windows · P6 界面重构 Implementation Plan

> **⚠️ 历史记录**：本文是 2026-10-09 的实现计划，只描述当时怎么做；其中的第三方依赖白名单、平台版本与目录结构等已被后续改动取代（例如 `CommunityToolkit.Mvvm` 已移除、目标平台为 Win11 22H2），以 `AGENTS.md` 与代码为准。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按用户反馈重构界面：记事页只做编辑（对齐系统记事本）、快捷键页改为投屏页、设置页保留自启并新增呼出快捷键与路径按钮、导航栏更窄，整体排版对齐系统应用与主流开源 WinUI 工具箱。

**Architecture:** 不改分层。新增/调整集中在 `WithWindows.App`（Views / Controls / Services / Tools）与 `WithWindows.Core`（配置项、动作）；配置 schema 增加一个「呼出主窗口」工具项。

**Spec:** `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`（本计划的界面细节以其第 5 节为基础，按用户 2026-10-09 反馈调整）

## Global Constraints

- 仅 x64；构建/测试带 `-p:Platform=x64`
- `Core` 禁止 `Microsoft.UI.Xaml`；颜色一律 `ThemeResource`
- 可交互控件必须有 `AutomationProperties.AutomationId`
- 中文文案用中文标点、中英文间加空格；提交信息不加 AI 署名
- **每完成一个 Task：构建 → 真实启动 → `winapp ui inspect` + `screenshot` → 记 `docs/agents/ui-baseline.md` → 提交 → 跑 `codex exec review`**（不再询问用户是否执行审查）

## 调研结论

### 一、系统记事本（Windows 11）有什么

| 区域 | 内容 |
|---|---|
| 顶部 | 格式工具栏（Markdown 风格：粗体 / 斜体 / 链接 / 列表 / 标题，较新版本）、标签页 |
| 底部状态栏 | 行列号、字符数、缩放比例、编码、换行符；格式视图与 Markdown 源码视图切换 |
| 视图菜单 | 缩放（`Ctrl+加号` / `Ctrl+减号` / `Ctrl+0`）、自动换行开关、状态栏显示开关 |
| 其他 | 查找替换、字体设置（字体与字号） |

### 二、能不能把系统记事本嵌进来

**不可行**，两条硬理由：

1. Windows 11 记事本是商店打包应用（有包身份），跨进程 `SetParent` 属**未支持用法**——微软答复明确「不推荐、不保证正确停靠」，社区实测在部分机器上也不生效；
2. 它没有可复用的托管控件或公开的宿主接口。

可行替代两条：

- **A（推荐）自己实现**：页面内 `CommandBar` 工具条 + `RichEditBox` + 底部状态栏，功能对齐系统记事本；
- **B 外部唤起**：加一个按钮直接 `notepad.exe` 起系统记事本（不是嵌入，只是快捷入口）。

### 三、排版参考（系统应用 + 开源同类）

| 参考 | 可借鉴点 |
|---|---|
| Windows 设置 / PowerToys 设置 | 左栏导航 + 内容区分组卡片 + 分组小标题（`SubtitleTextBlockStyle`） |
| Files、DevToys（WinUI 3 开源工具箱） | 左栏导航 + 页面顶部 `CommandBar` 工具条 + 底部状态栏；页内边距 24；卡片用 `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush` |
| WinUI Gallery | `NavigationView` + 页面内 `CommandBar`；图标统一 `FontIcon`（Segoe Fluent Icons） |

---

### Task 1: 记事页（原「记事本」→「记事」）

**Files:** `Views/NotepadPage.xaml(.cs)`、`Tools/ToolCatalog.cs`、`Controls/StatusBar*`（如需）、`Core/Config/AppConfig.cs`

**决策（用户 2026-10-09 已确认）**：采用**方案 A**（对齐系统记事本），**不做格式化**（纯文本）；工具条在「自动换行 / 缩放」之外**再增加 保存、复制、粘贴**；**关闭程序不再自动复制到剪贴板**，复制粘贴全部手动。页内标题、时钟、置顶开关、字号按钮一律移除。

**备选（未采用）**：

- **方案 A（完整对齐系统记事本）**：`CommandBar`（自动换行、缩放 −/+ / 重置、字体、设置）+ `RichEditBox` + 底部状态栏（行列 / 字符数 / 缩放%）。工作量中，体验最接近系统记事本。
- **方案 B（极简）**：只有一个占满页面的输入框，无工具条与状态栏。工作量最小。
- 两者都要：**去掉**页内标题、时钟、置顶开关、字体按钮（按用户要求）。

**Steps（选定方案后）**

- [ ] 目录改名「记事」，`NotepadId` 保持不变（配置兼容），标题与导航文案更新
- [ ] 按 A / B 实现页面
- [ ] 构建 + 启动验证 + 记录 + 提交 + 审查

---

### Task 2: 投屏页（替换原「快捷键」页）

**Files:** `Views/HotkeysPage.xaml(.cs)` → 改造为投屏页；`MainWindow.xaml` 导航项；`ToolCatalog`

**Steps**

- [ ] 导航项文案改为「投屏」，页面标题同步
- [ ] 页面内容：投屏快捷键（`HotkeyInputBox` + 启用开关 + 立即执行）、候选模式复选（internal / extend / external / clone，至少保留一项）、当前模式只读展示
- [ ] 设置页移除投屏相关项（迁移到本页）
- [ ] 构建 + 启动验证 + 记录 + 提交 + 审查

---

### Task 3: 设置页

**Files:** `Views/SettingsPage.xaml(.cs)`、`Core/Config/AppConfig.cs`（新增 `show_window` 工具项）、`Tools/ToolCatalog.cs`、`Services/HotkeyRouter.cs`

**Steps**

- [ ] 保留「开机自启」；移除投屏候选模式（迁到投屏页）
- [ ] 新增「呼出主窗口」快捷键：配置里加 `show_window` 工具项（`HasHotkey = true`、无页面），路由映射为「显示并激活主窗口」
- [ ] 关于区：版本 + 两个路径改为**按钮形式**（点击打开对应的资源管理器路径），用 `ShellExecute` 打开 `%APPDATA%\WithWindows` 与日志文件
- [ ] 构建 + 启动验证 + 记录 + 提交 + 审查

---

### Task 4: 排版与视觉统一

**Files:** 各页面 XAML、`docs/agents/ui-conventions.md`

**Steps**

- [ ] 统一页面内边距 24、分组小标题用 `SubtitleTextBlockStyle`
- [ ] 每个工具页采用「顶部工具条 + 内容 + 底部状态栏（可选）」结构，与参考应用一致
- [ ] 卡片统一 `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush`，不嵌套双卡片
- [ ] 把「参考了哪些系统应用 / 开源项目」写进 `ui-conventions.md`
- [ ] 逐个页面截图（深色 + 浅色）确认；提交 + 审查

---

### Task 5: 文档与配置同步

- [ ] README 的功能段按新页面重写；CHANGELOG 记录本轮调整
- [ ] `docs/HANDOFF.md` 更新进度
- [ ] 提交 + 审查

---

## 已知不确定项

1. 系统记事本的「格式工具栏」偏向 Markdown 富文本；本项目是否要支持格式化（还是纯文本）需用户定调
2. 打开路径用 `ShellExecute`（CsWin32 的 `ShellExecuteW`）还是 `Process.Start`——优先前者以满足「P/Invoke 一律 CsWin32」的约束
3. `show_window` 的默认热键仍为空（不预置默认值）
