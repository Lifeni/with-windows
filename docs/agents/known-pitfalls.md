# 已知坑清单

每条都是实际踩过并付出代价的，按原文保留。遇到相似现象先查这里。

## 窗口与 XAML

**不要用 `AppWindow.Hide()` / `AppWindow.Show()` 做常驻窗口的托盘收起与唤回**：WinUI 3 在隐藏时可能让当前页面触发 `Unloaded`，但再次 `AppWindow.Show()` 只恢复顶层窗口，不会把页面重新挂到可视树；投影、AI 对话等页面会黑屏，随后伴随 `Microsoft.UI.Xaml.dll` 的 `0xC000027B`。本项目统一走 `WindowInterop.Hide/Show`（CsWin32 `ShowWindow(SW_HIDE/SW_SHOW)`），让 XAML 内容在隐藏期间保持存活。关闭窗口与「显示 / 隐藏窗口」热键都不得绕回 `AppWindow.Hide()`。

**窗口子类化与 WinUI 3 消息泵冲突**：`SetWindowSubclass` 或自绘 `WM_GETMINMAXINFO` 会导致原生崩溃（`0xc000027b`）。已两次验证，一律改用官方 API。

**`DisplayArea.FindAll()` 在窗口打开时可能抛 `InvalidCastException`**：调用处必须 `try/catch`，失败时视为可见、不阻塞窗口打开。

**`AppWindow.PreferredMinimum*` 属性不存在**：最小尺寸在 `OverlappedPresenter` 上，用 `PreferredMinimumWidth` / `PreferredMinimumHeight`。

**`App.UnhandledException` 只能兜底托管异常**：原生崩溃（XAML 层）不经过它，只能靠事件日志排查。

**没有 XAML 设计器**：这不是缺陷而是优点——所有界面都是纯文本，可读、可改、可 diff。UI 改动必须写成可 review 的 XAML diff。

**`UserControl` 没有自动化节点**：把 `AutomationProperties.AutomationId` 设在 `UserControl` 上等于没设——它在 UIA 树里不出现，既搜不到也无法被 `winapp ui focus` / `send-keys --target` 定位。自定义控件若要被自动化与读屏使用，根元素要用可聚焦控件（如 `Button`），或由控件把 ID 转交给内部可聚焦元素。

**`Button` 会把 `PointerPressed` 标记为已处理**：XAML 上的 `PointerPressed="…"` 普通订阅**永远不会被调用**（与 `RichEditBox` 内部 `ScrollViewer` 吞滚轮同源）。要收到这类事件必须 `AddHandler(UIElement.PointerPressedEvent, handler, handledEventsToo: true)`。另外 WinUI 的 `RoutedEventArgs` **没有 `Handled`**（那是 WPF），想阻止冒泡只能在指针阶段拦（`PointerRoutedEventArgs.Handled`）。

**指针点击会引起焦点抖动**：点击可聚焦控件时会先 `LosingFocus` 再 `GettingFocus`。若在 `LosingFocus` 里无条件清状态（如取消热键录制），按键恰好落在空档就会被丢弃，表现为「点了没反应」。判断焦点是否仍在控件内部（`VisualTreeHelper.GetParent` 向上走）再决定是否清状态。

**XAML 里的初值会立刻触发事件**：`ToggleButton` / `AppBarToggleButton` 写 `IsChecked="True"` 会在**解析期**就触发 `Checked`。若该处理器引用了同一 XAML 中**定义在其之后**的元素（此时还是 `null`），会抛 `NullReferenceException`，而 XAML 层把它包装成极具误导性的「`Failed to assign to property 'ToggleButton.IsChecked'`」并导致 `0xC000027B` 启动崩溃。事件处理器里务必对后定义元素做空值保护。

**XAML 崩溃的定位办法**：`0xC000027B` 只有「应用已启动」一行日志时，给 `App` 挂 `UnhandledException` 把 `e.Exception` 写进日志，通常能拿到带行号的 `XamlParseException`（本次就是靠它定位的）。

**中文输入法会吃掉/转换注入的字符**：`winapp ui send-keys ... text=AB1CD` 实测结果是 `AB`、`XYZ789` 变成 `XYZ'7'8'9`——根因是**输入法（IME）当时处于中文态**，注入的数字/符号被输入法拦截或改写，**不是应用缺陷**。做注入输入验证前先切到英文输入法，或只注入字母/中文，否则会把输入法问题误判成应用 bug。

**验证不得污染用户数据**：`%APPDATA%\WithWindows\` 下的草稿、配置是用户的真实数据。用注入文本 / 改字号等方式验证后必须还原（先快照再写回，或按「新内容以原文为前缀」精确截断）；更稳妥的是不碰真实文件。

## 热键与配置

**热键注册失败不抛异常**：`RegisterHotKey` 返回 `false` 并设置 Win32 错误码（`1409` = 已被占用）。必须把失败原因回传设置界面，否则用户只会觉得「按了没反应」。

**新旧实现共用同一把命名互斥体与数据目录**：`Local\WithWindows.SingleInstance` 与 `%APPDATA%\WithWindows\` 是全局资源。过渡期若主检出的旧构建（`src/WithWindows.UI`，net8）还留在后台，新构建一启动就会判定「已有实例」而**静默退出**，且新日志因文件被占用降级为丢弃——表现为「什么都没发生、日志也没新增」。验证前先 `Get-Process -Name WithWindows` 确认没有陈旧实例。

**残留绑定会静默占住按键**：曾出现旧配置里的 `theme` 绑定与 `display_mode` 同绑一个键，`theme` 先注册成功，导致 `display_mode` 永远注册失败，而界面上看不到也改不掉。因此配置加载时要自愈式清理未知绑定，禁用工具时要先注销热键。

**窗口位置 `(0, 0)` 是合法坐标**：不能用 `0` 当「未保存」的哨兵，必须用 `null`。恢复位置前要校验可见性，超出屏幕时移回主屏居中。

## 生命周期与日志

**`Logger` 生命周期等于应用**：由 `App` 持有实例字段，热键回调会跨 `OnLaunched` 使用，禁止用局部 `using` 释放（曾导致崩溃）。

**单实例互斥体必须由 `App` 字段持有到进程退出**：写成 `OnLaunched` 里的局部 `using` 会让第二个实例在常驻期间照样起来。

## 构建与发布

**发布/构建前必须停掉常驻实例**：运行中的 exe 会锁住输出文件，构建报 `MSB3021` / `MSB3027`——这是文件占用，不是代码错误。

**单文件发布时 WASDK 强制要求 `EnableMsixTooling=true`**：`-p:PublishSingleFile=true` 时必须同时开启该属性（csproj 已按条件处理），否则启动即崩。

**WinUI 3 不支持裁剪**：`PublishTrimmed` 必须为 `false`。`PublishSingleFile` 仅支持「未打包 + 自包含」组合。

**审查 / 自检构建输出不要留在项目目录**：`codex exec review` 等工具可能在 `src/WithWindows.App/artifacts/review-build` 留下一整份发布副本；WinUI 的 PRI 资源索引会把其中的 XBF / Assets 当成内容，单文件发布再把整份副本打包进 exe（实测 exe 从约 225 MB 涨到约 380 MB，安装器从约 60 MB 涨到约 96 MB）。`Directory.Build.props` 已用 `DefaultItemExcludes` 排除 `artifacts/**`；临时发布仍应放在 `dist-*` 并在完成后清理。

**WindowsAppSDK 2.5 元包默认带 AI / ML / Search / Widgets**：即使项目不使用这些 API，`Microsoft.WindowsAppSDK` 也会把 ONNX Runtime、DirectML、语义索引等组件带进自包含发布（约 50 MB）。官方在 [WindowsAppSDK#6015](https://github.com/microsoft/WindowsAppSDK/issues/6015) 建议自包含应用直接引用需要的组件包；[#6464](https://github.com/microsoft/WindowsAppSDK/issues/6464) 也记录了相同问题和其他用户的排除做法。本项目的 App 现在直接引用 Base / Foundation / InteractiveExperiences / WinUI / DWrite / Runtime，不再引用元包；升级 WASDK 或改用新组件时要重新跑全页面回归。

**WinUI 语言资源不受 `SatelliteResourceLanguages` 控制**：`Microsoft.ui.xaml.dll.mui` 等原生资源会随 WindowsAppSDK 一起进入发布目录，`SatelliteResourceLanguages` 只影响 .NET 卫星程序集。要在单文件里真正删掉其它语言，必须在 `_ComputeFilesToBundle` **之前**过滤 `ResolvedFileToPublish`；写成 `BeforeTargets="GenerateSingleFileBundle"` 时，`_ComputeFilesToBundle` 已经把条目搬进 `_FilesToBundle`，过滤不会生效（实测尺寸完全不变）。

**XAML 编译器的报错很含糊**（如 `MSB3073` / `XamlCompiler.exe`）：先退回最简结构确认，检查 `UseWinUI`、`WindowsPackageType` 相关属性与命名空间，不要自创恢复结构。

## 本机环境

**本机 .NET 8 运行时卸载不掉**：它们带 `SystemComponent=1` 且被 Visual Studio 当作组件安装，`winget` / `msiexec` 都返回成功但实际不动。要清只能走 Visual Studio Installer，**不要强删目录**（会破坏 MSI 数据库）。

**编译需要 .NET 10 SDK**：9 的 SDK 不能编译 `net10` 目标框架。
