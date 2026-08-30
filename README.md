# <img src="docs/with-windows.png" width="32" alt="With Windows"> With Windows

![版本](https://img.shields.io/github/v/release/Lifeni/with-windows?label=%E7%89%88%E6%9C%AC)
![协议](https://img.shields.io/github/license/Lifeni/with-windows?label=%E5%8D%8F%E8%AE%AE)

> 本项目由 AI 协作完成：代码、文档与迭代均经 AI 生成和优化。

## 功能

Windows 常驻托盘的一键动作平台：全局热键 → 动作执行。无主界面，通过系统托盘管理，两个独立窗口：

**快捷记事**：热键弹出置顶记事本，关闭时自动复制到剪贴板并保存。
- 标题栏实时时钟（秒级刷新）；Ctrl+滚轮 / Ctrl+加减号缩放字体（10-32px，Ctrl+0 重置）
- 行距舒适（1.05 倍）；Ctrl+S 另存为；Ctrl+C/V 原生复制粘贴
- 状态栏显示行列/字符数 + **置顶开关**（右下角图钉按钮，状态持久化）
- 窗口常驻：关闭 = 最小化到托盘，内容/字体/尺寸/位置跨开窗保留
- 最小尺寸 520×780（官方 API 限制），可自由放大

**设置**：右键菜单"设置"打开。
- **切换投屏**：大卡片点击切换 + 勾选循环模式 + 快捷键设置/重置
- **快捷记事**：快捷键设置/重置
- 开机自启开关、恢复默认快捷键、关于
- 全部修改即时保存热重载

**托盘菜单**：快捷记事 / 切换投屏 / 设置 / 退出（左键单击托盘图标打开记事本）。

## 快速开始

```bash
# 构建（WinUI 3 需要 x64 平台）
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64

# 测试（必须全绿）
dotnet test tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj -p:Platform=x64

# 冒烟检查（不常驻，验证配置加载与热键注册）
dotnet run --project src/WithWindows.UI -- --smoke

# 发布 Release（自包含目录，免装 runtime，输出到 dist/）
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true

# 开发辅助：一键构建并重启（Windows PowerShell）
powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
```

注意：常驻实例运行时会锁 exe，重新构建前先停掉它（托盘"退出"或任务管理器结束 WithWindows.exe）。

## 技术栈

- .NET 8 + WinUI 3（Windows App SDK 2.4）：现代 Win11 原生 UI
- WinUIEx：托盘图标（TrayIcon）、窗口管理
- P/Invoke：`RegisterHotKey`、`SetDisplayConfig`、`QueryDisplayConfig`、`SendMessageTimeout`（WM_SETTINGCHANGE）；主题读写用 `Microsoft.Win32.Registry`
- `System.Text.Json` 配置读写
- **发布为自包含目录**（含 .NET runtime + WindowsAppSDK，免装任何依赖，约 200 MB）

## 配置

运行时数据位于 `%APPDATA%\WithWindows\`：

- `config.json`——配置（首次启动自举默认值；旧 v2 数组格式自动迁移为 v3）
- `log.txt`——运行日志
- `notepad.txt`——记事本内容

```json
{
  "bindings": { "notepad": "F13", "display_mode": "F14" },
  "displayMode": { "modes": ["internal", "extend"] },
  "windowState": { "notepadFontSize": 14, "notepadWidth": 520, "notepadHeight": 780, "notepadX": 0, "notepadY": 0, "settingsWidth": 520, "settingsHeight": 780, "settingsX": 0, "settingsY": 0 }
}
```

- `bindings`：动作 → 热键，在设置窗口录制修改（保存即热重载）。热键留空 = 不绑定
- `displayMode.modes`：投屏 toggle 循环的候选模式
- `windowState`：窗口尺寸/位置/字体记忆（关闭重开自动恢复；位置超出屏幕自动移回主屏）

## 目录结构

```
with-windows/
├── AGENTS.md               # AI Agent 开发指南（架构/约定/测试规范）
├── CHANGELOG.md            # 更新日志（Release 正文来源）
├── LICENSE                 # MIT 开源协议
├── WithWindows.sln         # 解决方案文件
├── .github/                # GitHub Actions 工作流（构建并发布 Release）
├── docs/                   # 设计文档与素材
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

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)。

## 开源协议

[MIT](LICENSE) — 可自由使用、修改、商用，需保留版权声明。
