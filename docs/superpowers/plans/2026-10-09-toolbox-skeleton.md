# With Windows 工具箱骨架（P0+P1）Implementation Plan

> **⚠️ 历史记录**：本文是 2026-10-09 的实现计划，只描述当时怎么做；其中的第三方依赖白名单、平台版本与目录结构等已被后续改动取代（例如 `CommunityToolkit.Mvvm` 已移除、目标平台为 Win11 22H2），以 `AGENTS.md` 与代码为准。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `dev` 分支建立 WinUI 3 工具箱的可运行骨架：仓库规范文件齐全、`AGENTS.md` 分层可用、`WithWindows.Core` 类库与测试工程可编译、`WithWindows.App` 以未打包方式启动并显示带 Mica 的 `NavigationView` 导航壳。

**Architecture:** 三层分离——`WithWindows.Core` 是纯逻辑类库（禁止引用任何 `Microsoft.UI.Xaml` 类型），`WithWindows.App` 是未打包的 WinUI 3 可执行项目，`WithWindows.Core.Tests` 只引用 Core 以便快速跑纯逻辑测试。导航壳沿用官方模板的 `NavigationView + Frame + TitleBar + MicaBackdrop` 结构。

**Tech Stack:** C# 14 / .NET 10 / WinUI 3（Windows App SDK）/ CommunityToolkit.Mvvm / xunit / Inno Setup（本计划不涉及打包）

**Spec:** `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`

## Global Constraints

- 目标框架固定 `net10.0-windows10.0.22621.0`；平台固定 x64（`-p:Platform=x64`），RID 固定 `win-x64`
- 未打包：`<WindowsPackageType>None</WindowsPackageType>`，不使用 `Package.appxmanifest`
- `PublishTrimmed` 必须为 `false`（WinUI 3 不支持裁剪）
- `WithWindows.Core` 中禁止出现任何 `Microsoft.UI.Xaml` 类型
- 运行时数据全部位于 `%APPDATA%\WithWindows\`，程序目录只放程序与资源
- 注释与用户可见文案使用中文；中文用中文标点；中英文之间加空格
- 每个可交互控件必须有 `AutomationProperties.AutomationId`
- 提交信息用 Conventional Commits，中文描述，不加任何 AI 署名

---

### Task 1: 仓库级规范文件

**Files:**
- Create: `.editorconfig`
- Create: `.gitattributes`
- Create: `global.json`
- Create: `nuget.config`

**Interfaces:**
- Consumes: 无
- Produces: 仓库级格式与 SDK 版本约束，后续所有任务隐式依赖

- [ ] **Step 1: 写 `.gitattributes`**

```
* text=auto

*.cs text eol=crlf
*.xaml text eol=crlf
*.csproj text eol=crlf
*.props text eol=crlf
*.targets text eol=crlf
*.sln text eol=crlf
*.slnx text eol=crlf
*.md text eol=crlf

*.png binary
*.ico binary
*.pfx binary
```

- [ ] **Step 2: 写 `global.json`**

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 3: 写 `nuget.config`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

- [ ] **Step 4: 写 `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
trim_trailing_whitespace = true

[*.{cs,xaml}]
indent_style = space
indent_size = 4

[*.{json,yml,yaml,props,targets,csproj,iss}]
indent_style = space
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.cs]
dotnet_sort_system_directives_first = true
dotnet_style_qualification_for_field = false:suggestion
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_style_var_for_built_in_types = false:suggestion
dotnet_diagnostic.CA1416.severity = none
```

说明：最后一条关闭「平台兼容性」分析器（`CA1416`）——本项目只面向 Windows，每个 API 都会报该警告，属于噪声。

- [ ] **Step 5: 验证文件生效**

Run:
```powershell
git check-attr text eol -- src/WithWindows.UI/App.xaml.cs
dotnet --version
```
Expected: 第一条输出 `... text: auto` 与 `... eol: crlf`；第二条输出 `10.0.401`。

- [ ] **Step 6: 验证现有构建未被破坏**

Run:
```powershell
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```
Expected: 退出码 0，无错误输出。

- [ ] **Step 7: Commit**

```bash
git add .editorconfig .gitattributes global.json nuget.config
git commit -m "chore: 新增仓库级格式与 SDK 约束文件"
```

---

### Task 2: MSBuild 集中属性与中央包版本

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`

**Interfaces:**
- Consumes: Task 1 的 `global.json`（SDK 版本）
- Produces: `Directory.Build.props` 提供 `LangVersion` / `Nullable` / `ImplicitUsings` / `PublishTrimmed=false`；`Directory.Packages.props` 提供集中包版本，后续工程的 `PackageReference` 不再写 `Version`

**注意事项**：仓库根目录的 `Directory.Build.props` 会同时作用于**旧项目**（`src/WithWindows.UI`，P2 结束时删除）。因此这里**不得**设置 `TargetFramework` 或 `Platform`——旧项目 TFM 是 `19041`、新项目是 `22621`，写死会破坏旧项目。

- [ ] **Step 1: 写 `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- WinUI 3 不支持裁剪；集中关闭，避免模板默认的 Release 裁剪误开 -->
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: 验证旧项目仍能构建（关键回归检查）**

Run:
```powershell
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
dotnet test tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 两条都退出码 0，测试全绿。若失败，说明该文件引入了冲突，需回退并改为按目录放置。

- [ ] **Step 3: Commit（先只提交 Build.props）**

```bash
git add Directory.Build.props
git commit -m "chore: 集中 MSBuild 通用属性"
```

- [ ] **Step 4: 暂缓 `Directory.Packages.props`**

决策：`Directory.Packages.props`（中央包版本）**推迟到 Task 6 创建新工程时**再加。原因：一旦启用集中版本管理，旧项目 `src/WithWindows.UI` 现有的带 `Version` 的 `PackageReference` 会立刻报 NU1008，而旧项目要到 P2 才删除。现在加入会破坏旧项目构建。

本步骤不产生文件改动，仅记录该决策。

---

### Task 3: `AGENTS.md` 分层重构

**Files:**
- Modify: `AGENTS.md`（整篇重写）
- Create: `docs/agents/winui3-hard-rules.md`
- Create: `docs/agents/development-workflow.md`
- Create: `docs/agents/ui-conventions.md`
- Create: `docs/agents/interop-and-platform.md`
- Create: `docs/agents/known-pitfalls.md`

**Interfaces:**
- Consumes: 无
- Produces: 根 `AGENTS.md` 作为每轮必读的入口（≤100 行），五个分册按需读取；文件名被根文件索引引用

**素材来源**：现有 `AGENTS.md` 的「文案规范」「常用命令」「架构」「关键机制」「已知坑」「提交规范」「测试约定」各段，以及设计文档第 3、5、6、7 节。

- [ ] **Step 1: 写根 `AGENTS.md`**

必须包含且仅包含以下七段，总行数 ≤100：

1. **一句话定位**：Windows 常驻托盘的工具箱应用；配置驱动的全局热键 → 动作框架；`NavigationView` 导航壳，每个工具一页。
2. **技术栈锁定**：.NET 10 + WinUI 3（Windows App SDK）；`net10.0-windows10.0.22621.0`；仅 x64；未打包（`WindowsPackageType=None`）；C# latest + nullable + implicit usings；测试 xunit。
3. **硬性约束**：构建/测试必须带 `-p:Platform=x64`；`PublishTrimmed` 固定 false；`Core` 禁止引用 `Microsoft.UI.Xaml`；运行时数据全在 `%APPDATA%\WithWindows\`；第三方依赖白名单（WindowsAppSDK、WinUIEx、CommunityToolkit.Mvvm、Microsoft.Windows.CsWin32），白名单外先问。
4. **文案规范**：中文用中文标点；中英文/中文与数字之间加空格；适用于注释、README、CHANGELOG、日志、提交信息。
5. **常用命令**：build / test / run / 冒烟 / 发布（发布命令先写占位并在 P5 补全）。
6. **目录地图**：`src/WithWindows.App`、`src/WithWindows.Core`、`tests/WithWindows.Core.Tests`、`installer/`、`docs/agents/`、`docs/superpowers/`。
7. **分册索引 + 提交规范 + DoD**：索引到五个分册；Conventional Commits、中文、不加 AI 署名；DoD 六条（build 零错误、test 全绿、能启动、`winapp ui inspect` 看到控件、截图无错乱、README/CHANGELOG 已同步）。

同时保留两条既有约定：**不要用局部 `using` 持有 `Logger`/单实例互斥体**；**每次改动后复查 README 与 CHANGELOG**。

把原「升级/改动后不要自动启动应用」改为：**允许自动启动应用做验证**（`winui-app` skill 的验证闭环依赖它），验证完关闭即可。

- [ ] **Step 2: 写 `docs/agents/winui3-hard-rules.md`**

内容：编译通过但运行失败的坑，逐条给「错误写法 → 正确写法」：

- `ContentDialog` 必须设 `XamlRoot`，否则运行期抛 `InvalidOperationException`
- 文件选择器必须先 `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)`
- 禁止 `Windows.UI.Xaml.*` 命名空间，一律 `Microsoft.UI.Xaml.*`
- 禁止 `CoreDispatcher`，用 `DispatcherQueue.TryEnqueue`
- 禁止 `MessageDialog`（`Windows.UI.Popups`），用 `ContentDialog`
- 禁止 `Window.Current`，用 `App.MainWindow` 静态属性
- 禁止 `ApplicationView` / `CoreWindow` / `*GetForCurrentView()`，用 `AppWindow` 与 `XamlRoot.RasterizationScale`
- 禁止 `Windows.UI.Colors`，用 `Microsoft.UI.Colors`
- 禁止 `ConfigureAwait(false)` 出现在 ViewModel（异常会被静默吞掉）
- 用 `AutomationProperties.AutomationId` 标记可交互控件
- 附 UWP → WinUI 3 的命名空间与窗口管理映射表

- [ ] **Step 3: 写 `docs/agents/development-workflow.md`**

内容：六步闭环（build → run → `winapp ui inspect` → `winapp ui screenshot` → 自检 → 才允许声称完成）；`winapp ui` 六条命令与用法；AutomationId 要求；构建/启动连续 3 次失败则停止并报告（卡在哪、完整错误、已排除项、建议）；DoD 清单；`winapp` 处于公共预览、执行前 `winapp --help` 核对。

- [ ] **Step 4: 写 `docs/agents/ui-conventions.md`**

内容：`NavigationView + Frame` 导航壳结构；`TitleBar` 控件 + `MicaBackdrop`；深浅色双主题，颜色/字体/间距一律 `ThemeResource`；控件选择顺序（WinUI 内置 → CommunityToolkit.WinUI → 自写）；分组命令优先 `CommandBar`；响应式三档宽度；像素级视觉细节由人决定，AI 标为「待人工确认」。

- [ ] **Step 5: 写 `docs/agents/interop-and-platform.md`**

内容：P/Invoke 一律走 CsWin32（`NativeMethods.txt` 声明），禁止手写 `DllImport`，禁止改生成代码；**全局热键用独立隐藏消息窗口接收，禁止子类化 WinUI 窗口**（曾两次致原生崩溃）；`DISPLAYCONFIG_*` 结构体布局必须与 `wingdi.h` 一致；显示拓扑用 `QDC_DATABASE_CURRENT` 判定；托盘用 WinUIEx `TrayIcon` 且必须持有引用防 GC；开机自启写 `HKCU\...\Run` 并给路径加引号。

- [ ] **Step 6: 写 `docs/agents/known-pitfalls.md`**

内容：迁移现有 `AGENTS.md` 的「已知坑」全部条目，并补三条新的：

- 单文件发布时 WASDK 强制要求 `EnableMsixTooling=true`（按条件开启）
- 窗口最小尺寸用 `OverlappedPresenter.PreferredMinimumWidth/Height`，`AppWindow.PreferredMinimum*` 不存在
- 窗口位置 `(0, 0)` 是合法坐标，`null` 才表示"未保存"，不能用 0 当哨兵

- [ ] **Step 7: 验证结构**

Run:
```powershell
(Get-Content AGENTS.md).Count
foreach ($f in 'winui3-hard-rules','development-workflow','ui-conventions','interop-and-platform','known-pitfalls') {
  if (-not (Test-Path "docs/agents/$f.md")) { throw "缺失: $f" }
}
Select-String -Path AGENTS.md -Pattern 'docs/agents/' | Measure-Object | Select-Object -ExpandProperty Count
```
Expected: 首行 ≤100；第二条无异常；第三条 ≥5（根文件索引到全部分册）。

- [ ] **Step 8: Commit**

```bash
git add AGENTS.md docs/agents
git commit -m "docs: 拆分 AGENTS 为入口加分册结构"
```

---

### Task 4: `WithWindows.Core` 类库骨架

**Files:**
- Create: `src/WithWindows.Core/WithWindows.Core.csproj`
- Create: `src/WithWindows.Core/Diagnostics/AppPaths.cs`

**Interfaces:**
- Consumes: Task 2 的 `Directory.Build.props`
- Produces:
  - 程序集 `WithWindows.Core`，命名空间根 `WithWindows.Core`
  - `public static class AppPaths` 带 `public static string DataRoot { get; }`（返回 `%APPDATA%\WithWindows`）与 `public static string ConfigPath { get; }`（返回 `DataRoot\config.json`）

- [ ] **Step 1: 写 `src/WithWindows.Core/WithWindows.Core.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <RootNamespace>WithWindows.Core</RootNamespace>
    <Platforms>x64</Platforms>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

注意：**没有** `UseWinUI`、**没有** 任何 UI 包引用——这是分层硬规则的落地。

- [ ] **Step 2: 写 `src/WithWindows.Core/Diagnostics/AppPaths.cs`**

```csharp
namespace WithWindows.Core.Diagnostics;

/// <summary>
/// 应用数据路径。运行时数据全部位于 %APPDATA%\WithWindows\，程序目录只放程序与资源。
/// </summary>
public static class AppPaths
{
    /// <summary>数据根目录：%APPDATA%\WithWindows。</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WithWindows");

    /// <summary>配置文件路径。</summary>
    public static string ConfigPath { get; } = Path.Combine(DataRoot, "config.json");

    /// <summary>日志文件路径。</summary>
    public static string LogPath { get; } = Path.Combine(DataRoot, "log.txt");

    /// <summary>记事本草稿路径。</summary>
    public static string NotepadDraftPath { get; } = Path.Combine(DataRoot, "notepad.txt");
}
```

- [ ] **Step 3: 验证编译**

Run:
```powershell
dotnet build src/WithWindows.Core/WithWindows.Core.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```
Expected: 退出码 0。

- [ ] **Step 4: Commit**

```bash
git add src/WithWindows.Core
git commit -m "feat(核心): 新增 Core 类库与应用数据路径"
```

---

### Task 5: `WithWindows.Core.Tests` 测试工程

**Files:**
- Create: `tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj`
- Create: `tests/WithWindows.Core.Tests/AppPathsTests.cs`

**Interfaces:**
- Consumes: Task 4 的 `AppPaths`
- Produces: 测试工程 `WithWindows.Core.Tests`，**只引用 Core**（不引用 App），这是相对旧测试工程的关键改进

- [ ] **Step 1: 写测试工程文件**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\WithWindows.Core\WithWindows.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 写失败测试 `AppPathsTests.cs`**

```csharp
using WithWindows.Core.Diagnostics;

namespace WithWindows.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void DataRoot_位于_APPDATA_下的_WithWindows()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(appData, AppPaths.DataRoot, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("WithWindows", AppPaths.DataRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 各路径都位于_DataRoot_之下()
    {
        Assert.StartsWith(AppPaths.DataRoot, AppPaths.ConfigPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.DataRoot, AppPaths.LogPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.DataRoot, AppPaths.NotepadDraftPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 各路径均非空且互不相同()
    {
        var all = new[] { AppPaths.DataRoot, AppPaths.ConfigPath, AppPaths.LogPath, AppPaths.NotepadDraftPath };

        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        Assert.Equal(all.Length, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
```

- [ ] **Step 3: 运行测试确认失败（红）**

先临时把 `AppPaths.cs` 里的 `ConfigPath` 改成 `Path.Combine(DataRoot, "wrong.json")`，然后：

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 失败，报 `各路径均非空且互不相同` 不通过（说明测试确实在校验行为，而不是空跑）。验证后**改回** `config.json`。

说明：这一步是红绿验证，确保测试有效。若改动后测试仍通过，说明测试写错了，必须先修正测试。

- [ ] **Step 4: 运行测试确认通过（绿）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 3 个测试全部通过。

- [ ] **Step 5: 确认测试工程未引入 UI 依赖**

Run:
```powershell
dotnet list tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj reference
```
Expected: 只列出 `WithWindows.Core.csproj`，不包含 `WithWindows.App` 或 `WithWindows.UI`。

- [ ] **Step 6: Commit**

```bash
git add tests/WithWindows.Core.Tests
git commit -m "test(核心): 新增 Core 测试工程与路径测试"
```

---

### Task 6: `WithWindows.App` 未打包工程（最小可运行）

**Files:**
- Create: `src/WithWindows.App/WithWindows.App.csproj`
- Create: `src/WithWindows.App/app.manifest`
- Create: `src/WithWindows.App/App.xaml`
- Create: `src/WithWindows.App/App.xaml.cs`
- Create: `src/WithWindows.App/MainWindow.xaml`
- Create: `src/WithWindows.App/MainWindow.xaml.cs`
- Create: `Directory.Packages.props`（本任务启用中央包版本管理）

**Interfaces:**
- Consumes: Task 4 的 `WithWindows.Core`；Task 2 的 `Directory.Build.props`
- Produces:
  - 可执行的未打包 WinUI 3 应用，程序集名 `WithWindows`，根命名空间 `WithWindows`
  - `public partial class App : Application`，带 `public static Window? MainWindow { get; private set; }`
  - `public sealed partial class MainWindow : Window`

**本任务的取舍**：先用**伞包** `Microsoft.WindowsAppSDK` 跑通，不用旧项目的组件包拆分方案。理由：组件包方案要求各组件版本整体对齐（旧项目踩过），在骨架阶段引入会放大失败面；体积优化留到 P5，届时用实测数据对比再决定。这一条要在提交信息或注释中写明，避免后人以为遗漏。

- [ ] **Step 1: 写 `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>false</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.4654" />
    <PackageVersion Include="WinUIEx" Version="2.9.2" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="Microsoft.Windows.CsWin32" Version="0.3.183" />
  </ItemGroup>
</Project>
```

**副作用警告**：启用集中版本管理后，旧项目 `src/WithWindows.UI` 与旧测试工程的 `PackageReference`（带显式 `Version`）会报 **NU1008**。因此本步骤必须**同时**给旧项目加退出标记，做法是在旧两个 csproj 的 `<PropertyGroup>` 中加：

```xml
<!-- 旧项目待 P2 删除，暂时退出中央包版本管理 -->
<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
```

受影响的文件：`src/WithWindows.UI/WithWindows.UI.csproj`、`tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj`。

版本号若在还原时报「找不到包」，用 `dotnet package search <包名> --take 1` 查当期稳定版并更新本文件，不要凭记忆写。

- [ ] **Step 2: 验证旧项目仍能构建**

Run:
```powershell
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
dotnet test tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 都退出码 0。若报 NU1008，说明 Step 1 的 `ManagePackageVersionsCentrally=false` 没加到旧项目。

- [ ] **Step 3: 写 `src/WithWindows.App/WithWindows.App.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22621.0</TargetPlatformMinVersion>
    <RootNamespace>WithWindows</RootNamespace>
    <AssemblyName>WithWindows</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWinUI>true</UseWinUI>
    <!-- 未打包：不使用 MSIX，无包身份 -->
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <PublishSingleFile>false</PublishSingleFile>
    <!-- 单文件发布时 WASDK 强制要求开启 MSIX 工具链（已知坑） -->
    <EnableMsixTooling Condition="'$(PublishSingleFile)' == 'true'">true</EnableMsixTooling>
    <EnableMsixTooling Condition="'$(PublishSingleFile)' != 'true'">false</EnableMsixTooling>
    <Version>0.5.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" />
    <PackageReference Include="WinUIEx" />
    <PackageReference Include="CommunityToolkit.Mvvm" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\WithWindows.Core\WithWindows.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

注意 `PackageReference` 都**不写 `Version`**——由 `Directory.Packages.props` 提供。所有 `.xaml` 由 WinUI SDK 自动纳入，无需显式 `Page` 项。

- [ ] **Step 4: 写 `src/WithWindows.App/app.manifest`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="WithWindows.app" />

  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- WinUI 3 自定义标题栏在未打包模式下依赖该声明 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 5: 写 `App.xaml` 与 `App.xaml.cs`**

`App.xaml`：

```xml
<Application
    x:Class="WithWindows.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`App.xaml.cs`：

```csharp
using Microsoft.UI.Xaml;

namespace WithWindows;

/// <summary>
/// 应用入口。持有主窗口引用，供全局访问（替代 WinUI 3 中不存在的 Window.Current）。
/// </summary>
public partial class App : Application
{
    /// <summary>主窗口引用；关闭时只是隐藏，不置空。</summary>
    public static Window? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
```

- [ ] **Step 6: 写最小 `MainWindow.xaml` 与 `MainWindow.xaml.cs`**

`MainWindow.xaml`：

```xml
<Window
    x:Class="WithWindows.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="With Windows">

    <Window.SystemBackdrop>
        <MicaBackdrop />
    </Window.SystemBackdrop>

    <Grid>
        <TextBlock
            AutomationProperties.AutomationId="ShellPlaceholder"
            Text="With Windows"
            HorizontalAlignment="Center"
            VerticalAlignment="Center" />
    </Grid>
</Window>
```

`MainWindow.xaml.cs`：

```csharp
using Microsoft.UI.Xaml;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
```

- [ ] **Step 7: 创建 `Assets` 目录与图标**

从旧项目复制图标（本任务不重新设计图标）：

```powershell
New-Item -ItemType Directory -Force -Path src/WithWindows.App/Assets | Out-Null
Copy-Item src/WithWindows.UI/Assets/with-windows.ico src/WithWindows.App/Assets/
Copy-Item src/WithWindows.UI/Assets/with-windows.png src/WithWindows.App/Assets/
```

- [ ] **Step 8: 构建**

Run:
```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```
Expected: 退出码 0。若报 XAML 编译错误（MSB3073 / XamlCompiler.exe），先检查 `UseWinUI` 是否为 `true`、`WindowsPackageType` 是否为 `None`，不要自创结构。

- [ ] **Step 9: 启动验证**

Run:
```powershell
dotnet run --project src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```
Expected: 出现一个标题为「With Windows」的窗口，背景为 Mica 材质。确认后关闭窗口。

- [ ] **Step 10: Commit**

```bash
git add Directory.Packages.props src/WithWindows.App src/WithWindows.UI/WithWindows.UI.csproj tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj
git commit -m "feat(应用): 新增未打包 WinUI 3 应用骨架"
```

---

### Task 7: 导航壳与三个页面

**Files:**
- Modify: `src/WithWindows.App/MainWindow.xaml`
- Modify: `src/WithWindows.App/MainWindow.xaml.cs`
- Create: `src/WithWindows.App/Views/NotepadPage.xaml`
- Create: `src/WithWindows.App/Views/NotepadPage.xaml.cs`
- Create: `src/WithWindows.App/Views/HotkeysPage.xaml`
- Create: `src/WithWindows.App/Views/HotkeysPage.xaml.cs`
- Create: `src/WithWindows.App/Views/SettingsPage.xaml`
- Create: `src/WithWindows.App/Views/SettingsPage.xaml.cs`

**Interfaces:**
- Consumes: Task 6 的 `MainWindow`
- Produces:
  - `WithWindows.Views.NotepadPage` / `HotkeysPage` / `SettingsPage` 三个 `Page` 类型
  - `MainWindow` 内 `NavView`（`NavigationView`）与 `NavFrame`（`Frame`），通过 `NavigationViewItem.Tag` 映射到页面类型
  - 每个页面暴露 `public static string RouteTag { get; }`（值分别为 `notepad` / `hotkeys` / `settings`）

- [ ] **Step 1: 写 `MainWindow.xaml`（导航壳）**

```xml
<Window
    x:Class="WithWindows.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Microsoft.UI.Xaml.Controls"
    Title="With Windows">

    <Window.SystemBackdrop>
        <MicaBackdrop />
    </Window.SystemBackdrop>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="48" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <TitleBar
            x:Name="AppTitleBar"
            Title="With Windows"
            IsPaneToggleButtonVisible="True"
            PaneToggleRequested="AppTitleBar_PaneToggleRequested" />

        <NavigationView
            x:Name="NavView"
            Grid.Row="1"
            AutomationProperties.AutomationId="MainNavView"
            IsBackButtonVisible="Collapsed"
            IsPaneToggleButtonVisible="False"
            IsSettingsVisible="True"
            SelectionChanged="NavView_SelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem
                    AutomationProperties.AutomationId="NavItemNotepad"
                    Content="记事本"
                    Tag="notepad">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE70B;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem
                    AutomationProperties.AutomationId="NavItemHotkeys"
                    Content="快捷键"
                    Tag="hotkeys">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE765;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>

            <Frame x:Name="NavFrame" />
        </NavigationView>
    </Grid>
</Window>
```

若 `TitleBar` 控件在当前的 Windows App SDK 版本中不可用（编译报类型不存在），退回旧项目已验证的写法：去掉 `TitleBar` 行，改为 `NavigationView` 占据 `Grid.Row="0"` 并跨两行，并在 `MainWindow.xaml.cs` 中设置 `ExtendsContentIntoTitleBar`。不要自创第三种结构。

- [ ] **Step 2: 写 `MainWindow.xaml.cs`（路由与标题栏联动）**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Views;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    /// <summary>导航项 Tag → 页面类型。新增工具页只需在此登记一处。</summary>
    private static readonly Dictionary<string, Type> Routes = new(StringComparer.Ordinal)
    {
        [NotepadPage.RouteTag] = typeof(NotepadPage),
        [HotkeysPage.RouteTag] = typeof(HotkeysPage),
        [SettingsPage.RouteTag] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate(SettingsPage.RouteTag);
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            Navigate(tag);
    }

    private void Navigate(string tag)
    {
        if (!Routes.TryGetValue(tag, out Type? pageType) || NavFrame.CurrentSourcePageType == pageType)
            return;

        NavFrame.Navigate(pageType);
    }
}
```

若 Step 1 退回了无 `TitleBar` 的方案，删除 `AppTitleBar_PaneToggleRequested` 方法与对应的 `PaneToggleRequested` 属性。

- [ ] **Step 3: 写三个占位页面（以 `NotepadPage` 为例，另两个同构）**

`Views/NotepadPage.xaml`：

```xml
<Page
    x:Class="WithWindows.Views.NotepadPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid Padding="24">
        <TextBlock
            AutomationProperties.AutomationId="NotepadPlaceholder"
            Style="{StaticResource TitleTextBlockStyle}"
            Text="记事本" />
    </Grid>
</Page>
```

`Views/NotepadPage.xaml.cs`：

```csharp
using Microsoft.UI.Xaml.Controls;

namespace WithWindows.Views;

public sealed partial class NotepadPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "notepad";

    public NotepadPage() => InitializeComponent();
}
```

`HotkeysPage` 与 `SettingsPage` 按同一结构创建，`RouteTag` 分别为 `hotkeys` 与 `settings`，`AutomationId` 分别为 `HotkeysPlaceholder` 与 `SettingsPlaceholder`，标题分别为「快捷键」与「设置」。

- [ ] **Step 4: 构建**

Run:
```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
```
Expected: 退出码 0。

- [ ] **Step 5: 启动并验证导航**

Run:
```powershell
dotnet run --project src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```
手动确认：窗口启动即停在「记事本」页；点「快捷键」「设置」能切换内容；标题栏能折叠左栏。确认后关闭窗口。

- [ ] **Step 6: Commit**

```bash
git add src/WithWindows.App
git commit -m "feat(导航): 新增导航壳与三个工具页面占位"
```

---

### Task 8: 用 `winapp ui` 建立界面自检闭环

**Files:**
- Create: `docs/agents/ui-baseline.md`
- Create: `artifacts/ui/`（截图输出目录，加入 `.gitignore`）

**Interfaces:**
- Consumes: Task 7 的导航壳与三个页面
- Produces: 一份可复现的界面自检流程，后续每次改 UI 都照此执行

- [ ] **Step 1: 安装 `winapp` CLI**

Run:
```powershell
winget install --id Microsoft.WinAppCli --accept-package-agreements --accept-source-agreements
winapp --version
```
Expected: 输出版本号（当前约 `0.7.x`）。若命令不存在，重开终端再试。

- [ ] **Step 2: 启动应用（保持运行）**

Run:
```powershell
dotnet run --project src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```
保持该窗口运行，另开一个终端执行后续步骤。

- [ ] **Step 3: 导出无障碍树并确认控件存在**

Run:
```powershell
winapp ui inspect
```
Expected: 输出中能找到 `MainNavView`、`NavItemNotepad`、`NavItemHotkeys`、`NotepadPlaceholder` 这些 AutomationId。若为空或找不到应用，先执行 `winapp --help` 核对其「选择目标窗口」的参数。

- [ ] **Step 4: 截图**

Run:
```powershell
New-Item -ItemType Directory -Force -Path artifacts/ui | Out-Null
winapp ui screenshot
```
Expected: 生成应用窗口截图。把产物移动到 `artifacts/ui/` 下并记录文件名。

- [ ] **Step 5: 点击导航项验证可交互**

Run:
```powershell
winapp ui click NavItemHotkeys
winapp ui inspect
```
Expected: `inspect` 输出中出现 `HotkeysPlaceholder`，说明导航切换生效。

- [ ] **Step 6: 把截图目录加入忽略**

在 `.gitignore` 末尾追加：

```
# 界面自检截图
artifacts/
```

- [ ] **Step 7: 写 `docs/agents/ui-baseline.md`**

记录：本次基线的截图文件名、`winapp ui inspect` 输出的关键 AutomationId 清单、以及可复现的四条命令（run / inspect / screenshot / click）。这份文件是后续 UI 改动的对照基准。

- [ ] **Step 8: Commit**

```bash
git add .gitignore docs/agents/ui-baseline.md
git commit -m "docs: 记录界面自检基线与 winapp ui 流程"
```

---

## 自检结果

**Spec 覆盖检查：**

| Spec 条目 | 对应任务 |
|---|---|
| 2. 技术栈 / TFM / 平台 | Task 4、6 |
| 3.1 脚手架策略（未打包手工组织） | Task 6 Step 3 |
| 4. 架构分层（Core 禁 UI） | Task 4 Step 1、Task 5 Step 5 |
| 5.1 导航壳 + Mica + TitleBar | Task 7 |
| 5.5 UI 硬规则（AutomationId） | Task 7 Step 1、3 |
| 6.1 工程文件 | Task 1、2、6 |
| 7.2 六步闭环 | Task 8 |
| 7.3 AGENTS.md 分层 | Task 3 |
| 9. 测试策略 | Task 5 |

P0 全部覆盖，P1 全部覆盖。P2–P5 属后续计划。

**未覆盖并有意推迟的：**

- `installer/WithWindows.iss` 与 CI 改造（P5）
- CsWin32 接入（P2，届时按需加入 `Microsoft.Windows.CsWin32` 包，版本已在 `Directory.Packages.props` 预留）
- 托盘、热键、单实例、自启（P2）
- 深浅色双主题截图（P5；Task 8 只做基线）

**类型一致性检查：**

- `AppPaths.DataRoot` / `ConfigPath` / `LogPath` / `NotepadDraftPath` 在 Task 4 定义，Task 5 的测试引用一致
- `RouteTag` 在 Task 7 的三个页面中以 `public static string RouteTag` 暴露，与 `MainWindow` 的 `Routes` 表键一致
- `MainWindow` 中 `NavView` / `NavFrame` 的 `x:Name` 与 code-behind 引用一致

**已知不确定项（执行时须实测确认，不得凭记忆断言）：**

1. `TitleBar` 控件是否在当前 WASDK 版本可用 —— Task 7 Step 1 已给出回退方案
2. `Microsoft.WindowsAppSDK` 2.5.1 与 `WinUIEx 2.9.2` / `CommunityToolkit.Mvvm 8.4.2` 是否兼容 net10 —— 还原失败时用 `dotnet package search` 查最新稳定版
3. `winapp ui` 选择目标窗口的具体参数 —— Task 8 Step 3 要求先查 `--help`
4. 单文件自包含的冷启动开销 —— P5 实测
