# With Windows · WinUI 3 工具箱重构设计

- 日期：2026-10-09
- 分支：`dev`（worktree：`C:/Users/LFN/Documents/Code/with-windows/.worktrees/dev`）
- 状态：待评审
- 依据：Windows App SDK 2.5 官方文档、`openai/skills` 的 `winui-app` skill、微软 `winui` 插件与 `winapp` CLI、`PowerToys` / `Files` / `DeskBox` 等项目的实际工程实践

> **实现偏差（2026-10-11 补注）**：本文是 2026-10-09 的设计稿，落地时有两处偏离，读的时候以代码与 `AGENTS.md` 为准：
>
> 1. **没有 `ViewModels/` 层**：三个页面都是 code-behind（配置与控件的双向同步靠手工回写），`CommunityToolkit.Mvvm` 依赖也已移除。何时再考虑引入，见 `AGENTS.md` 的「MVVM（当前不引入）」。
> 2. **目录结构**：实际是 `App/Views|Controls|Services|Tools|Assets` 与 `Core/Actions|App|Config|Diagnostics|Hotkeys|Interop`，没有 `Shell/`、`Themes/`。
>
> 另外本文表格里「面向 Win11 21H2 及以上」有误：`TargetPlatformMinVersion` 是 `10.0.22621.0`，即 **Win11 22H2（22621）及以上**。
>
> 工作区也变了：`dev` 分支与 `.worktrees/dev` 已合并进 `main` 并清理，当前只有 `main`（见 `docs/HANDOFF.md`）。

## 1. 目标

把当前"两个独立窗口 + 托盘"的形态，重构为一个 WinUI 3 工具箱应用：左侧 `NavigationView` 导航，每个工具是一页。首版含「记事本」「快捷键」两个工具页与「设置」页；此后新增工具只需注册，不改主窗口。

必须保留的行为（功能等价）：

- 全局热键触发动作（当前：切换投屏）
- 托盘常驻、左键唤出主窗口、右键菜单
- 开机自启开关
- 单实例守卫
- 显示拓扑切换（`SetDisplayConfig`）
- 记事本：编辑、标题栏实时时钟、字体缩放、置顶、尺寸与位置记忆、草稿持久化

本次改变：

- 主界面：导航壳 + 页面（工具列表可扩展）
- 记事本：打开即编辑器，页面即编辑器
- 快捷键页：列表形式，每行「功能名 + 录制式热键框 + 启用开关 + 立即执行」，首版仅「切换投屏」；不预置默认热键
- 分发：改为 **EXE 安装器**（未打包 + Inno Setup）
- 配置：全新 schema，不与旧 `v3` 兼容

## 2. 已确认决策

| 项 | 决策 | 备注 |
|---|---|---|
| 技术栈 | 继续 WinUI 3 + Windows App SDK | 不换框架 |
| 目标框架 | `net10.0-windows10.0.22621.0` | 面向 Win11 21H2 及以上 |
| **打包模型** | **未打包（unpackaged，`WindowsPackageType=None`）** | 见 10.1 |
| **分发方式** | **Inno Setup EXE 安装器 + GitHub Releases** | 见 10.3 |
| **运行时部署** | **自包含 + 单文件 exe** | 用户无需预装运行时，见 10.2 |
| **默认热键** | **不预置任何默认值**，全部由用户录入 | 见 6.4 |
| 工作隔离 | git worktree + 分支 `dev` | `.worktrees/dev`（仓库内，已忽略） |
| 旧代码 | 结构重写，逻辑内核按清单迁移 | 见第 8 节 |
| 旧配置 | 不兼容，全新 schema | 直接重建 |
| P/Invoke | CsWin32 生成 | 见 6.6 |
| 自动启动验证 | 允许 AI 启动应用做验证 | 按 `winui-app` skill 默认 |
| 文档检索 | 接 Microsoft Learn MCP | `https://learn.microsoft.com/api/mcp` |
| 提交规范 | 只写入 `AGENTS.md`，不装 git hook | — |

## 3. 技术栈锁定

| 项 | 值 |
|---|---|
| 语言 / 运行时 | C# 14 / .NET 10（LTS） |
| UI | WinUI 3，Windows App SDK 2.5.x（组件包引用，与现状一致） |
| TFM | `net10.0-windows10.0.22621.0` |
| 平台 | 仅 x64（`-p:Platform=x64`，RID `win-x64`） |
| MVVM | CommunityToolkit.Mvvm（源生成器） |
| 托盘 | WinUIEx `TrayIcon` |
| P/Invoke | Microsoft.Windows.CsWin32 |
| 打包 | 未打包 + `PublishSingleFile` + `WindowsAppSDKSelfContained` |
| 安装器 | Inno Setup（`iscc`） |
| 测试 | xunit（逻辑层） |
| UI 验证 | `winapp ui inspect / screenshot / click` |

白名单外的新依赖必须先确认。

明确不使用：WPF、Windows Forms、UWP `Windows.UI.*`、WinUI 2、.NET MAUI、Reactor（官方标注 experimental）。

### 3.1 脚手架策略

官方模板 `dotnet new winui-navview` 生成的是**打包（MSIX）项目**，且当前模板版本（`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.7-alpha`）**不提供非打包开关**。因此脚手架只作**结构参考**，工程文件按未打包方式手工组织——这与旧项目现有配置一致（`WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`）。

模板中值得沿用的部分：

- `NavigationView + Frame` 导航结构与 `Tag` 路由
- 新的 `TitleBar` 控件 + `MicaBackdrop`（`Window.SystemBackdrop`）
- `Controls` / `Pages` / `Properties` 的目录划分

模板中**不沿用**的部分：

- `Package.appxmanifest` 与 Store 图标资产（未打包不需要）
- `Microsoft.Windows.SDK.BuildTools.WinApp` 包引用（打包模式专用；`winapp` CLI 仍单独用于 UI 验证）
- Release 默认 `PublishTrimmed=true`（WinUI 3 不支持裁剪，固定 `false`）

## 4. 架构

```
src/
  WithWindows.App/                 WinUI 3 可执行项目（未打包）
    App.xaml(.cs)                  入口：单实例、配置、服务组装；静态 MainWindow 属性
    Shell/                         MainWindow（导航壳）、路由、标题栏、时钟
    Views/                         NotepadPage / HotkeysPage / SettingsPage
    ViewModels/                    每页一个 VM，只依赖 CommunityToolkit.Mvvm 与 Core
    Services/                      托盘宿主、热键宿主、窗口状态
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
installer/
  WithWindows.iss                  Inno Setup 脚本
```

分层硬规则：

- `Core` 不出现任何 UI 类型，因此绝大部分逻辑可用普通单元测试覆盖。
- `ViewModels` 不 new View、不直接操作 Window、不直接弹对话框。
- 业务逻辑放 `Services` 或 `Core`，由 ViewModel 通过构造函数注入。
- 页面一个文件一组（`.xaml` + 瘦 code-behind），code-behind 只放纯 UI 逻辑。

UI 自动化测试项目（`winui-unittest`）首版不建：AI 的界面验证由 `winapp ui` 承担（见 7.2），等出现具体的回归测试需求再补，避免一开始就背上打包测试工程的复杂度。

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
- 草稿持久化是**既有行为**，继续保留：`%APPDATA%\WithWindows\notepad.txt`，编辑后用防抖计时器自动写入，打开时读回。
- 窗口尺寸/位置/字号沿用状态记忆。

### 5.3 快捷键页

- 列表布局，每行：工具名 / 说明 + `HotkeyInputBox` + 启用开关 + 立即执行。
- 首版只有「切换投屏」一条；结构上由 `ToolRegistry` 生成，后续工具自动出现。
- 注册失败（如 1409 已被占用）在该行显示错误，不静默失败。
- **不预置默认热键**：首次运行时该行显示「未设置」，由用户点击录制框自行录入。
- 「立即执行」按钮让功能在用户尚未录入热键时也可用，同时便于验证。

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

`WithWindows.App.csproj` 关键属性：`WindowsPackageType=None`、`UseWinUI=true`、`RuntimeIdentifier=win-x64`、`Platforms=x64`、`WindowsAppSDKSelfContained=true`、`PublishTrimmed=false`、`PublishSingleFile` 由发布命令传入、`EnableMsixTooling` 仅在单文件发布时为 `true`。

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

### 6.3 配置与数据位置

全新 schema（版本号从 1 开始，旧 v3 不兼容）：

```json
{
  "version": 1,
  "tools": [
    { "id": "notepad", "enabled": true, "hotkey": null },
    { "id": "display_mode", "enabled": true, "hotkey": null }
  ],
  "displayMode": { "modes": ["internal", "extend"] },
  "windowState": { "width": 900, "height": 640, "x": null, "y": null, "notepadFontSize": 14 }
}
```

`hotkey` 为 `null` 或空字符串表示**不绑定**，此时不注册任何全局热键。

数据位置：`%APPDATA%\WithWindows\`（配置、日志、记事本草稿）。安装目录只放程序与资源。

保留旧实现的优秀模式：写入用「同目录临时文件 + `File.Replace`」原子替换、旧内容留 `.bak`、主文件损坏时回退 `.bak`、自愈式清理未知绑定（旧版本曾因遗留的 `theme` 绑定占住 F14，导致 `display_mode` 永远注册失败）。

### 6.4 热键

- 语法与校验规则保留：修饰键 `Ctrl/Alt/Shift/Win`、F1–F24、字母数字；字母数字必须带修饰键；`MOD_NOREPEAT`。
- **不设默认热键**：新装后不注册任何全局热键。理由：默认值必然与部分用户的既有快捷键冲突，而冲突表现为"按了没反应"，用户既不知道原因也不知道去哪儿改。
- 注册与分发继续使用**独立隐藏消息窗口**（`RegisterClassEx` + `CreateWindowEx` + 自持 `WndProc` 委托），**不要**子类化 WinUI 窗口：已两次验证子类化会和 WinUI 3 消息泵冲突导致原生崩溃。
- 句柄生命周期由 `HotkeyManager` 持有，`Dispose` 时显式注销；注册失败返回错误码而非抛异常（1409 = 已被占用）。
- 工具被禁用或热键被清空时，必须先注销再刷新，避免残留绑定继续占用按键。

### 6.5 显示切换

- 沿用 `SetDisplayConfig` + `SDC_APPLY` 直接应用拓扑（比 `DisplaySwitch.exe` 可靠、同步）。
- 当前拓扑用 `QueryDisplayConfig(QDC_DATABASE_CURRENT)` 返回的权威 `DISPLAYCONFIG_TOPOLOGY_ID` 判定，不用路径启发式。
- 「目标模式 == 当前模式」时返回 `Changed=false`，宿主不重复提示。
- `toggle` 的候选模式来自配置，纯函数选择下一个目标，便于测试。

### 6.6 互操作

- 新代码的普通 P/Invoke 一律走 **CsWin32**（`NativeMethods.txt` 声明 + 源生成），不再手写 `DllImport`。
- 已封装的 Win32 调用集中在 `Core/Interop`，禁止散落。
- 结构体布局必须与原生一致：`DISPLAYCONFIG_PATH_INFO` / `DISPLAYCONFIG_MODE_INFO` 按 `wingdi.h` 定义；系统会写入这些缓冲区，尺寸错误会越界。

### 6.7 生命周期

未打包应用没有包身份，因此：

**单实例**：命名 Mutex（`Local\WithWindows.SingleInstance`），由 `App` 字段持有到进程退出。禁止写成 `OnLaunched` 里的局部 `using` —— 方法返回即释放，常驻期间第二个实例照样能起。

**开机自启**：写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值为 exe 全路径并加引号（用户级、无需管理员权限）。相比 `startupTask`，这个方式的好处是**用户被外部禁用后，程序仍能重新启用**。

**日志**：`%APPDATA%\WithWindows\log.txt`，1 MB 轮转为 `log.1.txt`；`Logger` 由 App 持有整个进程生命周期，禁止用局部 `using`。

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

### 7.3 AGENTS.md 结构（单一入口 + 按需分册）

根 `AGENTS.md` 控制在 100 行内，只放"永远适用"的内容；领域细节拆到分册，按需读取。

| 文件 | 内容 |
|---|---|
| `AGENTS.md` | 定位、技术栈锁定、硬约束、命令表、目录地图、DoD、分册索引 |
| `docs/agents/winui3-hard-rules.md` | 不报编译错误但运行失败的坑、UWP → WinUI 3 映射表、`ContentDialog.XamlRoot`、`FileOpenPicker` 初始化、`DispatcherQueue` 等 |
| `docs/agents/development-workflow.md` | 六步闭环、`winapp ui` 用法、AutomationId 要求、构建失败处理、DoD |
| `docs/agents/ui-conventions.md` | 导航壳、主题与 Mica、控件优先级、自适应、图标 |
| `docs/agents/interop-and-platform.md` | CsWin32 规则、隐藏消息窗口的原因、显示拓扑、托盘、注册表 |
| `docs/agents/known-pitfalls.md` | 本项目积累的坑，持续追加 |

理由：硬约束必须每轮可见，领域细节只在相关改动时读取；避免把上万字的规范全文塞进每轮上下文。

### 7.4 环境前置

```
winget install --id Microsoft.WinAppCli
winget install --id JRSoftware.InnoSetup
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
```

外加：安装 `winui-app` skill 与微软 `winui` 插件、配置 Microsoft Learn MCP。模板包当前为 `0.0.7-alpha`，仅作参考，日常开发不依赖它。

## 8. 复用与重写清单

判定原则：**逻辑内核迁移，代码在新结构下重写；不复用旧 XAML。** 迁移时必须逐条复核下列硬知识，避免重新踩坑。

| 旧模块 | 处理 | 必须保留的细节 |
|---|---|---|
| `Core/Hotkey.cs`、`HotkeyParser`、`HotkeyFormatter` | 迁移 | 修饰键校验、F1–F24 编解码、字母数字必须带修饰键、`MOD_NOREPEAT`。改造点：解析层不再直接依赖 Win32 常量，在 Core 定义修饰键枚举，只在边界映射 |
| `Core/HotkeyManager` | 迁移并重写封装 | 隐藏消息窗口方案、`WndProc` 委托自持防 GC、失败返回错误码而非抛异常 |
| `Core/SingleInstance` | **迁移**（未打包下继续适用） | `AbandonedMutexException` 时接管 |
| `Core/AutoStart` | **迁移**（未打包下继续适用） | 写 exe 全路径并加引号 |
| `Config/ConfigStore` | 重写 schema，保留模式 | 原子替换 + `.bak`、损坏回退、自愈式清理未知绑定 |
| `Actions/DisplayModeAction`、`DisplayTopology` | 迁移 | `QDC_DATABASE_CURRENT` 权威拓扑、`Changed=false` 语义、toggle 纯函数 |
| `Interop/DisplayConfigTypes` | 迁移，改为 CsWin32 生成 | 结构体布局必须与 `wingdi.h` 一致 |
| `Interop/NativeMethods` | 重写为 CsWin32 | 常量与语义保留；不再手写 `DllImport` |
| `Logger` | 迁移 | 1 MB 轮转、被占用时降级为丢弃 |
| `MainWindow`、`ToggleWindow`、`NotepadWindow` XAML | 重写 | 无复用价值 |
| `Controls/HotkeyInputBox` | 重写 | 录制交互的行为契约（聚焦后捕获、Esc 取消、清除） |

## 9. 测试策略

| 层 | 方式 | 覆盖 |
|---|---|---|
| 逻辑 | `WithWindows.Core.Tests`（xunit） | 热键解析/格式化、配置读写与容错、toggle 目标选择、`Changed` 语义、动作分发 |
| 界面 | `winapp ui inspect / screenshot` | 每轮改动的自检：控件是否存在、布局是否错乱 |

平台相关只做只读断言，禁止在测试中改变真实显示状态。

## 10. 打包与分发

### 10.1 打包模型：未打包

决定采用**未打包（unpackaged）**，理由是分发摩擦：

| | 未打包 + EXE 安装器（本方案） | MSIX 打包 |
|---|---|---|
| 用户下载后能否安装 | ✅ 能（SmartScreen 提示可跳过） | ❌ 自签名会被直接阻止安装 |
| 是否需要证书 | 不需要 | 必须受信任证书 |
| 管理员权限 | 可按需请求 | ❌ 不能提权（mediumIL） |
| 干净安装/卸载 | 由安装器负责 | ✅ 系统原生 |
| 自动更新 | 需自建 | ✅ `.appinstaller` / 商店 |

生态佐证：GitHub 上主流的做法是 EXE 安装器 + 便携包（PowerToys、ShareX、Auto Dark Mode、Flow Launcher、DeskBox、Winhance 等）；走 MSIX 的项目多数把用户导向商店——`Files`（45.9k star）与 `RyTuneX` 在 GitHub Release 里**没有任何二进制产物**。

代价：失去包身份。因此**开机自启回到 `HKCU\...\Run`**、**单实例回到命名 Mutex**——这两点旧实现已有可用代码（见第 8 节）。

### 10.2 运行时：自包含 + 单文件

`WindowsAppSDKSelfContained=true` + `PublishSingleFile=true`：用户无需预装 .NET 10 桌面运行时与 Windows App Runtime，代价是体积约 167 MB。

保留单文件的额外收益：**同一个 exe 既是安装器载荷，也可直接当便携版运行**。代价是冷启动比非单文件略慢；若日后在意，改回目录式发布即可（安装器逻辑无需改动）。

### 10.3 安装器：Inno Setup

选它的理由：脚本语法简单（AI 容易写对）、生态成熟、中文界面支持好、单个编译器可离线运行。

安装器职责：

- 安装到 `Program Files`（或用户目录，按需），带图标与版本信息
- 创建开始菜单项与可选桌面快捷方式
- 注册卸载项，卸载时清理程序目录
- 可选：按用户选择写入开机自启（也可完全交给应用内开关）

### 10.4 代码签名：暂不做

未签名分发的影响仅限首次运行出现 SmartScreen「未知发布者」提示，用户点「仍要运行」即可继续——这与 MSIX 自签名"直接拒绝安装"有本质区别。

将来若想消除提示，两条路：**SignPath**（为开源项目提供赞助签名、支持 CI 集成，需核实资格）或把安装器走商店的 MSI/EXE 提交路径。

### 10.5 CI 调整

`release.yml` 从"发布两个单文件 exe + zip + SHA256"改为：

1. 构建自包含单文件 exe（x64）。
2. 用 Inno Setup 编译安装器。
3. 上传安装器、便携 exe、`SHA256SUMS.txt` 到 Release。

### 10.6 注意事项

- 常驻实例会锁住输出文件，构建前先关闭应用。
- 每次改动后复查 `README.md` 与 `CHANGELOG.md`；README 的安装说明整段需要重写。

## 11. 分阶段计划

| 阶段 | 内容 | 验收 |
|---|---|---|
| P0 规范与环境 | 装 `winapp` CLI、Inno Setup、模板包、skill 与插件、Learn MCP；建立 6 个规范文件；写好分层 `AGENTS.md` | `winapp --version` 可用；分册可从根文件索引到 |
| P1 工程骨架 | 建 App / Core / Core.Tests；非打包工程文件；`dotnet run` 启动；`winapp ui inspect/screenshot` 拍到导航壳 | 截图可见导航壳 |
| P2 Core 迁移 | 按第 8 节迁移 Core 与互操作；CsWin32 生效；测试迁到 `Core.Tests` | Core 测试全绿且不拉起 WinUI |
| P3 页面 | 记事本页、快捷键页、设置页 | 控件在无障碍树中可见；截图符合 Win11 风格 |
| P4 工具抽象 | `ITool` + `ToolRegistry` + 配置 v1 落地 | 新增工具仅需注册，主窗口不改 |
| P5 打磨与打包 | 深浅色、标题栏、自适应、图标；Inno Setup 脚本；CI 改为产出安装器；README 重写 | 安装器能在干净机器上装好并常驻 |

旧目录（`src/WithWindows.UI`、旧测试工程）在 `dev` 分支于 P2 完成后删除；主检出的 `main` 始终保留旧实现供查阅。

## 12. 风险与未决

1. **CsWin32 与窗口类注册**：`RegisterClassEx` 的 `WNDCLASSEX` 与 `WndProc` 委托需要函数指针，需验证 CsWin32 生成结果；若无干净写法，保留这一处最小手写互操作并注明原因。
2. **裁剪**：模板 Release 默认 `PublishTrimmed=true`，与"WinUI 3 不支持裁剪"的结论冲突，需实测后固定取值。
3. **SmartScreen 提示**：未签名安装器首次运行会出现「未知发布者」提示，属预期行为，README 需说明。
4. **首次运行无热键**：不预置默认值意味着用户必须自己录入才能用上全局热键；快捷键页需要明确的空状态引导，并保留「立即执行」入口。
5. **单文件冷启动**：单文件自包含的启动开销略高于目录式，需实测确认是否可接受；不可接受则改回目录式发布。
6. **`winapp` CLI 处于公共预览**：命令语法可能变动，执行前用 `winapp --help` 核对。
7. **托盘与主窗口隐藏**：关闭主窗口仅隐藏时，需验证热键与托盘仍工作，且再次唤出时焦点正常。
8. **第三方示例数字**（如"装 skill 后 30 分钟完成""token 降 70%"）来自他人自述，不作为验收依据。

## 13. 完成定义（DoD）

- [ ] `dotnet build -p:Platform=x64` 零错误
- [ ] `dotnet test -p:Platform=x64` 全绿
- [ ] `dotnet run` 能启动，且已实际启动验证
- [ ] `winapp ui inspect` 能看到新增控件
- [ ] `winapp ui screenshot` 目视无错乱，浅色与深色各一张
- [ ] 新增可交互控件均有 `AutomationProperties.AutomationId`
- [ ] 新增逻辑有对应单元测试
- [ ] `README.md` 与 `CHANGELOG.md` 已同步
