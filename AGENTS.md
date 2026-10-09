# AGENTS.md — With Windows 开发指南

Windows 常驻托盘的工具箱应用：配置驱动的全局热键 → 动作框架。左侧 `NavigationView` 导航，每个工具一页；单进程常驻后台，关闭主窗口只是隐藏到托盘。

## 技术栈（锁定，不得更换）

- .NET 10 + WinUI 3（Windows App SDK），目标框架 `net10.0-windows10.0.22621.0`
- 仅 x64：构建与测试必须带 `-p:Platform=x64`，RID 固定 `win-x64`
- 未打包（`WindowsPackageType=None`）；分发用 Inno Setup 安装器
- C# `latest` + nullable + implicit usings；测试用 xunit
- 第三方依赖白名单：WindowsAppSDK、WinUIEx、CommunityToolkit.Mvvm、Microsoft.Windows.CsWin32。白名单外一律先问
- 明确禁止：WPF、Windows Forms、UWP `Windows.UI.*`、WinUI 2、.NET MAUI、Reactor

## 硬性约束

- `PublishTrimmed` 必须为 `false`（WinUI 3 不支持裁剪）
- `WithWindows.Core` 中禁止出现任何 `Microsoft.UI.Xaml` 类型
- 运行时数据全在 `%APPDATA%\WithWindows\`，程序目录只放程序与资源
- `Logger` 与单实例互斥体必须由 `App` 字段持有到进程退出，禁止写成局部 `using`
- 每次改动后复查 README 与 CHANGELOG；文档与代码不一致视为缺陷
- **界面改动做完后直接启动应用并保持运行**，让用户立刻看到效果；不要验证完就关掉，也不要等用户自己启动（用户明确要求）

## 文案规范

- 中文内容必须使用中文标点：，。；：""''（）——・；禁止混用英文标点
- 中英文之间、中文与数字之间加空格（如 `Win11 风格`、`config.json 文件`）
- 代码标识符保持原样，仅在与中文相邻处加空格
- 适用于代码注释、README、CHANGELOG、日志、气泡提示、git 提交信息

## 常用命令

```powershell
dotnet build src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64
dotnet run --project src/WithWindows.App/WithWindows.App.csproj -p:Platform=x64
# 界面自检（应用需先处于运行状态）
winapp ui inspect
winapp ui screenshot
# 发布：见 docs/agents/development-workflow.md
```

常驻实例会锁住 exe，构建前先关闭应用。

## 目录地图

```text
src/WithWindows.App/          未打包 WinUI 3 可执行项目（Shell/ Views/ ViewModels/ Services/ Controls/ Themes/）
src/WithWindows.Core/         纯逻辑类库（Hotkey/ Config/ Actions/ Interop/ Tools/ Diagnostics/）
tests/WithWindows.Core.Tests/ 纯 xunit，只引用 Core
installer/                    Inno Setup 脚本
docs/agents/                  项目约定分册（见下）
docs/superpowers/             设计文档与实现计划
```

## 分册索引（按需读取）

| 你要改什么 | 先读 |
|---|---|
| 任何 WinUI 3 / XAML 代码 | `docs/agents/winui3-hard-rules.md` |
| 构建、运行、验证界面 | `docs/agents/development-workflow.md` |
| 界面结构与视觉约定 | `docs/agents/ui-conventions.md` |
| **写界面代码前**：参考库与图标名表（WinUI Gallery / PowerToys） | `docs/agents/ui-conventions.md` 第七节 |
| P/Invoke、托盘、注册表、显示拓扑 | `docs/agents/interop-and-platform.md` |
| 遇到奇怪的失败 | `docs/agents/known-pitfalls.md` |
| **新会话接进度**（无对话记忆时先读） | `docs/HANDOFF.md` |

## 提交规范

Conventional Commits，中文描述，祈使句，一次提交只做一件事：

> 换行符：仓库规定 `*.cs` / `*.xaml` / `*.csproj` / `*.props` / `*.md` 用 CRLF，但 AI 写文件默认输出 LF，
> 于是 `git add` 会反复提示「LF will be replaced by CRLF」。跑 `powershell -ExecutionPolicy Bypass -File scripts/normalize-eol.ps1` 对齐即可（索引里始终是 LF，不影响 diff）。

`feat` 新功能 / `fix` 修复 / `docs` 文档 / `refactor` 重构 / `perf` 性能 / `test` 测试 / `chore` 杂务 / `ci` CI

示例：`feat(记事本): 支持字体缩放`、`fix(设置): DisplayArea 校验失败不阻塞窗口打开`

提交信息中**不得出现任何 AI 署名**（`Co-Authored-By`、"Generated with" 等）。本项目不装 git hook，靠本文件约束。

## 完成定义（DoD）

- [ ] `dotnet build -p:Platform=x64` 零错误
- [ ] `dotnet test -p:Platform=x64` 全绿
- [ ] 涉及界面时：已实际启动，`winapp ui inspect` 能看到新增控件
- [ ] 涉及界面时：`winapp ui screenshot` 目视无错乱
- [ ] 新增可交互控件均有 `AutomationProperties.AutomationId`
- [ ] 新增逻辑有对应单元测试
- [ ] README 与 CHANGELOG 已同步
