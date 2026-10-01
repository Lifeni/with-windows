# AGENTS.md — With Windows 开发指南

Windows 常驻托盘的一键动作平台：配置驱动的全局热键 → 动作框架。无主界面，两个独立窗口（快捷记事、设置），单进程常驻后台。

## 硬性约束（不可破坏）

- **.NET 10 + WinUI 3**（`net10.0-windows10.0.19041.0` + Windows App SDK 2.4）：Win11 现代原生 UI
- **构建必须指定 x64 平台**：`-p:Platform=x64`（WinUI 3 不支持 AnyCPU；RuntimeIdentifier 已固定 win-x64）
- **发布为单文件 exe**：自包含版免装 .NET runtime 与 WindowsAppSDK（约 167 MB）；另有框架依赖版（约 38 MB，需预装运行时）。WinUI 3 不支持裁剪；单文件必须同时开 `EnableMsixTooling`（csproj 已按条件设置）
- **运行时数据全在 `%APPDATA%\WithWindows\`**：exe 目录保持干净（仅 Assets 资源）
- 语言 C# `latest` + nullable + implicit usings；测试用 xunit
- 第三方依赖仅限：WindowsAppSDK（按组件包引用）、WinUIEx
- 注释与用户可见文案使用中文
- **用户侧系统要求**：Windows 10 1809（17763）及以上 / Windows 11，x64；自包含版零依赖，框架依赖版需要 .NET 10 桌面运行时 + Windows App Runtime 2.4（Windows 11 25H2 起随系统自带）

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
# 发布 Release：单文件（内置运行库，约 167 MB）
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist -p:Platform=x64 -p:SelfContained=true -p:PublishSingleFile=true
# 发布 Release：单文件（框架依赖，约 38 MB，需预装 .NET 10 桌面运行时 + Windows App Runtime）
dotnet publish src/WithWindows.UI/WithWindows.UI.csproj -c Release -o dist-fd -p:Platform=x64 -p:SelfContained=false -p:WindowsAppSDKSelfContained=false -p:PublishSingleFile=true
# 开发辅助：一键构建并重启（PowerShell）
powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
```

注意：常驻实例运行时会锁 exe，重新构建前先停掉它（任务管理器结束 WithWindows.exe）。构建产物在 `bin/x64/Debug/.../win-x64/` 子目录（RuntimeIdentifier 化的输出路径）。编译需要 .NET 10 SDK（9 的 SDK 不能编译 net10 目标框架）。

## 架构

```
App.xaml.cs          入口：单实例守卫 → 配置加载（v3 + 旧格式迁移）→ MainWindow 创建
MainWindow.xaml.cs   常驻宿主：托盘图标 + 热键注册/分发 + 设置窗口管理
ToggleWindow.xaml    设置窗口：投屏（卡片/模式/快捷键）+ 记事本快捷键 + 自启 + 关于
Notepad/             记事本窗口：编辑/实时时钟/字体缩放/置顶/状态记忆
Controls/            HotkeyInputBox：录制式热键输入控件（聚焦后按组合键捕获）
Core/                热键解析/格式化/注册、单实例互斥体、动作框架
Config/              ConfigStore（System.Text.Json）+ AppConfig 模型
Actions/             投屏动作（DisplayModeAction）
Interop/             P/Invoke 集中地（RegisterHotKey、SetDisplayConfig/QueryDisplayConfig 等）
```

### 启动流程（App.xaml.cs）

1. `--smoke` 参数：加载配置 + 注册热键后立即退出（不抢单实例互斥体，不创建托盘）
2. 单实例守卫：`Local\WithWindows.SingleInstance` 命名 Mutex，重复启动直接退出（互斥体由 App 字段持有到进程退出）
3. 加载 `%APPDATA%\WithWindows\config.json`（首次自举默认值；旧 v2 数组格式自动迁移；主文件损坏回退 `.bak`；仍失败则弹框并退出进程）
4. 创建 `MainWindow`：托盘图标、热键注册
5. **常驻模式不显示主窗口**（仅托盘），由托盘菜单/热键唤出记事本与设置窗口

### 关键机制

- **单实例守卫**：互斥体必须由 App 持有到进程退出。禁止写成 `OnLaunched` 里的 `using var` 局部变量——方法返回即释放，常驻期间第二个实例照样能起
- **热键热重载**：`MainWindow.ReloadBindings(AppConfig)` 同时刷新投屏候选模式、注销并重注册全部热键，返回失败说明；设置窗口保存后据此提示用户
- **Logger 生命周期 = 应用**：App 持有实例字段，热键回调跨 OnLaunched 使用，禁止用局部 `using` 释放（曾致崩溃）
- **配置写入**：`ConfigStore.Save` 原子替换（同目录临时文件 + `File.Replace`），旧内容留为 `config.json.bak`；`Load` 在 JSON 损坏时回退 `.bak` 并修复主文件
- **Windows App SDK 组件包**：csproj 逐项引用 Base 2.0.4 / Foundation 2.3.9 / InteractiveExperiences 2.1.6 / WinUI 2.3.6 / DWrite 2.1.0 / Runtime 2.4.0（= 伞包 2.4.0 的依赖清单去掉 Widgets / AI / ML / Search）；升级 WASDK 时必须整体对齐版本，否则框架依赖模式会报组件版本不一致
- **单文件发布**：`-p:PublishSingleFile=true` 时 WASDK 强制要求 `EnableMsixTooling=true`（csproj 用条件属性自动处理）；产物里只有 exe 是必需的，`WithWindows.pri` 与 `.xbf` 已内嵌、可删
- **运行库依赖模型**：WinUI 3 需要现代 .NET（不是 .NET Framework）加 Windows App SDK 运行库。微软自家应用走 MSIX 框架包依赖（系统用 CBS 预置 `Microsoft.WindowsAppRuntime.CBS.*`，应用清单声明 `TargetWASDKPackageName`）；未打包应用只能自包含或要求用户预装，故本项目同时发布两种产物
- **发布产物清单**：`WithWindows-vX.Y.Z-x64-selfcontained.exe`（约 167 MB）、`-frameworkdependent.exe`（约 38 MB）、两者同名 zip（自包含 zip 约 64 MB）与 `SHA256SUMS.txt`
- **记事本常驻**：关闭（X 或热键）= 最小化到托盘（拦截 Closed 事件），窗口不销毁；内容/字体/尺寸/位置/置顶跨开窗保留
- **窗口状态记忆**：尺寸/位置/字体持久化到 `config.json` 的 `windowState`；位置未保存时为 `null`（(0, 0) 是合法坐标，不能用 0 当哨兵）；恢复前校验可见性，超出屏幕自动移回主屏居中
- **配置 v3 schema**：
  ```json
  {
    "version": 3,
    "bindings": { "notepad": "F13", "display_mode": "F14" },
    "displayMode": { "modes": ["internal", "extend"] },
    "windowState": { "notepadFontSize": 14, "notepadWidth": 520, "notepadHeight": 780, "settingsWidth": 520, "settingsHeight": 780 }
  }
  ```
  热键留空 = 不绑定；字母/数字热键必须带修饰键（F1–F24 可单键）；投屏默认热键 F14（注意：可能被系统程序占用，需在设置窗口改键）
- **窗口最小尺寸**：用 `OverlappedPresenter.PreferredMinimumWidth/Height` 官方 API（勿用 Win32 子类化，已两次验证致崩溃）
- **记事本**：标题栏实时时钟（DispatcherQueueTimer 秒级）、Ctrl+滚轮/± 缩放字体（10-32px）、置顶开关（注册表持久化 `HKCU\Software\WithWindows\Notepad\Pinned`）、行距 1.05（RichEditBox 段落格式）

### 已知坑（勿重复踩）

- **窗口子类化（SetWindowSubclass / WM_GETMINMAXINFO）与 WinUI 3 消息泵冲突**：重复开关窗口致原生崩溃（0xc000027b），一律用官方 API
- **DisplayArea.FindAll() 在窗口打开时可能抛 InvalidCastException**：调用处必须 try-catch（失败视为可见，不阻塞）
- **AppWindow.PreferredMinimum* 属性不存在**（在 OverlappedPresenter 上）
- **App.UnhandledException 只能兜底托管异常**，原生崩溃（XAML 层）不经过它，需靠事件日志排查
- **热键注册失败不抛异常**：`RegisterHotKey` 返回 false + Win32 错误码（1409 = 已被占用），必须把失败原因回传设置窗口提示，否则用户只会觉得"按了没反应"
- **发布/构建前必须停掉常驻实例**：运行中的 exe 会锁住输出文件，构建报 MSB3021/MSB3027（不是代码错误）
- **本机 .NET 8 运行时卸载不掉**：它们带 `SystemComponent=1` 且被 Visual Studio 作为组件安装（来自 `C:\ProgramData\Microsoft\VisualStudio\Packages\...`），MSI 日志写 `Disallowing uninstallation of component ... since another client exists`，winget/msiexec 都返回成功但实际不动。要清只能走 Visual Studio Installer，别强删目录（会破坏 MSI 数据库）

## 提交规范（硬性）

遵循 [Conventional Commits](https://www.conventionalcommits.org) 与原子提交原则：

- **一次提交只做一件事**：按逻辑变更拆分（功能、修复、文档、重构分开），禁止把无关改动揉进同一个提交
- 格式：`类型(范围): 一句话描述`——描述用祈使句，中文标点，中英文间加空格
- 类型（范围可选，给出一词上下文）：
  - `feat`：新功能　`fix`：修复缺陷　`docs`：文档（README、CHANGELOG、AGENTS）
  - `refactor`：重构（不改行为）　`perf`：性能　`test`：测试　`chore`：杂务　`ci`：CI 配置
- 示例：`feat(记事本): 支持字体缩放`、`fix(设置): DisplayArea校验失败不阻塞窗口打开`
- 描述尽量简洁（≤50 字）；需要细节时空一行写正文段落

## 测试约定

- 纯函数优先（动作逻辑、热键解析/格式化、配置加载与迁移）
- 平台相关（注册表/显示器）只做只读断言，禁止写测试改变真实状态
- 测试命令需带 `-p:Platform=x64`（WinUI 3 项目引用要求）
- 完整套件必须通过后才算完成
