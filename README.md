# <img src="docs/with-windows.png" width="32" alt="With Windows"> With Windows

![版本](https://img.shields.io/github/v/release/Lifeni/with-windows?label=%E7%89%88%E6%9C%AC)
![协议](https://img.shields.io/github/license/Lifeni/with-windows?label=%E5%8D%8F%E8%AE%AE)

> 本项目由 AI 协作完成：代码、文档与迭代均经 AI 生成和优化。

## 功能

Windows 常驻托盘的一键动作平台：全局热键 → 动作执行。无主界面，通过系统托盘管理，两个独立功能窗口：

**快捷记事（带 AI）**：热键弹出置顶记事本，关闭时自动复制到剪贴板。
- 剪贴板建议条：打开时若剪贴板有文本且与正文不同，底部显示可点击条目，点击即可追加
- AI 助手：把记事本内容发给 AI（OpenAI 兼容端口），SSE 流式打字机回复
- 自动保存（`notepad.txt`），下次打开恢复内容；始终置顶

**一键切换**：亮暗与屏幕切换的配置窗口。
- 切换亮暗：录制式热键绑定 + 日出日落自动切换（NOAA 算法，坐标/固定时间/偏移可配）
- 切换屏幕：录制式热键绑定 + 切换模式列表（internal/extend/external/clone）
- 保存即生效（热重载，无需重启）

托盘菜单：快捷记事 / 一键切换 / 退出（双击托盘图标打开一键切换）。

## 快速开始

```bash
# 构建（WinUI 3 需要 x64 平台）
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64

# 测试（必须全绿）
dotnet test tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj -p:Platform=x64

# 冒烟检查（不常驻，验证配置加载与热键注册）
dotnet run --project src/WithWindows.UI -- --smoke

# 发布 Release（自包含，输出到 dist/）
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true
```

注意：常驻实例运行时会锁 exe，重新构建前先停掉它（托盘"退出"或任务管理器结束 WithWindows.exe）。

## 技术栈

- .NET 8 + WinUI 3（Windows App SDK 2.4）：现代 Win11 原生 UI
- WinUIEx：托盘图标（TrayIcon）、窗口管理
- P/Invoke：`RegisterHotKey`、`SetDisplayConfig`、`QueryDisplayConfig`、`SendMessageTimeout`（WM_SETTINGCHANGE）；主题读写用 `Microsoft.Win32.Registry`
- `System.Text.Json` 配置读写；AI 对话手写 SSE 流式解析（零第三方 SDK）
- 发布为自包含目录（免装 .NET runtime），约 200 MB

## 配置

运行时数据位于 `%APPDATA%\WithWindows\`：

- `config.json`——配置（首次启动自举默认值；旧 v2 数组格式自动迁移为 v3）
- `log.txt`——运行日志
- `notepad.txt`——记事本内容

```json
{
  "bindings": { "notepad": "F13", "theme": "F14", "display_mode": "F15" },
  "displayMode": { "modes": ["internal", "extend"] },
  "theme": { "enabled": false, "latitude": 36.6512, "longitude": 117.1201, "offsetMinutes": 0 },
  "ai": { "baseUrl": "", "apiKey": "", "model": "" }
}
```

- `bindings`：动作 → 热键，可在"一键切换"窗口录制修改（保存即热重载）。热键留空 = 不绑定
- `displayMode.modes`：屏幕 toggle 循环的候选模式
- `theme`：日出日落自动切换开关与参数（坐标与固定时间二选一；`sunrise`/`sunset` 为 `"HH:mm"`；`offsetMinutes` 正数 = 延后）
- `ai`：AI 助手配置（OpenAI 兼容端口，`baseUrl` 如 `http://127.0.0.1:11434/v1`）

## 目录结构

```
with-windows/
├── AGENTS.md               # AI Agent 开发指南（架构/约定/测试规范）
├── CHANGELOG.md            # 更新日志（Release 正文来源）
├── LICENSE                 # MIT 开源协议
├── WithWindows.sln         # 解决方案文件
├── .github/                # GitHub Actions 工作流（构建并发布 Release）
├── docs/                   # 设计文档与素材
├── src/WithWindows.UI/     # 主程序（WinUI 3）
│   ├── App.xaml(.cs)       # 入口：单实例 → 配置 → 主窗口（托盘宿主）
│   ├── MainWindow.xaml.cs  # 托盘、热键注册、动作分发、自动亮暗调度
│   ├── ToggleWindow.xaml   # 一键切换配置窗口
│   ├── Notepad/            # 记事本窗口（编辑器 + 剪贴板建议条 + AI 面板）
│   ├── Controls/           # 热键录制控件（HotkeyInputBox）
│   ├── Core/               # 热键解析/注册、单实例、AI 客户端、动作框架
│   ├── Config/             # ConfigStore（v3 + 旧格式迁移）
│   ├── Actions/            # 亮暗/屏幕动作、日出日落调度
│   └── Interop/            # P/Invoke 集中地
└── tests/WithWindows.UI.Tests/  # 单元测试
```

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)。

## 开源协议

[MIT](LICENSE) — 可自由使用、修改、商用，需保留版权声明。
