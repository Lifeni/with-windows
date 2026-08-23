# <img src="docs/with-windows.png" width="32" alt="With Windows"> With Windows

![版本](https://img.shields.io/github/v/release/Lifeni/with-windows?label=%E7%89%88%E6%9C%AC)
![协议](https://img.shields.io/github/license/Lifeni/with-windows?label=%E5%8D%8F%E8%AE%AE)

> 本项目由 AI 协作完成：代码、文档与迭代均经 AI 生成和优化。

## 功能

Windows 常驻后台的一键动作平台：配置驱动的全局热键 → 动作框架。无主界面，通过系统托盘管理。

把所有"一键操作"需求收敛到一个常驻程序：热键集中注册、动作按接口扩展。

- 内置动作：快捷记事、投影切换、亮暗切换、日出日落自动亮暗
- 托盘菜单：快捷记事、动作切换（带快捷键提示）、自动亮暗、开机自启、打开配置、恢复配置、重启应用、版本号

<img src="docs/screenshot.png" alt="快捷记事" width="630">

## 快速开始

```bash
dotnet build src/WithWindows/WithWindows.csproj

# 运行(开发)
dotnet run --project src/WithWindows

# 发布 Release(输出到 dist/)
dotnet publish src/WithWindows/WithWindows.csproj -c Release -o dist

# 冒烟检查(不常驻,验证配置加载与热键注册)
dotnet run --project src/WithWindows -- --smoke
```

## 技术栈

- .NET Framework 4.8.1：Win11 内置，免安装 runtime，直接运行
- WinForms 无窗口宿主 + 自管系统托盘（自定义气泡图标）
- 自绘 Win11 风格托盘菜单：圆角实色卡片（双缓冲防闪烁）、主题跟随、Segoe Fluent Icons 图标
- P/Invoke：`RegisterHotKey`、`SetDisplayConfig`、`QueryDisplayConfig`、`SendMessageTimeout`（WM_SETTINGCHANGE）；主题读写用 `Microsoft.Win32.Registry`
- 内置 MiniJson，零外部依赖，单 exe 发布（约 100KB）
- 高 DPI（PerMonitorV2）、单实例守卫（重复启动自动退出）

## 配置

运行时数据位于 `%APPDATA%\WithWindows\`（exe 目录保持干净）：

- `config.json`——热键配置，首次启动自举默认值
- `log.txt`——运行日志

启动后弹一次"已在后台运行"通知，列出生效热键。热键支持：

- 单键：`F13`～`F24`、`F1`～`F12`、字母或数字键
- 组合键：`Ctrl+Shift+F14`、`Alt+F13`（修饰键：`Ctrl`、`Alt`、`Shift`、`Win`）

托盘右键菜单：

- **快捷记事**：原生多行文本框（Maple Mono 等宽字体、始终置顶、行/列/字符数显示在标题栏、Ctrl+S 另存为、撤回/恢复、Ctrl+加号/Ctrl+滚轮缩放、自动保存），快捷键 `F13`（显示时按 F13 复制内容并关闭）
- **切换投影 / 切换亮暗**：与对应热键一致，右侧显示快捷键（`F15` / `F14`）
- **打开配置**：用默认程序打开 `%APPDATA%\WithWindows\config.json`
- **自动亮暗**：勾选启用日出日落自动切换（重启后保持）
- **开机自启**：写 `HKCU\...\Run`，无需管理员权限
- **恢复配置**：删除运行时配置、注册表设置与记事本内容，恢复默认并重启（带确认对话框）
- **重启应用**：配置只在启动时读取，修改后点此生效
- **版本 v0.3.0**：点击跳转 GitHub 项目页
- **退出应用**


示例：

```json
[
  { "hotkey": "F13", "action": "notepad" },
  { "hotkey": "F14", "action": "theme", "args": { "mode": "toggle" } },
  { "hotkey": "F15", "action": "display_mode", "args": { "mode": "toggle", "modes": ["internal", "extend"] } }
]
```

`display_mode` 支持：

- `internal` / `extend` / `external` / `clone`：直接切换指定模式；已是该模式则不执行
- `toggle`：在 `modes`（默认 `["internal","extend"]`）中循环切换

`theme` 支持：

- `light` / `dark`：直接切换（广播 `WM_SETTINGCHANGE`，运行中的应用即时刷新）
- `toggle`：切换相反值（亮 ↔ 暗）

`auto_theme`（声明式条目，可选）：日出切亮色、日落切暗色，托盘菜单勾选启用。内置默认坐标，不配置也能用：

```json
{ "action": "auto_theme", "args": { "latitude": "纬度", "longitude": "经度", "offset_minutes": "0" } }
```

- `latitude` / `longitude`：按日期计算（NOAA 算法，中纬度误差约几分钟；时区固定北京时间 UTC+8）
- `sunrise` / `sunset`（`"HH:mm"`）：固定时间
- `offset_minutes`：切换点整体偏移，正数=延后
- 极昼/极夜地区当天不切换；错过切换点会自动对账修正

## 目录结构

```
with-windows/
├── AGENTS.md        # AI Agent 开发指南(架构/约定/测试规范)
├── CHANGELOG.md     # 更新日志(Release 正文来源)
├── LICENSE          # MIT 开源协议
├── WithWindows.sln # 解决方案文件
├── .github/         # GitHub Actions 工作流(构建并发布 Release)
├── config/          # 配置模板(运行时数据在 %APPDATA%\WithWindows)
├── docs/            # 设计文档与素材
├── scripts/         # 开发/运维脚本
├── src/WithWindows/    # 主程序
└── tests/WithWindows.Tests/  # 单元测试
```

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)。

## 开源协议

[MIT](LICENSE) — 可自由使用、修改、商用，需保留版权声明。
