# With Windows · WinUI 3 工具箱重构设计

- 日期：2026-10-09
- 分支：`dev`（worktree：`C:/Users/LFN/Documents/Code/with-windows/.worktrees/dev`）
- 状态：待评审
- 依据：Windows App SDK 2.5 官方文档、`openai/skills` 的 `winui-app` skill、微软 `winui` 插件与 `winapp` CLI、`files-community/Files` 与 `DeskBox` 的工程实践

## 1. 目标

把当前"两个独立窗口 + 托盘"的形态，重构为一个 WinUI 3 工具箱应用：左侧 `NavigationView` 导航，每个工具是一页。首版含「记事本」「快捷键」两个工具页与「设置」页；此后新增工具只需注册，不改主窗口。

必须保留的行为（功能等价）：

- 全局热键触发动作（当前：切换投屏）
- 托盘常驻、左键唤出主窗口、右键菜单
- 开机自启开关
- 单实例守卫
- 显示拓扑切换（`SetDisplayConfig`）
- 记事本：编辑、标题栏实时时钟、字体缩放、置顶、尺寸与位置记忆

本次改变：

- 主界面：导航壳 + 页面（工具列表可扩展）
- 记事本：打开即编辑器，页面即编辑器（草稿自动保存为既有行为，继续保留）
- 快捷键页：列表形式，每行「功能名 + 录制式热键框 + 启用开关 + 立即执行」，首版仅「切换投屏」；不预置默认热键
- 分发：改为 **MSIX 打包**（原为非打包单文件 exe）
- 配置：全新 schema，不与旧 `v3` 兼容

## 2. 已确认决策

| 项 | 决策 | 备注 |
|---|---|---|
| 技术栈 | 继续 WinUI 3 + Windows App SDK | 不换框架 |
| 目标框架 | `net10.0-windows10.0.22621.0` | 面向 Win11 21H2 及以上 |
| **打包模型** | **MSIX 打包（packaged）** | 新建 WinUI 3 应用的官方默认 |
| **运行时部署** | **框架依赖（framework-dependent）** | Windows App SDK 默认模式；打包应用会自动安装运行时 |
| 分发方式 | GitHub Releases + 自签名证书 + `.appinstaller` 自动更新 | 见 10.3 |
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
| UI | WinUI 3，Windows App SDK 2.5.x（伞包引用，模板默认） |
| TFM | `net10.0-windows10.0.22621.0` |
| 平台 | 仅 x64（`-p:Platform=x64`，RID `win-x64`） |
| MVVM | CommunityToolkit.Mvvm（源生成器） |
| 托盘 | WinUIEx `TrayIcon` |
| P/Invoke | Microsoft.Windows.CsWin32 |
| 打包 | MSIX（单项目 MSIX，`EnableMsixTooling`） |
| 测试 | xunit（逻辑）+ `winui-unittest`（`[UITestMethod]`） |
| UI 验证 | `winapp ui inspect / screenshot / click` |

白名单外的新依赖必须先确认。

明确不使用：WPF、Windows Forms、UWP `Windows.UI.*`、WinUI 2、.NET MAUI、Reactor（官方标注 experimental）。

### 3.1 脚手架策略（打包后大幅简化）

官方模板 `dotnet new winui-navview` 生成的**就是打包项目**，与本方案一致，因此直接作为 `WithWindows.App` 的基底，不再重写工程结构。模板已内置、可直接沿用：

- `NavigationView + Frame` 导航结构与 `Tag` 路由
- 新的 `TitleBar` 控件 + `MicaBackdrop`（`Window.SystemBackdrop`）
- `Package.appxmanifest`、Store 图标资产、`Properties/PublishProfiles`
- `Microsoft.Windows.SDK.BuildTools.WinApp`：让 `dotnet run` 在打包模式下带身份启动

脚手架后必须手工修正：

1. `Package.appxmanifest` 的 `Identity/@Name` 与 `Identity/@Publisher` 改为本项目值（Publisher 必须与签名证书一致，否则无法安装）。
2. 删除模板自带的 `<systemai:Capability Name="systemAIModels"/>`——本项目不需要。
3. 保留 `<rescap:Capability Name="runFullTrust"/>`——全局热键、`SetDisplayConfig`、注册表访问都需要完全信任。
4. `Assets/` 里模板生成的占位图替换为本项目图标（现有 `with-windows.ico` / `png`）。
5. Release 默认 `PublishTrimmed=true`，与旧结论"WinUI 3 不支持裁剪"冲突，需实测确认后固定取值。

## 4. 架构

```
src/
  WithWindows.App/                 WinUI 3 可执行项目（打包，来自 winui-navview 模板）
    App.xaml(.cs)                  入口：单实例、配置、服务组装；静态 MainWindow 属性
    Shell/                         MainWindow（导航壳）、路由、标题栏、时钟
    Views/                         NotepadPage / HotkeysPage / SettingsPage
    ViewModels/                    每页一个 VM，只依赖 CommunityToolkit.Mvvm 与 Core
    Services/                      托盘宿主、热键宿主、启动任务、窗口状态
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
  WithWindows.UITests/             winui-unittest（[UITestMethod]）
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
- 草稿持久化是**既有行为**，继续保留：`%APPDATA%\WithWindows\notepad.txt`，编辑后用防抖计时器自动写入，打开时读回。
- 窗口尺寸/位置/字号沿用状态记忆。

### 5.3 快捷键页

- 列表布局，每行：工具名 / 说明 + `HotkeyInputBox` + 启用开关。
- 首版只有「切换投屏」一条；结构上由 `ToolRegistry` 生成，后续工具自动出现。
- 注册失败（如 1409 已被占用）在该行显示错误，不静默失败。
- **不预置默认热键**：首次运行时该行显示「未设置」，由用户点击录制框自行录入。
- 每行带一个「立即执行」按钮：用户尚未录入热键时功能仍可用，也便于验证。

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
| `Directory.Build.props` | 统一 TFM、`LangVersion`、`Nullable`、`Platform` |
| `Directory.Packages.props` | 中央包版本管理（Windows App SDK 组件版本必须整体对齐） |
| `global.json` | 钉死 SDK 版本，避免版本漂移 |
| `.editorconfig` | 缩进、换行、using 排序、命名偏好（AI 的格式依据） |
| `.gitattributes` | `*.cs text eol=crlf` |
| `nuget.config` | 固定包源 |

`WithWindows.App.csproj` 沿用模板结构（`UseWinUI`、`EnableMsixTooling`、`Package.appxmanifest`、`BuildTools.WinApp`），只调整 TFM、平台与版本号。

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

保留旧实现的优秀模式：写入用「同目录临时文件 + `File.Replace`」原子替换、旧内容留 `.bak`、主文件损坏时回退 `.bak`。

**数据位置仍用 `%APPDATA%\WithWindows\`**，不用 `ApplicationData.Current.LocalFolder`。理由：

- 应用是 full-trust 打包，写入 `%APPDATA%` 不被虚拟化，路径稳定。
- 配置面向用户（`displayMode.modes` 可手改），放可预期路径便于排查。
- 卸载重装后配置与日志保留。
- 同一分发方的 Codex 桌面端（同样是 MSIX 打包）也把数据写在包容器之外的 `%LOCALAPPDATA%\OpenAI\Codex\`，有先例。

若后续要改为包容器内的 `LocalState`，只需替换 `ConfigStore` 的根路径，其余不变。

### 6.4 热键

- 语法与校验规则保留：修饰键 `Ctrl/Alt/Shift/Win`、F1–F24、字母数字；字母数字必须带修饰键；`MOD_NOREPEAT`。
- **不设默认热键**：新装后不注册任何全局热键。理由：默认值必然与部分用户的既有快捷键冲突，而冲突表现为"按了没反应"，用户既不知道原因也不知道去哪儿改。首次运行由用户在快捷键页自行录入。
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
- 打包不影响 P/Invoke 可用性（`runFullTrust` 下与普通桌面进程一致）。

### 6.7 生命周期（打包后改写）

**单实例**：改用 Windows App SDK 激活模型，而非命名 Mutex。

```csharp
var instance = AppInstance.FindOrRegisterForKey("WithWindows");
if (!instance.IsCurrent)
    await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
```

好处：第二次启动会把激活事件重定向到已有实例，可顺带把主窗口唤到前台；这是打包应用的官方做法。

**开机自启**：改用 `windows.startupTask` 清单扩展 + `StartupTask.GetAsync(taskId)`，不再写 `HKCU\...\Run`。打包应用的注册表写入被虚拟化，写 Run 键不是受支持路径。

```xml
<desktop:Extension Category="windows.startupTask"
                   Executable="WithWindows.App.exe"
                   EntryPoint="Windows.FullTrustApplication">
  <desktop:StartupTask TaskId="WithWindowsStartup" Enabled="false"
                       DisplayName="With Windows" />
</desktop:Extension>
```

必须如实反映的两条平台限制：

- 用户至少手动启动过一次应用，启动任务才会注册。
- 用户在任务管理器中禁用后，**程序无法再自行启用**；设置页开关必须显示真实状态（`StartupTask.State`），不能只显示本地布尔值。

**日志**：`%APPDATA%\WithWindows\log.txt`，1 MB 轮转为 `log.1.txt`；`Logger` 由 App 持有整个进程生命周期，禁止用局部 `using`。

**运行数据**：全部位于 `%APPDATA%\WithWindows\`，安装目录保持干净（仅 Assets 资源）。

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
2. dotnet run                → 必须真的启动起来（打包模式走 winapp 身份）
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
| `docs/agents/interop-and-platform.md` | CsWin32 规则、隐藏消息窗口的原因、显示拓扑、托盘、startupTask |
| `docs/agents/known-pitfalls.md` | 本项目积累的坑，持续追加 |

理由：硬约束必须每轮可见，领域细节只在相关改动时读取；避免把上万字的规范全文塞进每轮上下文。

### 7.4 环境前置

```
winget install --id Microsoft.WinAppCli
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
```

外加：开启开发者模式（旁加载调试身份所需）、安装 `winui-app` skill 与微软 `winui` 插件、配置 Microsoft Learn MCP。模板包当前为 `0.0.7-alpha`（脚手架可用，日常开发不依赖它）。

## 8. 复用与重写清单

判定原则：**逻辑内核迁移，代码在新结构下重写；不复用旧 XAML。** 迁移时必须逐条复核下列硬知识，避免重新踩坑。

| 旧模块 | 处理 | 必须保留的细节 |
|---|---|---|
| `Core/Hotkey.cs`、`HotkeyParser`、`HotkeyFormatter` | 迁移 | 修饰键校验、F1–F24 编解码、字母数字必须带修饰键、`MOD_NOREPEAT` |
| `Core/HotkeyManager` | 迁移并重写封装 | 隐藏消息窗口方案、`WndProc` 委托自持防 GC、失败返回错误码而非抛异常 |
| `Core/SingleInstance` | **重写**为 `AppInstance` | 见 6.7 |
| `Core/AutoStart` | **重写**为 startupTask | 见 6.7 |
| `Config/ConfigStore` | 重写 schema，保留模式 | 原子替换 + `.bak`、损坏回退、自愈式清理未知绑定 |
| `Actions/DisplayModeAction`、`DisplayTopology` | 迁移 | `QDC_DATABASE_CURRENT` 权威拓扑、`Changed=false` 语义、toggle 纯函数 |
| `Interop/DisplayConfigTypes` | 迁移 | 结构体字节布局按 `wingdi.h` |
| `Interop/NativeMethods` | 重写为 CsWin32 | 常量与语义保留；不再手写 `DllImport` |
| `Logger` | 迁移 | 1 MB 轮转、被占用时降级为丢弃 |
| `MainWindow`、`ToggleWindow`、`NotepadWindow` XAML | 重写 | 无复用价值 |
| `Controls/HotkeyInputBox` | 重写 | 录制交互的行为契约（聚焦后捕获、Esc 取消、清除） |

## 9. 测试策略

| 层 | 项目 | 覆盖 |
|---|---|---|
| 逻辑 | `WithWindows.Core.Tests` | 热键解析/格式化、配置读写与容错、toggle 目标选择、`Changed` 语义、动作分发 |
| UI | `WithWindows.UITests` | 实例化 XAML 的测试必须用 `[UITestMethod]`；页面导航、控件存在性 |
| 探索 | `winapp ui inspect / screenshot` | 每轮改动的自检，不是回归测试 |

平台相关只做只读断言，禁止在测试中改变真实显示状态。

## 10. 打包与分发

官方把这件事拆成三个**相互独立**的决策：打包模型、分发渠道、Windows App SDK 运行时部署方式。本项目三项均已选定。

### 10.1 打包模型：MSIX 打包

官方文档对新建应用的原话是 "Building a new WinUI 3 app? **You're already packaged by default**"，即打包是默认路径。收益：

- **包身份**：通知、`startupTask`、文件类型关联、上下文菜单扩展等 Windows 扩展点都依赖它。
- 干净的安装与卸载、可靠的更新、支持商店与企业管理（Intune / Configuration Manager）。

代价：**MSIX 无法做成单文件 exe**（`PublishSingleFile` 只支持"非打包 + 自包含"组合），所以旧的两个单文件产物下线，改为 `.msix`（多架构时 `.msixbundle`）。

对比另外两种模型：`packaging with external location`（稀疏包）是给"已有自己的安装器、只想补一个包身份"的应用用的；`unpackaged` 官方定位是"内部工具或开发者工具，构建部署最简单"。本项目选打包是因为需要 `startupTask` 且看重干净安装。

### 10.2 运行时部署：框架依赖

Windows App SDK 的**默认**模式，本项目沿用：

- 体积最小，且**打包应用会自动安装所需的运行时与框架包**，用户侧无需手动安装。
- 运行时随服务自动更新，不必随应用重新发布。

自包含模式的收益是"版本可控、与其它应用隔离、可 xcopy 部署"，代价是必须自己承担运行时更新；对个人工具没有必要。

### 10.3 分发渠道：GitHub Releases + 自签名证书

目标形态：

1. CI 构建并签名 x64 的 `.msix`，同时生成 `.appinstaller`。
2. 两者作为资产上传到 GitHub Release。
3. 用户下载 `.appinstaller` 双击安装；此后启动时自动检查更新。

**必须先说清的代价**：MSIX 的"下载即装"以证书受信任为前提。官方对自签名证书的定位是"仅开发测试，或企业有统一的证书信任管理"——**公开分发时它会直接阻止安装**，用户须先把证书装入「受信任人」。因此"任何人下载都能装"在自签名下**不成立**；成立只走两条路：商店（免费重签）或购买 CA 证书。

| 方案 | 成本 | 下载方摩擦 | 自动更新 |
|---|---|---|---|
| **自签名 + GitHub（本方案）** | 0 | 需先信任证书（每台机器一次） | ✅ |
| 未打包单文件 exe | 0 | SmartScreen 点「仍要运行」 | ❌ 需自建 |
| OV 证书 + GitHub | $150–300/年 + 硬件令牌 | 可直接安装（首次仍可能有 SmartScreen） | ✅ |
| Microsoft Store | 0 | 零摩擦 | ✅ |

微软推荐的**非商店**签名服务是 Azure Artifact Signing（原 Trusted Signing，约 $9.99/月），但个人开发者目前**仅限美国和加拿大**，国内不可用。

自签名流程由 `winapp cert generate` / `cert install` / `sign` 覆盖；证书与私钥不入库，CI 通过 Secret 注入。

其他要点：

- `ms-appinstaller` 协议自 2023-12 起默认禁用，浏览器一键安装已不可用；用户须下载 `.appinstaller` 后双击。
- `.appinstaller` 内指向 MSIX 的地址用 GitHub 的 `releases/latest/download/...` 固定路径，避免每次发版改链接（相对路径解析需实测）。
- 自动更新由 `.appinstaller` 的 `UpdateSettings` 控制（`OnLaunch` / `HoursBetweenUpdateChecks`）。

#### 10.3.1 用户侧安装步骤（README 需照此写）

证书**内嵌在 MSIX 的签名块里**，Windows 不会自动信任它，必须由用户显式导入。三条路径，按推荐顺序：

**方式一（推荐）：随包分发公钥证书，三步**

1. 双击 `WithWindows.cer` → 安装证书 → 存储位置选**本地计算机**（需管理员提权）。
2. 存储位置选**受信任人**（Trusted People）。
3. 双击 `WithWindows.appinstaller` 完成安装。

**方式二：从 MSIX 签名里就地安装（不额外下载证书）**

右键 `WithWindows.msix` → 属性 → 数字签名 → 选中证书 → 详细信息 → 查看证书 → 安装证书 → **本地计算机**（需管理员提权）→ 受信任的根证书颁发机构。这是官方旁加载文档给出的步骤。

**方式三：PowerShell 一步到位（管理员会话）**

```powershell
Import-Certificate -FilePath .\WithWindows.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage .\WithWindows.msix
```

证书管理规则（硬性）：

- **只分发公钥证书 `.cer`，绝不把带私钥的 `.pfx` 放进 Release**——私钥泄露等于别人可以冒充你签名。
- 导入目标必须是**本地计算机**（不是当前用户）的**受信任人**存储；官方签名文档指定的就是 Trusted People。
- `Package.appxmanifest` 的 `Identity/@Publisher` 必须**逐字等于**证书的 `Subject`，否则签名与清单对不上，安装后无法启动。
- 证书有效期设长（自签名默认仅 1 年）并**始终复用同一张证书**：更换证书会让所有已安装用户重新信任一次。

### 10.4 版本号同步

`Package.appxmanifest` 的 `Identity/@Version` 必须是四段式（`1.0.0.0`），需与 `CHANGELOG.md` 的语义化版本建立映射（如 `0.5.0` → `0.5.0.0`）。发布流程必须同时更新两处。

### 10.5 CI 调整

`release.yml` 从"发布单文件 exe + zip + SHA256"改为"构建并签名 MSIX + 生成 `.appinstaller` + 上传到 Release"，证书由仓库 Secret 提供。这是独立工作量，排在最后。

### 10.6 注意事项

- 调试与旁加载需要开启开发者模式。
- 常驻实例同样会锁住输出文件，构建前先关闭应用。
- 每次改动后复查 `README.md` 与 `CHANGELOG.md`；README 的系统要求与安装说明整段需要重写。

## 11. 分阶段计划

| 阶段 | 内容 | 验收 |
|---|---|---|
| P0 规范与环境 | 装 `winapp` CLI、模板包、skill 与插件、Learn MCP、开发者模式；建立 6 个规范文件；写好分层 `AGENTS.md` | `winapp --version` 可用；分册可从根文件索引到 |
| P1 工程骨架 | 用 `winui-navview` 生成 App 项目；建 Core 与两个测试工程；修正 manifest、TFM、平台、图标；`dotnet run` 启动；`winapp ui inspect/screenshot` 拍到导航壳 | 截图可见导航壳 |
| P2 Core 迁移 | 按第 8 节迁移 Core 与互操作；CsWin32 生效；测试迁到 `Core.Tests` | Core 测试全绿且不拉起 WinUI |
| P3 页面 | 记事本页、快捷键页、设置页 | 控件在无障碍树中可见；截图符合 Win11 风格 |
| P4 工具抽象 | `ITool` + `ToolRegistry` + 配置 v1 落地 | 新增工具仅需注册，主窗口不改 |
| P5 打磨与打包 | 深浅色、标题栏、自适应、图标；框架依赖 MSIX 打包与签名；CI 改为产出 MSIX；README 重写 | 产出的 MSIX 能在干净机器上安装并常驻 |

旧目录（`src/WithWindows.UI`、旧测试工程）在 `dev` 分支于 P2 完成后删除；主检出的 `main` 始终保留旧实现供查阅。

## 12. 风险与未决

1. **CsWin32 与窗口类注册**：`RegisterClassEx` 的 `WNDCLASSEX` 与 `WndProc` 委托需要函数指针，需验证 CsWin32 生成结果；若无干净写法，保留这一处最小手写互操作并注明原因。
2. **裁剪**：模板 Release 默认 `PublishTrimmed=true`，与旧结论冲突，需实测后固定取值。
3. **签名与旁加载摩擦**：自签名证书要求用户信任证书，否则无法安装；这是打包路线相对单文件 exe 的主要代价。
4. **startupTask 限制**：用户在任务管理器禁用后程序无法重新启用，设置页必须显示真实状态。
5. **首次运行无热键**：不预置默认值意味着用户必须自己录入才能用上全局热键；快捷键页需要明确的空状态引导，并保留「立即执行」入口。
6. **`winapp` CLI 处于公共预览**：命令语法可能变动，执行前用 `winapp --help` 核对。
7. **托盘与主窗口隐藏**：关闭主窗口仅隐藏时，需验证热键与托盘仍工作，且再次唤出时焦点正常。
8. **第三方示例数字**（如"装 skill 后 30 分钟完成""token 降 70%"）来自他人自述，不作为验收依据。

## 13. 完成定义（DoD）

- [ ] `dotnet build -p:Platform=x64` 零错误
- [ ] `dotnet test -p:Platform=x64` 全绿
- [ ] `dotnet run` 能启动（打包身份），且已实际启动验证
- [ ] `winapp ui inspect` 能看到新增控件
- [ ] `winapp ui screenshot` 目视无错乱，浅色与深色各一张
- [ ] 新增可交互控件均有 `AutomationProperties.AutomationId`
- [ ] 新增逻辑有对应单元测试
- [ ] `README.md` 与 `CHANGELOG.md` 已同步
