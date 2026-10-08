# With Windows · WinUI 3 工具箱重构设计

- 日期：2026-10-09
- 分支：`dev`（worktree：`C:/Users/LFN/.codex/worktrees/dev/with-windows`）
- 状态：待评审
- 依据：`AGENTS.md`（现状）、Windows App SDK 2.5 官方文档、`openai/skills` 的 `winui-app` skill、微软 `winui` 插件与 `winapp` CLI、`files-community/Files` 与 `DeskBox` 的工程实践

## 1. 目标

把当前"两个独立窗口 + 托盘"的形态，重构为一个 WinUI 3 工具箱应用：左侧 `NavigationView` 导航，每个工具是一页。首版含「记事本」「快捷键」两个工具页与「设置」页；此后新增工具只需注册，不改主窗口。

必须保留的行为（功能等价）：

- 全局热键触发动作（当前：切换投屏）
- 托盘常驻、左键唤出主窗口、右键菜单
- 开机自启开关（`HKCU\...\Run`）
- 单实例守卫
- 显示拓扑切换（`SetDisplayConfig`）
- 记事本：编辑、标题栏实时时钟、字体缩放、置顶、尺寸与位置记忆

本次改变：

- 主界面：导航壳 + 页面（工具列表可扩展）
- 记事本：打开即编辑器，页面即编辑器，切走自动保存草稿
- 快捷键页：列表形式，每行「功能名 + 录制式热键框 + 启用开关」，首版仅「切换投屏」
- 配置：全新 schema，不与旧 `v3` 兼容

## 2. 已确认决策

| 项 | 决策 | 备注 |
|---|---|---|
| 技术栈 | 继续 WinUI 3 + Windows App SDK | 不换框架 |
| 目标框架 | `net10.0-windows10.0.22621.0` | 面向 Win11 21H2 及以上 |
| 工作隔离 | git worktree + 分支 `dev` | 主检出保留旧代码供查阅 |
| 旧代码 | 结构重写，逻辑内核按清单迁移 | 见第 8 节 |
| 旧配置 | 不兼容，全新 schema | `%APPDATA%\WithWindows\config.json` 直接重建 |
| P/Invoke | CsWin32 生成 | 见 6.6 |
| 自动启动验证 | 允许 AI 启动应用做验证 | 按 `winui-app` skill 默认 |
| 文档检索 | 接 Microsoft Learn MCP | `https://learn.microsoft.com/api/mcp` |
| 提交规范 | 只写入 `AGENTS.md`，不装 git hook | — |
| 分发模型 | 非打包单文件 exe（自包含 + 框架依赖双产物） | 与现状一致 |

## 3. 技术栈锁定

| 项 | 值 |
|---|---|
| 语言 / 运行时 | C# 14 / .NET 10（LTS） |
| UI | WinUI 3，Windows App SDK 2.5.x（组件包引用方式，与现状一致） |
| TFM | `net10.0-windows10.0.22621.0` |
| 平台 | 仅 x64（`-p:Platform=x64`，发布 RID 固定 `win-x64`） |
| MVVM | CommunityToolkit.Mvvm（源生成器） |
| 托盘 | WinUIEx `TrayIcon`（已用，成熟） |
| P/Invoke | Microsoft.Windows.CsWin32 |
| 测试 | xunit（逻辑）+ WinUI UI 测试（`[UITestMethod]`） |
| UI 验证 | `winapp ui inspect / screenshot / click` |

白名单外的新依赖必须先确认。

明确不使用：WPF、Windows Forms、UWP `Windows.UI.*`、WinUI 2、.NET MAUI、Reactor（官方标注 experimental）。

### 3.1 与官方模板的差异（须显式处理）

`dotnet new winui-navview` 生成的工程默认是**打包（MSIX）项目**，模板不提供非打包开关。本项目的分发模型是非打包单文件，因此脚手架只作为**代码与结构参考**，工程文件按第 6.1 节重新组织。模板中值得直接沿用的部分：

- `MainWindow.xaml` 的 `NavigationView + Frame` 导航结构
- 新的 `TitleBar` 控件 + `MicaBackdrop`（`Window.SystemBackdrop`）
- `NavigationViewItem.Tag` 驱动的页面路由
- `Properties/PublishProfiles` 的 x64 发布配置思路

模板中**不沿用**的部分：

- `Package.appxmanifest` 与 Store 图标资产（非打包不需要）
- `EnableMsixTooling` 的默认开启（仅在单文件发布时需要，按现状保留条件开启）
- Release 默认 `PublishTrimmed=true`（WinUI 3 不支持裁剪，本项目固定 `false`）

## 4. 架构

```
src/
  WithWindows.App/                 WinUI 3 可执行项目（非打包）
    App.xaml(.cs)                  入口：单实例、配置、服务组装；静态 MainWindow 属性
    Shell/                         MainWindow（导航壳）、路由、标题栏
    Views/                         NotepadPage / HotkeysPage / SettingsPage
    ViewModels/                    每页一个 VM，只依赖 CommunityToolkit.Mvvm 与 Core
    Services/                      托盘宿主、热键宿主、窗口状态、主题
    Controls/                      HotkeyInputBox 等自定义控件
    Themes/                        资源字典
    Assets/
  WithWindows.Core/                纯逻辑类库，禁止引用任何 Microsoft.UI.Xaml 类型
    Hotkey/                        解析、格式化、注册抽象
    Config/                        配置模型、读写、自举
    Actions/                       动作框架、显示拓扑切换
    Interop/                       CsWin32 声明与封装
    Tools/                         工具抽象与注册表
    Diagnostics/                   日志
tests/
  WithWindows.Core.Tests/          纯 xunit（占绝大多数）
  WithWindows.UITests/             WinUI UI 测试
```

分层硬规则：

- `Core` 不出现任何 UI 类型，因此绝大部分逻辑可用普通单元测试覆盖。
- `ViewModels` 不 new View、不直接操作 Window、不直接弹对话框。
- 业务逻辑放 `Services` 或 `Core`，由 ViewModel 通过构造函数注入。
- 页面一个文件一组（`.xaml` + 瘦 code-behind），code-behind 只放纯 UI 逻辑。

## 5. 界面设计

### 5.1 导航壳

- `Window.SystemBackdrop = MicaBackdrop`，标题栏由 `TitleBar` 控件承担。
- 左栏 `NavigationView`：`记事本`、`快捷键`，底部 `设置`。
- 内容区 `Frame`，`NavigationViewItem.Tag` → 页面类型映射。
- 关闭主窗口 = 隐藏到托盘（不销毁），热键与托盘仍可用。

### 5.2 记事本页

- 打开即是编辑器，无独立窗口。
- 标题栏实时时钟（`DispatcherQueueTimer`，秒级）。
- `RichEditBox` + 行距 1.05；字体缩放 10–32 px（`Ctrl+滚轮` 与 `±` 按钮）。
- 置顶开关：首版作用于主窗口。
- 切走时把内容写入草稿文件，切回时恢复；窗口尺寸/位置/字号沿用状态记忆。

### 5.3 快捷键页

- 列表布局，每行：工具名 / 说明 + `HotkeyInputBox` + 启用开关。
- 首版只有「切换投屏」一条；结构上由 `ToolRegistry` 生成，后续工具自动出现。
- 注册失败（如 1409 已被占用）在该行显示错误，不静默失败。

### 5.4 设置页

- 开机自启开关、投屏模式候选（`displayMode.modes`）、关于（版本、日志路径、配置路径）。
- 使用 CommunityToolkit 的 `SettingsControls`，避免自造设置项样式。

### 5.5 UI 硬规则

- 颜色、字体、间距全部走 `ThemeResource`，禁止硬编码色值。
- 同时支持浅色与深色；每个可交互控件必须有 `AutomationProperties.AutomationId`，重要控件补 `Name`。
- 控件选择顺序：WinUI 3 内置 → CommunityToolkit.WinUI → 自写。
- 分组命令优先用 `CommandBar`，不用散落的按钮拼装。
- 像素级视觉细节（间距微调、配色、动效曲线）由人决定，不由 AI 自行拍板。

## 6. 关键设计

### 6.1 工程文件

| 文件 | 作用 |
|---|---|
| `Directory.Build.props` | 统一 TFM、`LangVersion`、`Nullable`、`Platform`、`PublishTrimmed=false` |
| `Directory.Packages.props` | 中央包版本管理（Windows App SDK 组件版本必须整体对齐） |
| `global.json` | 钉死 SDK 版本，避免版本漂移 |
| `.editorconfig` | 缩进、换行、using 排序、命名偏好（AI 的格式依据） |
| `.gitattributes` | `*.cs text eol=crlf` |
| `nuget.config` | 固定包源 |

`WithWindows.App.csproj` 关键属性：`WindowsPackageType=None`、`UseWinUI=true`、`RuntimeIdentifier=win-x64`、`Platforms=x64`、`SelfContained`/`PublishSingleFile` 由发布命令传入、`EnableMsixTooling` 仅在单文件发布时为 `true`。

### 6.2 工具抽象

```csharp
public interface ITool
{
    string Id { get; }              // "notepad"
    string Title { get; }           // 记事本
    string Glyph { get; }           // Segoe Fluent Icons 字形
    Type PageType { get; }          // 对应页面
    IAction? Action { get; }        // 可选：可绑热键的动作
}
```

`ToolRegistry` 从配置生成导航项与热键绑定；新增工具 = 新增一个实现 + 配置一行，主窗口代码不变。

### 6.3 配置

全新 schema（版本号从 1 开始，旧 v3 不兼容）：

```json
{
  "version": 1,
  "tools": [
    { "id": "notepad", "enabled": true, "hotkey": "F13" },
    { "id": "display_mode", "enabled": true, "hotkey": "F14" }
  ],
  "displayMode": { "modes": ["internal", "extend"] },
  "windowState": { "width": 900, "height": 640, "x": null, "y": null, "notepadFontSize": 14 }
}
```

保留旧实现的优秀模式：写入用「同目录临时文件 + `File.Replace`」原子替换、旧内容留 `.bak`、主文件损坏时回退 `.bak`。

### 6.4 热键

- 语法与校验规则保留：修饰键 `Ctrl/Alt/Shift/Win`、F1–F24、字母数字；字母数字必须带修饰键；`MOD_NOREPEAT`。
- 默认键保留 `F13`（记事本）与 `F14`（切换投屏）——可编程键盘专用键，不与常见软件冲突。
- 注册与分发继续使用**独立隐藏消息窗口**（`RegisterClassEx` + `CreateWindowEx` + 自持 `WndProc` 委托），**不要**子类化 WinUI 窗口：已两次验证子类化会和 WinUI 3 消息泵冲突导致原生崩溃。
- 句柄的生命周期由 `HotkeyManager` 持有，`Dispose` 时显式注销。

### 6.5 显示切换

- 沿用 `SetDisplayConfig` + `SDC_APPLY` 直接应用拓扑（比 `DisplaySwitch.exe` 可靠、同步）。
- 当前拓扑用 `QueryDisplayConfig(QDC_DATABASE_CURRENT)` 返回的权威 `DISPLAYCONFIG_TOPOLOGY_ID` 判定，不用路径启发式。
- 「目标模式 == 当前模式」时返回 `Changed=false`，宿主不重复提示。
- `toggle` 的候选模式来自配置，纯函数选择下一个目标，便于测试。

### 6.6 互操作

- 新代码的普通 P/Invoke 一律走 **CsWin32**（`NativeMethods.txt` 声明 + 源生成），不再手写 `DllImport`。
- 已封装的 Win32 调用集中在一个互操作项目或命名空间，禁止散落。
- 结构体布局必须与原生一致：`DISPLAYCONFIG_PATH_INFO` / `DISPLAYCONFIG_MODE_INFO` 按 `wingdi.h` 定义；系统会写入这些缓冲区，尺寸错误会越界。
- `Core` 不依赖 UI，故互操作只依赖 Win32 与 windows 元数据，纯函数部分可被单元测试覆盖。

### 6.7 诊断与生命周期

- 日志：`%APPDATA%\WithWindows\log.txt`，1 MB 轮转为 `log.1.txt`；`Logger` 由 App 持有整个进程生命周期，禁止用局部 `using`。
- 单实例：`Local\WithWindows.SingleInstance` 命名 Mutex，由 App 字段持到进程退出。
- 运行数据全部位于 `%APPDATA%\WithWindows\`，exe 目录保持干净。

## 7. AI 开发工作流

### 7.1 三层知识 + 一个闭环

| 层 | 组件 | 解决的问题 |
|---|---|---|
| 领域知识 | `winui-app` skill（openai curated）+ 微软 `winui` 插件（8 个 skill） | API 代际混淆、导航与主题的官方做法 |
| 验证能力 | `winapp` CLI + `winui-ui-testing` skill | AI 能自己看到界面：无障碍树 + 截图 + 点击 |
| 实时文档 | Microsoft Learn MCP | 杜绝凭记忆编 API |
| 项目知识 | 分层 `AGENTS.md` | 本项目积累的坑（外部 skill 无法提供） |

### 7.2 每轮改动的固定闭环

```
1. dotnet build              → 零错误
2. dotnet run                → 必须真的启动起来
3. winapp ui inspect         → 确认新控件出现在无障碍树
4. winapp ui screenshot      → 目视确认布局没错乱
5. 对照需求自检
6. 才允许声称完成
```

构建或启动连续 3 次未修复，停止并报告：卡在哪一步、完整错误、已排除的可能、建议的下一步。不允许为绕开问题更换框架或大范围重写。

### 7.3 AGENTS.md 结构（采纳"单一入口 + 按需分册"）

根 `AGENTS.md` 控制在 100 行内，只放"永远适用"的内容；领域细节拆到分册，按需读取。

| 文件 | 内容 |
|---|---|
| `AGENTS.md` | 定位、技术栈锁定、硬约束、命令表、目录地图、DoD、分册索引 |
| `docs/agents/winui3-hard-rules.md` | 不报编译错误但运行失败的坑、UWP → WinUI 3 映射表、`ContentDialog.XamlRoot`、`FileOpenPicker` 初始化、`DispatcherQueue` 等 |
| `docs/agents/development-workflow.md` | 六步闭环、`winapp ui` 用法、AutomationId 要求、构建失败处理、DoD |
| `docs/agents/ui-conventions.md` | 导航壳、主题与 Mica、控件优先级、自适应、图标 |
| `docs/agents/interop-and-platform.md` | CsWin32 规则、隐藏消息窗口的原因、显示拓扑、托盘、注册表 |
| `docs/agents/known-pitfalls.md` | 本项目积累的坑，持续追加 |

理由：硬约束必须每轮可见，领域细节只在相关改动时读取；避免把上万字的规范全文塞进每轮上下文。附录性质的映射表放在分册里，不重复粘贴到根文件。

### 7.4 环境前置

```
winget install --id Microsoft.WinAppCli
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
```

并安装 `winui-app` skill（`openai/skills` curated）与微软 `winui` 插件；配置 Microsoft Learn MCP。模板包当前为 `0.0.7-alpha`，仅作参考，不改变本项目手工维护的工程文件。

## 8. 复用与重写清单

判定原则：**逻辑内核迁移，代码在新结构下重写；不复用旧 XAML。** 迁移时必须逐条复核下列硬知识，避免重新踩坑。

| 旧模块 | 处理 | 必须保留的细节 |
|---|---|---|
| `Core/Hotkey.cs`、`HotkeyParser`、`HotkeyFormatter` | 迁移 | 修饰键校验、F1–F24 编解码、字母数字必须带修饰键、`MOD_NOREPEAT` |
| `Core/HotkeyManager` | 迁移并重写封装 | 隐藏消息窗口方案、`WndProc` 委托自持防 GC、失败返回错误码而非抛异常（1409 = 已占用） |
| `Core/SingleInstance` | 迁移 | `AbandonedMutexException` 时接管 |
| `Core/AutoStart` | 迁移 | 写 exe 全路径并加引号 |
| `Config/ConfigStore` | 重写 schema，保留模式 | 原子替换 + `.bak`、损坏回退、自愈式清理未知绑定 |
| `Actions/DisplayModeAction`、`DisplayTopology` | 迁移 | `QDC_DATABASE_CURRENT` 权威拓扑、`Changed=false` 语义、toggle 纯函数 |
| `Interop/DisplayConfigTypes` | 迁移（CsWin32 下同样需要） | 结构体字节布局按 `wingdi.h` |
| `Interop/NativeMethods` | 重写为 CsWin32 | 常量与语义保留；不再手写 `DllImport` |
| `Logger` | 迁移 | 1 MB 轮转、被占用时降级为丢弃 |
| `MainWindow`、`ToggleWindow`、`NotepadWindow` XAML | 重写 | 无复用价值 |
| `Controls/HotkeyInputBox` | 重写 | 录制交互的行为契约（聚焦后捕获、Esc 取消、清除） |

## 9. 测试策略

| 层 | 项目 | 覆盖 |
|---|---|---|
| 逻辑 | `WithWindows.Core.Tests` | 热键解析/格式化、配置读写与容错、toggle 目标选择、`Changed` 语义、动作分发 |
| UI | `WithWindows.UITests` | 实例化 XAML 的测试必须用 `[UITestMethod]`；页面能否导航、控件是否存在 |
| 探索 | `winapp ui inspect / screenshot` | 每轮改动的自检，不是回归测试 |

平台相关只做只读断言，禁止在测试中改变真实显示状态。

## 10. 交付与发布

- 双产物：`-x64-selfcontained.exe`（约 167 MB）与 `-x64-frameworkdependent.exe`（约 38 MB），与现状一致。
- `ci.yml` 继续只跑 `pull_request`，`release.yml` 继续版本号驱动；发布前必须在 `CHANGELOG.md` 顶部补同版本章节。
- 常驻实例会锁 exe，构建前必须先停掉。
- 每次改动后复查 `README.md` 与 `CHANGELOG.md`。

## 11. 分阶段计划

| 阶段 | 内容 | 验收 |
|---|---|---|
| P0 环境与规范 | 安装 `winapp` CLI、模板包、skill 与插件、Learn MCP；建立 6 个规范文件；写好分层 `AGENTS.md` | `winapp --version` 可用；`dotnet new list winui` 有模板；分册可从根文件索引到 |
| P1 工程骨架 | 新目录结构落地；非打包工程文件完成；`dotnet run` 启动；`winapp ui inspect/screenshot` 拍到 NavigationView 空壳 | 截图可见导航壳 |
| P2 Core 迁移 | 按第 8 节迁移 Core 与互操作；CsWin32 生效；测试迁到 `Core.Tests` | Core 测试全绿且不拉起 WinUI |
| P3 页面 | 记事本页、快捷键页、设置页 | 控件在无障碍树中可见；截图符合 Win11 风格 |
| P4 工具抽象 | `ITool` + `ToolRegistry` + 配置 v1 落地 | 新增工具仅需注册，主窗口不改 |
| P5 打磨 | 深浅色、标题栏、自适应、图标 | 浅色与深色各一张截图 |

旧目录（`src/WithWindows.UI`、旧测试工程、旧 `ToggleWindow`/`NotepadWindow`）在 `dev` 分支于 P2 完成后删除；主检出的 `main` 始终保留旧实现供查阅。

## 12. 风险与未决

1. **CsWin32 与窗口类注册**：`RegisterClassEx` 的 `WNDCLASSEX` 与 `WndProc` 委托需要函数指针，需验证 CsWin32 生成结果；若无干净写法，保留这一处最小手写互操作并注明原因。
2. **模板默认 `PublishTrimmed=true`**：WinUI 3 不支持裁剪，本项目固定 `false`，发布体积按现状评估。
3. **`winapp` CLI 处于公共预览**：命令语法可能变动，执行前用 `winapp --help` 核对。
4. **托盘与主窗口隐藏**：关闭主窗口仅隐藏时，需验证热键与托盘仍工作，且再次唤出时焦点正常。
5. **第三方示例数字**（如"装 skill 后 30 分钟完成""token 降 70%"）来自他人自述，不作为验收依据。

## 13. 完成定义（DoD）

- [ ] `dotnet build -p:Platform=x64` 零错误
- [ ] `dotnet test -p:Platform=x64` 全绿
- [ ] `dotnet run` 能启动，且已实际启动验证
- [ ] `winapp ui inspect` 能看到新增控件
- [ ] `winapp ui screenshot` 目视无错乱，浅色与深色各一张
- [ ] 新增可交互控件均有 `AutomationProperties.AutomationId`
- [ ] 新增逻辑有对应单元测试
- [ ] `README.md` 与 `CHANGELOG.md` 已同步
