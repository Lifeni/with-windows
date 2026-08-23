# AGENTS.md — With Windows 开发指南

Windows 常驻托盘的一键动作平台：配置驱动的全局热键 → 动作框架。无主界面，单 exe 常驻后台。

## 硬性约束（不可破坏）

- **.NET Framework 4.8.1**（`net481`），Win11 免安装 runtime 直接跑——这是项目的核心卖点
- **零外部依赖**：无 NuGet 运行时包、无 Windows App SDK/WinUI。JSON 用内置 MiniJson（仅对象/数组/字符串）
- **单 exe 发布**：`dotnet publish -c Release -o dist`，产物约 76KB。exe 目录保持干净，运行时数据全在 `%APPDATA%\WithWindows\`
- 语言 C# `latest` + nullable + implicit usings；测试用 xunit
- 注释与用户可见文案使用中文

## 文案规范（硬性）

- **中文内容必须使用中文标点**：`，。；：""''（）——・` 等；禁止混用英文标点 `, . ; : " ' ( )`
- **中英文之间加空格**：如 `Win11 风格`、`config.json 文件`、`net481 单 exe`；中文与数字之间同样加空格
- 代码标识符（变量名、类名、API）本身保持原样，仅在其与中文相邻处加空格
- 以上适用于代码注释、README、AGENTS、日志文案、气泡提示、**git 提交信息**等一切可见文本
- **每次改动后必须复查 README 与 CHANGELOG**：功能、命令、目录结构、版本号等任何变化都要同步到文档；README 与代码不一致视为缺陷
- **升级/改动后不要自动启动应用**：构建、测试、提交、推送即可，由用户自行启动验证（除非用户明确要求启动）

## 常用命令

```bash
dotnet build src/WithWindows/WithWindows.csproj        # 主程序
dotnet test tests/WithWindows.Tests/WithWindows.Tests.csproj   # 测试（必须全绿）
dotnet run --project src/WithWindows -- --smoke          # 冒烟：注册检查，不常驻
dotnet publish src/WithWindows/WithWindows.csproj -c Release -o dist
```

注意：常驻实例运行时会锁 exe，重新构建前先停掉它（任务管理器结束 WithWindows.exe）。

## 架构

```
Program.cs        入口：单实例守卫 → 配置加载 → 动作注册 → App 启动 → 自动亮暗恢复
App.cs            常驻宿主：托盘图标 + 热键注册 + 菜单构建 + 动作执行（气泡/日志）
TrayIcon.cs       自管系统托盘（NOTIFYICONDATA + 隐藏消息窗口）
ModernMenu.cs     自绘 Win11 风格托盘菜单（圆角实色卡片、图标、勾选、快捷键提示）
Core/             热键解析/注册、动作注册表、单实例互斥体
Config/           ConfigStore（%APPDATA% 配置读写）+ MiniJson
Actions/          内置动作 + 日出日落调度
Interop/          P/Invoke 集中地（RegisterHotKey、SetDisplayConfig、DWM、鼠标钩子等）
```

### 启动流程（Program.cs）

1. `--smoke` 参数：注册检查后立即退出（不抢单实例互斥体）
2. 单实例守卫：`Local\WithWindows.SingleInstance` 命名 Mutex，重复启动直接退出
3. 加载 `%APPDATA%\WithWindows\config.json`（首次自举默认值；解析失败弹框并退出）
4. 注册动作到 `ActionRegistry`，构建 `App`（含托盘菜单），`RegisterAll` 注册热键
5. 若自动亮暗启用标志为真，恢复 `AutoThemeScheduler`

### 新增一个动作（标准流程）

1. 实现 `IAction`（`Name` + `Execute(object? args)` 返回 `ActionResult(Changed, Message)`）；失败抛异常由宿主捕获
2. 纯逻辑抽成 `internal static` 供测试（参考 `DisplayModeAction.Decide`/`ThemeAction.PickToggleTarget`）
3. `Program.cs` 注册进 `ActionRegistry`
4. 配置条目：`{ "hotkey": "...", "action": "动作名", "args": {...} }`
5. 若需要托盘菜单项：`App` 构造菜单时添加 `ModernMenuItem`，并捕获首个配置条目的参数/热键回填
6. 测试：纯函数 + 参数解析；**禁止写测试改变真实状态**（注册表/主题/显示器）

### 配置 schema

- 条目：`hotkey`（可省略=声明式条目，如 `auto_theme`）、`action`、`args`
- `display_mode`：`mode` 为 internal/extend/external/clone/toggle（`modes` 数组）
- `theme`：`mode` 为 light/dark/toggle
- `notepad`：无参数，切换快捷记事显示/隐藏（隐藏时复制内容到剪贴板）
- `auto_theme`（声明式）：`latitude`/`longitude`/`offset_minutes` 或固定 `sunrise`/`sunset`；缺省回退内置默认坐标

### 已知设计决策

- 菜单自绘而非 ContextMenuStrip：Win11 现代样式（圆角+图标+提示）只有自绘或 WinUI 3 能做到；后者破坏零依赖约束
- 自绘弹窗：实色主题卡片 + 双缓冲（半透明 Acrylic 已被移除——其整窗重绘是闪烁根源）
- 菜单文字垂直居中带 `TextShift` 光学补偿（CJK 墨迹不占满行盒底部的视觉修正）
- 主题读写：`HKCU\...\Themes\Personalize` 的 AppsUseLightTheme/SystemUsesLightTheme + WM_SETTINGCHANGE 广播
- 开机自启：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`（用户级免管理员）
- 日出日落：NOAA 算法（`SunTimes.cs`），参考值见测试注释
- 配置只在启动时读取一次：修改 `config.json` 后需重启生效（托盘菜单"重启应用"，重启前先释放单实例互斥体）
- 日志：`Logger` append-only；被占用时降级为 Null 不阻断启动

## 提交规范（硬性）

遵循 [Conventional Commits](https://www.conventionalcommits.org) 与原子提交原则：

- **一次提交只做一件事**：按逻辑变更拆分（功能、修复、文档、重构分开），禁止把无关改动揉进同一个提交
- 格式：`类型(范围): 一句话描述`——描述用祈使句，中文标点，中英文间加空格
- 类型（范围可选，给出一词上下文）：
  - `feat`：新功能　`fix`：修复缺陷　`docs`：文档（README、CHANGELOG、AGENTS）
  - `refactor`：重构（不改行为）　`perf`：性能　`test`：测试　`chore`：杂务　`ci`：CI 配置
- 示例：`feat(记事本): 支持 Ctrl+滚轮缩放`、`fix(菜单): 修复高 DPI 下图标溢出`、`docs: 更新 README`
- 描述尽量简洁（≤50 字）；需要细节时空一行写正文段落

## 测试约定

- 纯函数优先；P/Invoke 布局用 `Marshal.SizeOf` 断言（曾因 CCD 结构体越界崩过 testhost）
- 覆盖：动作逻辑、配置加载、热键解析、MiniJson、日出日落参考值、调度纯函数
- 完整套件必须通过后才算完成
