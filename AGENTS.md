# AGENTS.md — With Windows 开发指南

Windows 常驻托盘的一键动作平台：配置驱动的全局热键 → 动作框架。无主界面，两个独立功能窗口（快捷记事带 AI、一键切换），单进程常驻后台。

## 硬性约束（不可破坏）

- **.NET 8 + WinUI 3**（`net8.0-windows10.0.19041.0` + Windows App SDK 2.4）：Win11 现代原生 UI
- **构建必须指定 x64 平台**：`-p:Platform=x64`（WinUI 3 不支持 AnyCPU；RuntimeIdentifier 已固定 win-x64）
- **发布为自包含目录**：免装 .NET runtime，输出 `dist/`（约 200 MB）；WinUI 3 不支持裁剪/单文件
- **运行时数据全在 `%APPDATA%\WithWindows\`**：exe 目录保持干净（仅 Assets 资源）
- 语言 C# `latest` + nullable + implicit usings；测试用 xunit
- 第三方依赖仅限：WindowsAppSDK、WinUIEx、CommunityToolkit.Mvvm（如需）；AI 对话手写 SSE，不引 SDK
- 注释与用户可见文案使用中文

## 文案规范（硬性）

- **中文内容必须使用中文标点**：`，。；：""''（）——・` 等；禁止混用英文标点 `, . ; : " ' ( )`
- **中英文之间加空格**：如 `Win11 风格`、`config.json 文件`、`net8 自包含`；中文与数字之间同样加空格
- 代码标识符（变量名、类名、API）本身保持原样，仅在其与中文相邻处加空格
- 以上适用于代码注释、README、AGENTS、日志文案、气泡提示、**git 提交信息**等一切可见文本
- **每次改动后必须复查 README 与 CHANGELOG**：功能、命令、目录结构、版本号等任何变化都要同步到文档；README 与代码不一致视为缺陷
- **升级/改动后不要自动启动应用**：构建、测试、提交、推送即可，由用户自行启动验证（除非用户明确要求启动）

## 常用命令

```bash
dotnet build src/WithWindows.UI/WithWindows.UI.csproj -p:Platform=x64
dotnet test tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj -p:Platform=x64
dotnet run --project src/WithWindows.UI -- --smoke     # 冒烟：不常驻，验证配置加载与热键注册
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true
```

注意：常驻实例运行时会锁 exe，重新构建前先停掉它（任务管理器结束 WithWindows.exe）。构建产物在 `bin/x64/Debug/.../win-x64/` 子目录（RuntimeIdentifier 化的输出路径）。

## 架构

```
App.xaml.cs          入口：单实例守卫 → 配置加载（v3 + 旧格式迁移）→ MainWindow 创建
MainWindow.xaml.cs   常驻宿主：托盘图标 + 热键注册/分发 + 自动亮暗调度（含热重载）
ToggleWindow.xaml    一键切换窗口：亮暗（热键 + 日落自动）+ 屏幕（热键 + modes），保存即热重载
Notepad/             记事本窗口：编辑器 + 剪贴板建议条 + AI 助手（SSE 流式回复 + 配置）
Controls/            HotkeyInputBox：录制式热键输入控件（聚焦后按组合键捕获）
Core/                热键解析/格式化/注册、单实例互斥体、AI 客户端、动作框架
Config/              ConfigStore（System.Text.Json）+ AppConfig 模型
Actions/             亮暗/屏幕切换动作、日出日落调度（NOAA 算法）
Interop/             P/Invoke 集中地（RegisterHotKey、SetDisplayConfig、WM_SETTINGCHANGE 等）
```

### 启动流程（App.xaml.cs）

1. `--smoke` 参数：加载配置 + 注册热键后立即退出（不抢单实例互斥体）
2. 单实例守卫：`Local\WithWindows.SingleInstance` 命名 Mutex，重复启动直接退出
3. 加载 `%APPDATA%\WithWindows\config.json`（首次自举默认值；旧 v2 数组格式自动迁移；解析失败弹框并退出）
4. 创建 `MainWindow`：托盘图标、热键注册、自动亮暗恢复
5. **常驻模式不显示主窗口**（仅托盘），由托盘菜单/热键唤出记事本与一键切换窗口

### 关键机制

- **热键热重载**：`MainWindow.ReloadBindings(AppConfig)` 先注销全部热键再按新配置注册；一键切换窗口保存后调用。自动亮暗调度器随配置重建
- **Logger 生命周期 = 应用**：App 持有实例字段，热键回调跨 OnLaunched 使用，禁止用局部 `using` 释放（曾致崩溃）
- **配置 v3 schema**：
  ```json
  {
    "bindings": { "notepad": "F13", "theme": "F14", "display_mode": "F15" },
    "displayMode": { "modes": ["internal", "extend"] },
    "theme": { "enabled": false, "latitude": 36.6512, "longitude": 117.1201, "offsetMinutes": 0 },
    "ai": { "baseUrl": "", "apiKey": "", "model": "" }
  }
  ```
  热键留空 = 不绑定；`theme` 坐标与固定时间二选一；`ai.baseUrl` 为 OpenAI 兼容端口
- **AI 对话**：`AiClient.AskAsync` 手写 SSE 解析（`data:` 行 + `[DONE]`），`ParseSseDelta` 为纯函数；回调在后台线程，UI 更新需 `DispatcherQueue.TryEnqueue`
- **记事本**：显示中再按热键 = 复制内容到剪贴板并隐藏；关闭窗口同样复制；内容防抖保存到 `notepad.txt`；剪贴板建议条在打开时读取，非空且与正文不同才显示

### 新增一个动作（标准流程）

1. 新建动作类，暴露 `Execute(string mode)` 返回 `ActionResult(Changed, Message)`；失败抛异常由宿主捕获
2. 纯逻辑抽 `internal static` 供测试（参考 `DisplayModeAction.Decide`/`ThemeAction.PickToggleTarget`）
3. 在 `MainWindow.ExecuteAction` 的动作分发 switch 中接入
4. 配置条目：`bindings` 加 `"动作名": "热键"`；一键切换窗口补对应配置 UI（如需要）
5. 测试：纯函数 + 模式验证；**禁止写测试改变真实状态**（注册表/主题/显示器）

## 提交规范（硬性）

遵循 [Conventional Commits](https://www.conventionalcommits.org) 与原子提交原则：

- **一次提交只做一件事**：按逻辑变更拆分（功能、修复、文档、重构分开），禁止把无关改动揉进同一个提交
- 格式：`类型(范围): 一句话描述`——描述用祈使句，中文标点，中英文间加空格
- 类型（范围可选，给出一词上下文）：
  - `feat`：新功能　`fix`：修复缺陷　`docs`：文档（README、CHANGELOG、AGENTS）
  - `refactor`：重构（不改行为）　`perf`：性能　`test`：测试　`chore`：杂务　`ci`：CI 配置
- 示例：`feat(记事本): 支持剪贴板建议条`、`fix(发布): 补齐 publish 缺失的 XBF/PRI 资源`
- 描述尽量简洁（≤50 字）；需要细节时空一行写正文段落

## 测试约定

- 纯函数优先（动作逻辑、热键解析/格式化、SSE 解析、日出日落参考值、调度纯函数、配置加载与迁移）
- 平台相关（注册表/主题/显示器）只做只读断言，禁止写测试改变真实状态
- 测试命令需带 `-p:Platform=x64`（WinUI 3 项目引用要求）
- 完整套件必须通过后才算完成
