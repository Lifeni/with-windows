# 代码与文档一致性检查清单

本文用于在改动、审查和发布前集中检查容易与代码产生偏差的文档内容。目标是让读者按当前行为使用软件，而不是按历史设计或旧截图推断行为。

## 事实源优先级

出现矛盾时按以下顺序判断：

1. 当前可运行代码、XAML、项目文件和 `.github/workflows`；
2. `README.md`、`CHANGELOG.md`、`docs/development.md` 与 `docs/agents/` 的当前说明；
3. `docs/design/` 与 `docs/superpowers/plans/` 的历史记录。

历史文档可以保留旧方案，但不能拿旧方案反推当前行为。若代码已变化而历史文档仍会被误读，优先在文首补“实现更新”，不要大范围重写历史记录。

## 高风险对照表

| 易漂移点 | 唯一事实源 | 必须同步检查 | 常见错误 |
|---|---|---|---|
| 当前版本与发布 | `src/WithWindows.App/WithWindows.App.csproj` 的 `<Version>` | `CHANGELOG.md` 同版本章节、Release tag、安装器产物名 | 在文档或安装器注释里硬编码旧版本号 |
| 发布流程 | `.github/workflows/release.yml` 与 `ci.yml` | `docs/development.md`、`docs/agents/development-workflow.md`、`installer/README.md` | 把 CI 的 `Program Files (x86)` 路径写成本机路径，或把本地流程写成 CI 行为 |
| 示例初始化 | `src/WithWindows.Core/Samples/SampleContentSeeder.cs` | `README.md`、`CHANGELOG.md`、`docs/development.md`、`installer/README.md`、代码注释 | 写成“全新安装才写入”，实际升级后只要对应数据不存在也会触发 |
| 运行时数据 | `src/WithWindows.Core/Diagnostics/AppPaths.cs` | `docs/development.md`、`installer/README.md`、卸载说明 | 文档漏文件，或对卸载时保留还是删除 `%APPDATA%\WithWindows\` 的承诺写反 |
| 导航名称 | `src/WithWindows.App/Tools/ToolCatalog.cs` | `MainWindow`、`TrayHost`、设置页、界面基线 | 把导航名“对话”和设置项名“AI 对话”当成同一处文案 |
| 切换与主题 | `DisplayPage.xaml`、`ThemeToggleAction` / `ThemeSwitchHost`、`ThemeScheduleDefaults`、`TrayHost` | `README.md`、`docs/development.md`、界面基线、示例配置 | 把“显示切换”和“主题切换”写成同一动作，漏掉定位失败默认时间，或托盘文案不是四字 / 不随当前主题变化 |
| 托盘图标样式 | `AppConfig.TrayConfig`、`AppIcon.ResolveTray`、`TrayHost.ApplyIconStyle` | `SettingsPage`、`Assets/tray-lightning-thin-*.ico`、界面基线、`docs/agents/interop-and-platform.md` | 默认值不是细线闪电，闪电图标不随亮暗主题换色，字形额外留白，或资源未随 `Assets\**` 发布 |
| 托盘收起与唤回 | `MainWindow.xaml.cs` 的 `HideToTray` / `ShowWindow` | `docs/agents/known-pitfalls.md`、`CHANGELOG.md` | 写成“先最小化、再移除切换器”，或删掉 `IsWindow` 与异常隔离 |
| 退出流程 | `MainWindow` 的 `RequestExitFromTray` / `ExitApplicationAsync`、`TrayHost` 的 `MenuFlyout.Closed` | `docs/agents/known-pitfalls.md`、`docs/agents/winui3-hard-rules.md`、`CHANGELOG.md` | 在菜单 `Click` 回调内释放托盘，退出失败后重置 `_exitRequested`，或重新调用 `Application.Exit()` |
| 激活刷新 | `MainWindow` 的 `QueueWindowThemeRefresh` / `ApplyTitleBarTheme` / `RefreshTrayIcon` | `docs/agents/known-pitfalls.md`、`docs/agents/winui3-hard-rules.md`、`CHANGELOG.md` | 在 `Activated` / `ActualThemeChanged` 回调栈内同步写标题栏并替换托盘图标导致 `uxtheme` 重入，或普通激活时重复写 `PreferredTheme` |
| 界面行为 | 当前 XAML 与 code-behind | `docs/agents/ui-baseline.md`、`CHANGELOG.md` | 把应用内遮罩写成原生轻触关闭，或沿用已删除的关闭方式 |
| UI Automation 标识 | `AutomationProperties.AutomationId` 与代码赋值 | `docs/agents/ui-baseline.md` | 控件改名后基线仍保留旧 ID |
| 截图与图片 | `docs/images/` 下的实际文件 | `README.md`、`docs/agents/ui-baseline.md` | 截图清单漏页面，或正文写“四张”但目录已有六张 |
| 构建命令 | 项目文件与仓库脚本 | `AGENTS.md`、`docs/development.md`、`docs/agents/development-workflow.md` | 漏 `-p:Platform=x64`，或把 Debug / Release 输出路径混用 |

## 每次改动后的重点检查

### 一、版本与发布

版本号只从 csproj 读取，不要在文档或注释中复制一份：

```powershell
$ver = [regex]::Match(
  (Get-Content src/WithWindows.App/WithWindows.App.csproj -Raw),
  '<Version>([^<]+)</Version>'
).Groups[1].Value
$verPattern = [regex]::Escape($ver)

rg -n "<Version>|v$verPattern|$verPattern" src/WithWindows.App/WithWindows.App.csproj CHANGELOG.md installer .github/workflows
```

检查点：

- `CHANGELOG.md` 有 `## [v$ver]` 章节，发布日期与发布时一致；
- 安装器注释、发布工作流和 Release 资产名没有硬编码旧版本；
- 本地手动发布路径与 CI 路径分开说明：本机 Inno Setup 在 `%LOCALAPPDATA%\Programs\Inno Setup 6`，GitHub Runner 在 `C:\Program Files (x86)\Inno Setup 6`；
- `release.yml` 仍先跑测试，再按“版本是否有对应 tag”决定是否发布；
- `ci.yml` 仍只负责 PR / 手动校验，不与 `main` 上的发布重复。

### 二、示例内容

关键词扫描：

```powershell
rg -n "首次安装|全新安装|首次启动|示例初始化|sample-content|示例待办|示例脚本" README.md CHANGELOG.md docs installer src tests
```

每一处文案都要能对应以下实际规则：

- `sample-content.json` 存在时跳过全部示例初始化；
- 标记不存在时，分别检查 `todos.json` 与 `scheduled-tasks/tasks.json` 及其 `.bak`；
- 对应主文件或备份已存在时，不向该类别写入示例；
- 待办与脚本按类别分别判断，正常情况下可只补齐尚不存在的一类；
- 某类写入异常会中止本次初始化；已成功写入的类别会在下次启动时按数据存在性跳过；
- 初始化成功或执行检查后写入 `sample-content.json`；
- 用户主动删空列表后不会因再次启动而重新填充。

不要使用“全新安装才写入”这类表述。准确说法是“首次执行示例初始化时，按类别为尚不存在的数据写入示例”。

### 三、界面行为与基线

先查实现，再改 `docs/agents/ui-baseline.md`：

```powershell
rg -n "IsLightDismissEnabled|Popup|ContentDialog|轻触关闭|显式遮罩|Esc" src/WithWindows.App docs/agents/ui-baseline.md
rg -n "AutomationProperties.AutomationId|AutomationId =" src/WithWindows.App
```

检查点：

- 基线写明的每个 UI Automation 标识都能在 XAML 或代码中找到；
- 关闭方式、默认展开状态、动画、输入行为等描述与当前 code-behind 一致；
- 普通收起路径必须先用 `WindowInterop.IsWindow` 校验句柄，再在窗口仍处于还原状态时设置 `AppWindow.IsShownInSwitchers = false`，之后才调用 `WindowInterop.Minimize`；唤回路径也要先校验句柄再恢复切换器状态，两处切换器操作必须有异常隔离；
- 托盘收起回归至少重复“关闭到托盘 → 再次启动唤回”三轮，日志顺序应为“从切换器移除 → 最小化 → 完成”，进程始终可响应；
- 托盘“退出”必须等 `MenuFlyout.Closed` 后再排队执行：日志应先出现“托盘：请求退出”，随后依次出现退出开始、后台停止、窗口状态保存、托盘释放与“即将结束进程”；清理任一步失败仍须结束进程，不能把 `_exitRequested` / `_exiting` 回滚；真实鼠标点击后进程消失，且 `CrashDumps` 与 Application Error 日志没有新增记录；
- 标题栏主题与托盘图标刷新必须使用低优先级 `DispatcherQueue` 并合并同一轮请求，不得直接写在 `Activated` / `ActualThemeChanged` 回调内；回归路径为切换托盘图标、关闭到托盘、真实左键唤回并等待至少 2 秒，日志显示 `CodeActivated` 后进程持续响应且没有新增 `0xC000027B` / `0x80070578`；
- 设置页“默认打开窗口”选项仍与 `WindowDefaultPage` 和导航项对应；
- 导航名与设置项名允许不同，但必须明确来源，例如导航为“对话”、设置项为“AI 对话”；
- `README.md` 截图与 `docs/agents/ui-baseline.md` 截图清单都包含当前全部页面图片。

### 四、数据路径与安装器

对照实现检查 `docs/development.md`：

```powershell
Get-Content src/WithWindows.Core/Diagnostics/AppPaths.cs
rg -n "%APPDATA%|config.json|todos.json|sample-content.json|scheduled-tasks|chat/|wallpapers|themeSwitch" docs installer README.md config
```

检查点：

- 新增运行时文件或目录已写入 `AppPaths.cs` 与用户文档；
- 程序目录只放程序与资源，用户数据仍全部位于 `%APPDATA%\WithWindows\`；
- 安装器不直接写用户数据，卸载不删除 `%APPDATA%\WithWindows\`；
- 自启项、快捷方式和安装目录行为与 `installer/WithWindows.iss` 一致。

### 五、切换页、主题与托盘

先查实现，再同步文档：

```powershell
rg -n "SwitchTitle|DisplaySwitchGroup|ThemeSwitchGroup|DisplayGroupSubtitle|ThemeGroupSubtitle|ThemeSchedule|ThemeWallpaper|SwitchGroupExpanderStyle|SettingsCardStyle|HotkeyBox_display_mode|HotkeyBox_theme_toggle|ThemeLightTime|ThemeDarkTime|TraySwitchMenu|TrayItemSwitchDisplay|TrayItemSwitchTheme|TrayIconStyleCard|TrayIconStyleComboBox|ResolveTray|ApplyIconStyle" src/WithWindows.App docs/agents/ui-baseline.md
rg -n "AppsUseLightTheme|SystemUsesLightTheme|WM_SETTINGCHANGE|WallpaperStore|wallpapers" src/WithWindows.Core docs/development.md
```

检查点：

- “切换”页只由“显示切换”“主题切换”两个内置 `Expander` 构成唯一折叠层级，默认展开；组标题与内容分别是原生模板的两层卡片表面，两个分组标题下的副标题分别显示当前显示拓扑、当前亮暗主题与自动切换状态；
- 标题使用 `CardBackgroundFillColorDefaultBrush`，内容使用 `CardBackgroundFillColorSecondaryBrush`，内容边框为 `1,0,1,1`，标题底边形成 1 DIP 接缝；不得在外层再套统一 `Border` 或同色背景，否则半透明画刷叠层会让标题偏亮、内容偏暗；
- 自动切换开关位于 `Expander.Header` 的第三列；鼠标点击开关只能切换开关自身，分组展开或收起时都不得因此折叠。两个分组必须使用内置 `Expander` 默认模板；
- 展开后的内容位于透明内容区，`SettingsCardStyle` 只负责内边距 `52,12,16,12`，背景必须透明、边框必须为 0；左侧 52 DIP 对齐组标题图标后的文字，右侧 16 DIP 对齐展开箭头右缘，也与设置页「关于」折叠内容一致；两个分组之间间距 12 DIP；不得绘制第三层功能卡片，不得嵌套第二层 `Expander`，也不得增加第二层小标题、`SettingsGroupCardStyle` 或整块大卡片；
- 显示组内只有候选模式；主题组内只有主题壁纸；不得重新加入显示 / 主题快捷键行、手动时间选择器或每张壁纸单独的清除按钮；主题壁纸标题旁的“恢复默认”在有自定义壁纸时显示，可一次清除两套壁纸；该链接必须使用 12 DIP 字号、`MinHeight=0` 和零内边距，不得把标题行撑高；
- 候选模式必须加入 `ModesHost`，复选框行高 36 DIP；候选模式位于同一透明内容区，候选行之间不另加卡片或分隔线；
- 组标题与功能项标题统一使用 `BodyStrongTextBlockStyle`；时间标签和文件状态文字使用 `CaptionTextBlockStyle` 或次级色，不得混用不同层级的字重；
- 内容区内边距必须保持左侧 52 DIP、右侧 16 DIP：左侧等于 Expander 默认 16 DIP、20 DIP 图标与 16 DIP 图标间距之和，右侧等于 Expander 默认内容边距；内容区与组标题同宽，不得再增加左右外边距；
- `SwitchGroupExpanderStyle` 只允许设置 `Background`、`BorderBrush`、`BorderThickness`、`CornerRadius`、`Padding` 和对齐属性；禁止设置 `ControlTemplate`，禁止使用 `Expander.Tag` 承载交互控件，禁止修改模板内部元素的高度；
- 两个分组使用内置 `Expander` 默认模板；折叠内容由原生模板负责，父级 `SwitchGroupsHost` 必须使用原生 `RepositionThemeTransition` 让兄弟分组平滑跟随，不得出现手写 Expander 高度 `Storyboard`、`Clip` 或模板内部元素修改。快速连续展开、收起至少 8 轮，并在分组展开与收起两种状态下各点击一次自动切换开关后，进程仍能响应且不崩溃；
- 切换 Windows 亮暗主题后，标题栏最小化 / 最大化 / 关闭按钮必须同步换色；`AppWindowTitleBar.PreferredTheme` 只刷新 caption 背景，还必须设置 `ButtonForegroundColor`、`ButtonHoverForegroundColor`、`ButtonPressedForegroundColor` 与 `ButtonInactiveForegroundColor`，并在窗口重新激活时再刷新一次；扩展内容到标题栏时按钮背景保持透明以露出 Mica；
- 主题动作同时切换应用与系统界面，配置了壁纸时使用 `%APPDATA%\WithWindows\wallpapers\` 内的副本，缺失时保留当前壁纸；应用壁纸时只替换图片，不得覆写 `WallpaperStyle` / `TileWallpaper`，以保留用户当前的契合度与平铺位置；拨动自动切换开关只重新布防，不得仅因开关变化重复应用主题和壁纸；
- 自动切换的启动对账、开关布防与定时应用必须在后台线程执行；`ThemeSwitchHost.Start()` / `RefreshSchedule()` 不得在 UI 线程同步写注册表或调用 `SystemParametersInfo`，否则启用开关或启动应用时主窗口会短暂无响应；
- 托盘一级“切换”下只有显示与主题两个动作，二级文案始终是下一次目标的四个汉字，点击执行循环而不是跳到固定目标；
- 自动切换按最近一次有效经纬度计算日出日落；定位失败、权限不可用或没有有效坐标时，开关保持开启并退回每天 `07:00` / `19:00` 的默认计划；驻留托盘时继续、完全退出后停止，启动时按当前时刻对账，定时器因睡眠或系统繁忙迟到后也会在触发时重新对账，不重放每一次错过的切换；
- 设置页“托盘图标”卡片左侧使用 `E790`，右侧下拉框具有 `AutomationId = TrayIconStyleComboBox`，候选项为 `lightning-thin` / `default`，默认选中 `lightning-thin`；选择后必须立即写入 `tray.iconStyle` 并刷新托盘图标，不能要求重启；
- 闪电托盘图标必须根据实际亮暗主题选择对应样式的 `-light.ico`（深色闪电）或 `-dark.ico`（白色闪电），并在系统主题变化、窗口重新激活与主题动作完成后刷新；资源缺失时必须回退 `with-windows.ico`，不能让托盘没有入口；
- 闪电 ICO 必须由 `scripts/generate-tray-icons.ps1` 生成，裁掉字形透明边界后按长边铺满画布，不额外保留安全边距，覆盖 16 至 256 px，并保持 `Assets\**` 随构建与发布复制；旧 `lightning` / `lightning-thick` / `lightning-compact` / `lightning-fine` 配置值必须迁移到 `lightning-thin`；
- 配置为 v7，示例配置只含可配置热键的工具，`themeSwitch` 包含 `scheduleEnabled`、经纬度、更新时间与亮暗壁纸，`tray` 包含 `iconStyle`；旧版手动时间字段不再读取，迁移不会删除已有工具、模式或窗口状态。

### 六、Markdown 链接与图片

检查所有本地链接：

```powershell
$files = Get-ChildItem -Recurse -Filter *.md -File |
  Where-Object { $_.FullName -notmatch '\\.git\\' }

foreach ($file in $files) {
  $text = Get-Content -LiteralPath $file.FullName -Raw
  foreach ($match in [regex]::Matches($text, '\[[^\]]*\]\(([^)]+)\)')) {
    $target = $match.Groups[1].Value.Trim()
    if ($target -match '^[a-z]+:' -or $target -match '^#') { continue }
    $target = ($target -split '#')[0]
    if ([string]::IsNullOrWhiteSpace($target)) { continue }
    $full = [System.IO.Path]::GetFullPath(
      (Join-Path $file.DirectoryName ([uri]::UnescapeDataString($target.Trim('<>'))))
    )
    if (-not (Test-Path -LiteralPath $full)) {
      Write-Error "$($file.FullName): 缺失链接 $target"
    }
  }
}
```

同时确认 `docs/images/` 中每张图片至少被 `README.md` 或界面基线引用，避免留下无来源的截图。

## 发布前检查

- [ ] `dotnet build WithWindows.sln -p:Platform=x64` 零错误
- [ ] `dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64` 全绿
- [ ] `CHANGELOG.md` 的 `## [Unreleased]` 已移动到对应版本章节
- [ ] csproj 版本、CHANGELOG 标题、Release tag 三者一致
- [ ] `ci.yml` 与 `release.yml` 的路径、平台、单文件发布参数一致
- [ ] 安装器可编译，产物名与文档一致
- [ ] README、开发文档与界面基线已同步本次行为变化
- [ ] 历史设计文档没有新增与现状冲突的“当前”描述

## 典型漂移案例

以下问题都曾实际出现，可作为扫描关键词：

- 安装器注释写死 `0.8.1`，csproj 已到 `0.11.0`；
- 界面基线写“原生轻触关闭”，代码实际为 `IsLightDismissEnabled = false` 的应用内遮罩；
- 示例初始化写成“全新安装才触发”，实现实际按类别数据是否存在决定；
- 根 `AGENTS.md` 的 Core 目录地图漏掉新增的 `Samples/`；
- 界面基线截图清单只有四页，`docs/images/` 已有脚本与设置截图；
- 主题说明漏掉 v7 配置、定位失败默认时间、`wallpapers/` 目录或“托盘二级文案显示下一次循环目标”。

发现矛盾时，先判断是代码有缺陷还是文档过期。若代码行为符合需求，就修文档；若代码行为偏离需求，就修代码并同步文档与测试，不能只改文字掩盖行为变化。
