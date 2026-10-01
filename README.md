# <img src="docs/with-windows.png" width="32" alt="With Windows"> With Windows

![版本](https://img.shields.io/github/v/release/Lifeni/with-windows?label=%E7%89%88%E6%9C%AC)
![协议](https://img.shields.io/github/license/Lifeni/with-windows?label=%E5%8D%8F%E8%AE%AE)

> 本项目由 AI 协作完成：代码、文档与迭代均经 AI 生成和优化。

## 功能

Windows 常驻托盘的一键动作平台：全局热键 → 动作执行。无主界面，通过系统托盘管理，两个独立窗口：

**快捷记事**：热键弹出置顶记事本，关闭时保存并复制到剪贴板。
- 标题栏实时时钟（秒级刷新，窗口隐藏时停表）；Ctrl+滚轮 / Ctrl+加减号缩放字体（10-32px，Ctrl+0 重置）
- 行距舒适（1.05 倍）；Ctrl+S 另存为；Ctrl+C/V 原生复制粘贴
- 状态栏显示行列/字符数 + **置顶开关**（右下角图钉按钮，状态持久化）
- 窗口常驻：关闭 = 最小化到托盘，内容/字体/尺寸/位置跨开窗保留
- 正文为空时不写剪贴板；从托盘退出应用不会覆盖剪贴板内容
- 最小尺寸 520×780，可自由放大

**设置**：右键菜单"设置"打开。
- **切换投屏**：勾选参与循环切换的模式 + 快捷键设置/重置
- **快捷记事**：快捷键设置/重置
- 开机自启开关、恢复默认快捷键、关于
- 全部修改即时保存热重载；热键被占用导致注册失败时，用警告条说明是哪个键、为什么失败

**托盘菜单**：快捷记事 / 切换投屏 / 设置 / 退出（左键单击托盘图标打开记事本）。

## 快速开始

```bash
# 构建（需要 .NET 10 SDK；WinUI 3 需要 x64 平台）
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64

# 测试（必须全绿）
dotnet test tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj -p:Platform=x64

# 冒烟检查（不常驻，验证配置加载与热键注册）
dotnet run --project src/WithWindows.UI -- --smoke

# 发布：单文件（内置运行库，约 167 MB，免装任何运行库）
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true -p:PublishSingleFile=true

# 发布：单文件（框架依赖，约 38 MB，需预装 .NET 10 桌面运行时 + Windows App Runtime）
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist-fd -p:Platform=x64 -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -p:PublishSingleFile=true

# 开发辅助：一键构建并重启（Windows PowerShell）
powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
```

注意：常驻实例运行时会锁 exe，重新构建前先停掉它（托盘"退出"或任务管理器结束 WithWindows.exe）；需要 .NET 10 SDK（9 的 SDK 编译不了 net10 目标框架）。

## 下载

每个 Release 提供两个 x64 单文件 exe，双击即可运行，无需安装：

| 文件 | 大小 | 说明 |
| --- | --- | --- |
| `WithWindows-vX.Y.Z-x64-selfcontained.exe` | 约 167 MB | 内置 .NET 10 与 Windows App SDK 运行库，**推荐**，不依赖系统预装 |
| `WithWindows-vX.Y.Z-x64-frameworkdependent.exe` | 约 38 MB | 需系统已装 .NET 10 桌面运行时与 Windows App Runtime 2.4 |

两者都是单文件（XAML 资源与运行库已内嵌进 exe）；首次启动会把原生库解压到 `%TEMP%` 并复用，之后启动恢复正常。同时提供同名 `.zip`（自包含版约 66 MB），慢网络可以下 zip 再解压。`SHA256SUMS.txt` 提供校验和。

## 系统要求

- **Windows 10 1809（17763）及以上 / Windows 11，x64**（Windows App SDK 2.4 的 WinUI 程序集目标框架是 `net6.0-windows10.0.17763.0`，本项目使用 .NET 10）
- 两个下载版本对运行库的要求不同：

| 版本 | 需要系统预装 | 说明 |
| --- | --- | --- |
| `selfcontained` | 无 | 自带 .NET 10 与 Windows App SDK 运行库，双击即用 |
| `frameworkdependent` | .NET 10 桌面运行时 + Windows App Runtime 2.4 | 体积约 1/4；Windows 11 25H2 及更新版本已随系统自带 Windows App Runtime |

### 为什么提供两个版本

WinUI 3 应用需要两样东西：**现代 .NET 运行时**（不是 Windows 自带的 .NET Framework 4.8.1）和 **Windows App SDK 运行库**。

- 微软自家的系统组件（开始菜单/任务栏体验、文件资源管理器，以及记事本、画图、截图、照片等）走的是 MSIX 框架包依赖：系统镜像用 CBS 预置了 `Microsoft.WindowsAppRuntime.CBS.*`，应用清单里声明 `<TargetWASDKPackageName>` 即可，无需单独安装
- 未打包（unpackaged）的第三方应用没有这层便利，只能二选一：**自包含**（把运行库打进产物，体积大但零依赖）或**框架依赖**（体积小，但要求用户预装运行库）
- 本项目两种都提供，默认推荐自包含版

## 技术栈

- .NET 10（LTS）+ WinUI 3（Windows App SDK 2.4）：现代 Win11 原生 UI
- WinUIEx：托盘图标（TrayIcon）、窗口管理
- Microsoft.Windows.SDK.BuildTools：构建期工具（不随产物分发）
- P/Invoke：`RegisterHotKey`（隐藏消息窗口收 WM_HOTKEY）、`SetDisplayConfig` / `QueryDisplayConfig`（投屏）；注册表读写用 `Microsoft.Win32.Registry`（开机自启、置顶状态）
- `System.Text.Json` 配置读写（原子替换 + `config.json.bak` 备份回退）
- xunit + Microsoft.NET.Test.Sdk：单元测试
- Windows App SDK 按组件包引用（只引 Base / Foundation / InteractiveExperiences / WinUI / DWrite / Runtime，不打包用不到的 Widgets / AI / ML / Search）
- **发布为单文件 exe**：自包含约 167 MB；WinUI 3 不支持裁剪（`PublishTrimmed`），单文件依赖 `EnableMsixTooling`（已在 csproj 中按条件开启）

## 配置

运行时数据位于 `%APPDATA%\WithWindows\`：

- `config.json`——配置（首次启动自举默认值；旧 v2 数组格式自动迁移为 v3）
- `config.json.bak`——上一次保存成功的配置，主文件损坏时自动回退
- `log.txt`——运行日志（超过 1 MB 轮转为 `log.1.txt`）
- `notepad.txt`——记事本内容

```json
{
  "version": 3,
  "bindings": { "notepad": "F13", "display_mode": "F14" },
  "displayMode": { "modes": ["internal", "extend"] },
  "windowState": { "notepadFontSize": 14, "notepadWidth": 520, "notepadHeight": 780, "settingsWidth": 520, "settingsHeight": 780 }
}
```

- `version`：配置模型版本，当前为 3
- `bindings`：动作 → 热键，在设置窗口录制修改（保存即热重载）。热键留空 = 不绑定；字母/数字必须配合修饰键（F1–F24 可单键）
- `displayMode.modes`：投屏 toggle 循环的候选模式（修改后立即生效，无需重启）
- `windowState`：窗口尺寸/位置/字体记忆（关闭重开自动恢复；位置超出屏幕自动移回主屏）。没有保存过位置的窗口不含 `notepadX` / `notepadY` / `settingsX` / `settingsY` 字段

仓库内的 [config/config.json](config/config.json) 是同样结构的示例文件，仅作参考。

## 目录结构

```
with-windows/
├── AGENTS.md               # AI Agent 开发指南（架构/约定/测试规范）
├── CHANGELOG.md            # 更新日志（Release 正文来源）
├── LICENSE                 # MIT 开源协议
├── WithWindows.sln         # 解决方案文件
├── .github/workflows/      # ci.yml（push/PR 构建测试 + 单文件校验）、release.yml（打 tag 出两个单文件 exe）
├── config/                 # 配置示例（运行时配置在 %APPDATA%\WithWindows）
├── docs/                   # 图标与截图素材
├── scripts/                # 开发脚本（dev.ps1、IconGen 图标生成）
├── src/WithWindows.UI/     # 主程序（WinUI 3）
│   ├── App.xaml(.cs)       # 入口：单实例 → 配置 → 主窗口（托盘宿主）
│   ├── MainWindow.xaml.cs  # 托盘、热键注册、动作分发
│   ├── ToggleWindow.xaml   # 设置窗口（投屏/快捷键/自启/关于）
│   ├── Notepad/            # 记事本窗口（编辑/时钟/缩放/置顶/记忆）
│   ├── Controls/           # 热键录制控件（HotkeyInputBox）
│   ├── Core/               # 热键解析/注册、单实例、动作框架
│   ├── Config/             # ConfigStore（v3 + 旧格式迁移）
│   ├── Actions/            # 投屏动作（DisplayModeAction）
│   └── Interop/            # P/Invoke 集中地
└── tests/WithWindows.UI.Tests/  # 单元测试
```

## 发布

发布由**版本号驱动**，不需要手动打 tag：

1. 改 `src/WithWindows.UI/WithWindows.UI.csproj` 里的 `<Version>`，并在 `CHANGELOG.md` 顶部加同版本号的 `## [vX.Y.Z] - 日期` 章节
2. push 到 `main`：工作流发现该版本还没有对应的 `vX.Y.Z` tag，就自动跑测试 → 产出两个单文件 exe + 两个 zip + `SHA256SUMS.txt` → 打 tag 并发布 Release
3. 版本号没变（tag 已存在）时，push 到 `main` 只跑 `ci.yml` 的构建与测试，**不会**重复构建发布

也可以手动 `git tag vX.Y.Z && git push origin vX.Y.Z` 直接发布；工作流会校验 tag 与 csproj 版本一致，不一致直接失败。

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)。

## 开源协议

[MIT](LICENSE) — 可自由使用、修改、商用，需保留版权声明。
