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
- `chat/current.json`：当前 AI 会话
- `chat/credential.bin`：DPAPI 保护的 API Key
- `log.txt`：运行日志

## 发布

发布由 `src/WithWindows.App/WithWindows.App.csproj` 的版本号驱动。更新版本并同步 `CHANGELOG.md` 后，GitHub Actions 会生成安装器、便携包和校验文件。

本地发布与安装器编译步骤见 [installer/README.md](../installer/README.md)，维护流程见 [development-workflow.md](agents/development-workflow.md)。
