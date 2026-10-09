# <img src="docs/with-windows.png" width="32" alt="With Windows"> With Windows

![版本](https://img.shields.io/github/v/release/Lifeni/with-windows?label=%E7%89%88%E6%9C%AC)
![协议](https://img.shields.io/github/license/Lifeni/with-windows?label=%E5%8D%8F%E8%AE%AE)

> 本项目由 AI 协作完成：代码、文档与迭代均经 AI 生成和优化。

## 功能

Windows 常驻托盘的工具箱：左侧 `NavigationView` 切换工具页，关闭窗口只是隐藏到托盘，单进程常驻后台。首版三个页面：

**记事本**（打开即编辑器）

- 输入即自动保存草稿（500 ms 防抖）到 `%APPDATA%\WithWindows\notepad.txt`，重开自动读回
- 页内秒级时钟；字体缩放 10–32 px（`Ctrl+滚轮` 与 `A−` / `A+`），字号随配置持久化
- 置顶开关作用于主窗口，切页返回后状态保持正确

**快捷键**

- 每个可绑热键的工具一行：录制框 + 启用开关 + 立即执行
- 点击录制框后按下组合键即录入，`Esc` 取消，可用「清除」解除绑定；不预置任何默认热键
- 注册失败（如 1409 已被占用）在该行就地显示原因，不静默失败

**设置**

- 开机自启（写 `HKCU\...\Run`，用户级）、投屏候选模式（至少保留一项）、关于（版本 / 配置路径 / 日志路径）

**托盘**：左键唤出主窗口，右键菜单「记事本 / 快捷键 / 设置 / 退出」。

内置工具：**记事本**、**切换投屏**（`SetDisplayConfig` 直接应用拓扑；目标与当前一致时提示「当前已是…」，不做无谓切换）。

## 快速开始

```bash
# 构建解决方案（需要 .NET 10 SDK；WinUI 3 需要 x64 平台）
dotnet build WithWindows.sln -p:Platform=x64

# 核心逻辑测试（必须全绿）
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64

# 启动应用（调试用）
dotnet run --project src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64

# 开发辅助：一键构建并重启（Windows PowerShell）
powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
```

冒烟检查（不常驻、不抢单实例、不建托盘，结果写日志）：

```bash
& "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe" --smoke
```

发布与打包（安装器脚本见 [installer/README.md](installer/README.md)）：

```powershell
# 自包含单文件（约 225 MB，免装任何运行库）
dotnet publish src/WithWindows.App/WithWindows.App.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true -p:PublishSingleFile=true

# 编译安装器（需 Inno Setup 6）
$ver = [regex]::Match((Get-Content src/WithWindows.App/WithWindows.App.csproj -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=$ver installer\WithWindows.iss
```

注意：常驻实例会锁 exe，重新构建前先退出应用（托盘「退出」或任务管理器结束 `WithWindows.exe`）。

## 下载与安装

每个 Release 提供两个 x64 产物：

| 文件 | 大小 | 说明 |
| --- | --- | --- |
| `WithWindows-vX.Y.Z-x64-setup.exe` | 约 60 MB | **推荐**。Inno Setup 安装器：装到 `%LocalAppData%\Programs\With Windows`，创建开始菜单项，桌面快捷方式与开机自启为可选任务 |
| `WithWindows-vX.Y.Z-x64-portable.exe` | 约 225 MB | 便携单文件，双击即用，不写安装信息 |

`SHA256SUMS.txt` 提供校验和。安装器**未签名**，首次运行会出现 SmartScreen「未知发布者」提示：点「更多信息 → 仍要运行」即可（这是设计文档第 10.4 节的既定取舍，与 MSIX 自签名被直接拒绝有本质区别）。

卸载会删除程序目录、开始菜单项与自启项，**不触碰** `%APPDATA%\WithWindows\` 下的用户数据。

## 系统要求

- **Windows 11 21H2（22000）及以上，x64**——目标框架 `net10.0-windows10.0.22621.0`，不背 Windows 10
- Release 产物为**自包含**：无需预装 .NET 运行时与 Windows App Runtime
- 自行构建框架依赖版本时，需 .NET 10 桌面运行时 + Windows App Runtime

## 技术栈

- .NET 10 + WinUI 3（Windows App SDK）：未打包（`WindowsPackageType=None`），目标框架 `net10.0-windows10.0.22621.0`
- 仅 x64；`PublishTrimmed` 固定 `false`（WinUI 3 不支持裁剪）；单文件发布依赖 `EnableMsixTooling`（csproj 按条件开启）
- WinUIEx：托盘 `TrayIcon`；CommunityToolkit.Mvvm：页面视图模型
- **P/Invoke 一律 CsWin32 生成**（`src/WithWindows.Core/Interop/NativeMethods.txt`），不手写 `DllImport`
- 全局热键用**独立隐藏消息窗口**接收 `WM_HOTKEY`（绝不子类化 WinUI 窗口，项目已验证两次会原生崩溃）
- 配置用 `System.Text.Json`（原子替换 + `config.json.bak` 回退 + 自愈清理未知工具）；测试用 xunit

## 配置

运行时数据位于 `%APPDATA%\WithWindows\`：

- `config.json`——配置（首次启动自举默认值；旧格式不兼容，识别到即重建默认值）
- `config.json.bak`——上一次保存成功的配置，主文件损坏时自动回退
- `log.txt`——运行日志（超过 1 MB 轮转为 `log.1.txt`）
- `notepad.txt`——记事本内容

```json
{
  "version": 1,
  "tools": [
    { "id": "notepad", "enabled": true, "hotkey": null },
    { "id": "display_mode", "enabled": true, "hotkey": null }
  ],
  "displayMode": { "modes": ["internal", "extend"] },
  "windowState": { "width": 1100, "height": 720, "x": null, "y": null, "notepadFontSize": 14 }
}
```

- `version`：配置模型版本，当前为 1
- `tools`：工具列表，每项含 `id` / `enabled` / `hotkey`；`hotkey` 为 `null` 或空 = 不绑定（**不预置任何默认热键**）
- `displayMode.modes`：投屏 toggle 的候选模式，按固定顺序循环（修改后立即生效）
- `windowState`：窗口尺寸/位置与记事本字号。位置为 `null` 表示尚未保存（`(0, 0)` 是合法坐标，不用 0 当哨兵）；位置超出所有显示器时回主屏居中

仓库内的 [config/config.json](config/config.json) 是同样结构的示例文件，仅作参考。

## 目录结构

```
with-windows/
├── AGENTS.md               # AI Agent 开发指南（架构/约定/测试规范）
├── CHANGELOG.md            # 更新日志（Release 正文来源）
├── LICENSE                 # MIT 开源协议
├── WithWindows.sln         # 解决方案文件
├── .github/workflows/      # ci.yml（PR 构建 + 测试 + 发布链路校验）、release.yml（出安装器与便携 exe）
├── config/                 # 配置示例（运行时配置在 %APPDATA%\WithWindows）
├── docs/                   # 素材与文档（agents 分册、设计文档、实现计划、界面基线）
├── installer/              # Inno Setup 脚本与说明
├── scripts/                # 开发脚本（dev.ps1、IconGen 图标生成）
├── src/WithWindows.App/    # 未打包 WinUI 3 应用（Shell/ Views/ ViewModels/ Services/ Controls/ Themes）
├── src/WithWindows.Core/   # 纯逻辑类库（Hotkeys/ Config/ Actions/ Interop/ Diagnostics/ App）
└── tests/WithWindows.Core.Tests/  # 核心逻辑单元测试
```

## 发布

发布由**版本号驱动**，不需要手动打 tag：

1. 改 `src/WithWindows.App/WithWindows.App.csproj` 的 `<Version>`，并在 `CHANGELOG.md` 顶部加同版本号的 `## [vX.Y.Z] - 日期` 章节
2. push 到 `main`：工作流发现该版本还没有对应的 `vX.Y.Z` tag，就自动跑测试 → 发布自包含单文件 → 编译安装器 → 产出安装器、便携 exe 与 `SHA256SUMS.txt` → 打 tag 并创建 Release
3. 版本号未变（tag 已存在）时只跑测试，不重复构建与发布；PR 由 `ci.yml` 负责构建 + 测试 + 单文件/安装器校验（两者触发条件不重叠）

发布中途失败时，可在 Actions 里手动 Run workflow 并勾选 `force` 重新发布当前版本。

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)。

## 开源协议

[MIT](LICENSE) — 可自由使用、修改、商用，需保留版权声明。
