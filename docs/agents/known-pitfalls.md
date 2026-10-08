# 已知坑清单

每条都是实际踩过并付出代价的，按原文保留。遇到相似现象先查这里。

## 窗口与 XAML

**窗口子类化与 WinUI 3 消息泵冲突**：`SetWindowSubclass` 或自绘 `WM_GETMINMAXINFO` 会导致原生崩溃（`0xc000027b`）。已两次验证，一律改用官方 API。

**`DisplayArea.FindAll()` 在窗口打开时可能抛 `InvalidCastException`**：调用处必须 `try/catch`，失败时视为可见、不阻塞窗口打开。

**`AppWindow.PreferredMinimum*` 属性不存在**：最小尺寸在 `OverlappedPresenter` 上，用 `PreferredMinimumWidth` / `PreferredMinimumHeight`。

**`App.UnhandledException` 只能兜底托管异常**：原生崩溃（XAML 层）不经过它，只能靠事件日志排查。

**没有 XAML 设计器**：这不是缺陷而是优点——所有界面都是纯文本，可读、可改、可 diff。UI 改动必须写成可 review 的 XAML diff。

## 热键与配置

**热键注册失败不抛异常**：`RegisterHotKey` 返回 `false` 并设置 Win32 错误码（`1409` = 已被占用）。必须把失败原因回传设置界面，否则用户只会觉得「按了没反应」。

**残留绑定会静默占住按键**：曾出现旧配置里的 `theme` 绑定与 `display_mode` 同绑一个键，`theme` 先注册成功，导致 `display_mode` 永远注册失败，而界面上看不到也改不掉。因此配置加载时要自愈式清理未知绑定，禁用工具时要先注销热键。

**窗口位置 `(0, 0)` 是合法坐标**：不能用 `0` 当「未保存」的哨兵，必须用 `null`。恢复位置前要校验可见性，超出屏幕时移回主屏居中。

## 生命周期与日志

**`Logger` 生命周期等于应用**：由 `App` 持有实例字段，热键回调会跨 `OnLaunched` 使用，禁止用局部 `using` 释放（曾导致崩溃）。

**单实例互斥体必须由 `App` 字段持有到进程退出**：写成 `OnLaunched` 里的局部 `using` 会让第二个实例在常驻期间照样起来。

## 构建与发布

**发布/构建前必须停掉常驻实例**：运行中的 exe 会锁住输出文件，构建报 `MSB3021` / `MSB3027`——这是文件占用，不是代码错误。

**单文件发布时 WASDK 强制要求 `EnableMsixTooling=true`**：`-p:PublishSingleFile=true` 时必须同时开启该属性（csproj 已按条件处理），否则启动即崩。

**WinUI 3 不支持裁剪**：`PublishTrimmed` 必须为 `false`。`PublishSingleFile` 仅支持「未打包 + 自包含」组合。

**XAML 编译器的报错很含糊**（如 `MSB3073` / `XamlCompiler.exe`）：先退回最简结构确认，检查 `UseWinUI`、`WindowsPackageType` 相关属性与命名空间，不要自创恢复结构。

## 本机环境

**本机 .NET 8 运行时卸载不掉**：它们带 `SystemComponent=1` 且被 Visual Studio 当作组件安装，`winget` / `msiexec` 都返回成功但实际不动。要清只能走 Visual Studio Installer，**不要强删目录**（会破坏 MSI 数据库）。

**编译需要 .NET 10 SDK**：9 的 SDK 不能编译 `net10` 目标框架。
