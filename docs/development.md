# 开发与构建

## 环境

- Windows 11 22H2（22621）及以上，x64
- .NET 10 SDK
- 可选：Inno Setup 6，用于本地生成安装器
- 可选：`winapp` CLI，用于 UI 自动化检查

技术栈为 .NET 10、WinUI 3（Windows App SDK）、xunit 和 Inno Setup。项目不使用 MVVM，页面保持 code-behind。

## 构建与测试

```powershell
dotnet build WithWindows.sln -p:Platform=x64
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64
```

常驻实例会锁住可执行文件，构建前先退出应用。

## 运行

```powershell
dotnet run --project src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
```

也可以直接运行：

```powershell
& "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe"
```

界面自检：

```powershell
winapp ui inspect -a WithWindows -i
winapp ui screenshot -a WithWindows
```

## 项目结构

```text
src/WithWindows.App/            WinUI 3 界面、托盘、窗口与页面
src/WithWindows.Core/           可测试的纯逻辑、存储、协议与平台封装
tests/WithWindows.Core.Tests/   xunit 测试
installer/                      Inno Setup 安装器
docs/                           用户、开发与设计文档
```

`WithWindows.Core` 不得引用 `Microsoft.UI.Xaml`。

## 数据与配置

运行时数据保存在 `%APPDATA%\WithWindows\`：

- `config.json`：应用配置，示例见 [config/config.json](../config/config.json)
- `notepad.txt`：记事草稿
- `todos.json`：待办与提醒
- `sample-content.json`：示例内容初始化的标记
- `scheduled-tasks/tasks.json`：C# 脚本定义
- `scheduled-tasks/runs.json`：最近执行历史
- `scheduled-tasks/scripts/`：内联 C# 脚本的运行副本
- `scheduled-tasks/logs/`：每次运行的 stdout 与 stderr 日志，逐行保存时间戳、顺序和输出流类型
- `scheduled-tasks/runner/`：隐藏 script helper 的短生命周期运行规格
- `chat/current.json`：当前 AI 会话
- `chat/credential.bin`：DPAPI 保护的 API Key
- `wallpapers/`：主题壁纸副本，文件名以 `light-` 或 `dark-` 开头
- `log.txt`：运行日志
- `log.1.txt`：上一份运行日志；当前日志超过 1 MB 时轮转生成

应用首次执行示例初始化时（全新安装与升级后的首次启动均可能触发），会按类别检查待办与脚本数据：对应主文件或备份尚不存在时，写入三条示例待办和三个示例脚本（手动环境巡检、每日摘要、每周发布检查）。已有数据不会被覆盖；初始化完成后写入 `sample-content.json`，因此用户在界面中删空列表后不会再次出现示例。

亮暗主题切换会同时写入当前用户的 `AppsUseLightTheme` 与 `SystemUsesLightTheme`，并广播 `WM_SETTINGCHANGE`；标题栏通过 `AppWindowTitleBar.PreferredTheme` 刷新 caption 背景，并显式同步按钮的正常、悬停、按下与非活动前景色，窗口重新激活时再刷新一次，避免从托盘或失焦状态切换主题后最小化 / 最大化 / 关闭按钮沿用旧颜色。应用壁纸时只通过 `SystemParametersInfo` 替换图片，不覆写 `WallpaperStyle` / `TileWallpaper`，保留用户当前选择的契合度和平铺位置。自动切换按最近一次成功获取的经纬度计算日出日落；位置权限不可用、定位超时或没有有效坐标时，开关保持开启并退回每天 `07:00` 切换到亮色、`19:00` 切换到暗色。关闭主窗口驻留托盘时继续，应用完全退出后停止；应用启动时按当前时刻对账，定时器因睡眠或系统繁忙迟到后也会在触发时重新对账，但不重放退出期间错过的每一次切换。拨动自动切换开关只重新布防，当前主题已匹配时不会重复应用主题和壁纸；布防、启动对账与定时应用均在后台线程执行，不得阻塞 UI 线程。

配置模型当前为 v7。`tools` 只保存当前可配置热键的工具，切换页的显示模式与主题动作不计入其中。`displayMode.modes` 保存切换页勾选的显示模式候选，至少两项；加载时过滤未知值、去重，不足两项时按固定顺序自动补足。`themeSwitch` 保存 `scheduleEnabled`、最近一次有效坐标 `latitude` / `longitude`、`locationUpdatedAt` 以及亮色和暗色壁纸路径；v1 至 v6 加载时自动迁移，旧手动时间字段不再读取。坐标不完整或越界、空白壁纸路径会在加载时自愈清理。`tray.iconStyle` 保存托盘图标样式：默认 `lightning-thin` 使用预生成细线闪电 ICO；`default` 使用 `Assets/with-windows.ico` 黄色图标。旧标准、粗线或实验闪电值加载时统一迁移为细线。设置页修改后立即生效，无需重启。

修改闪电托盘图标后运行 `pwsh scripts/generate-tray-icons.ps1`。脚本使用 `Segoe Fluent Icons` 的 `E945`，裁掉字形透明边界后按长边铺满画布，不额外保留安全边距，生成 16 至 256 px 多尺寸 ICO。闪电资源缺失时会自动回退黄色应用图标，不增加第三方依赖。

脚本使用应用内置的 Roslyn 编译器执行 C# 代码，不需要目标机器安装 .NET SDK，也不向脚本暴露应用私有 API。脚本通过 .NET 10 的公开 API 访问网络、文件和本地程序。脚本可仅手动运行，也可设置单次、每天、每周或每月执行；应用完全退出后不再执行计划，关闭主窗口并驻留托盘时继续执行。脚本不是沙箱，以当前用户权限运行。停止或超时会终止脚本 helper，脚本主动启动的程序可以继续运行。

## 发布

发布由 `src/WithWindows.App/WithWindows.App.csproj` 的版本号驱动。更新版本并同步 `CHANGELOG.md` 后，GitHub Actions 会生成安装器、便携包和校验文件。

本地发布与安装器编译步骤见 [installer/README.md](../installer/README.md)，维护流程见 [development-workflow.md](agents/development-workflow.md)。
