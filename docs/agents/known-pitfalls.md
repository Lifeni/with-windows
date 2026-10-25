# 已知坑清单

每条都是实际踩过并付出代价的，按原文保留。遇到相似现象先查这里。

## 窗口与 XAML

**托盘收起不要隐藏顶层 WinUI 窗口**：`AppWindow.Hide()` / `AppWindow.Show()` 和 `SW_HIDE` 都可能让当前页面触发 `Unloaded`，再次显示时只恢复顶层窗口，不会把整棵 XAML 内容重新挂载；投影、AI 对话等页面会黑屏，随后伴随 `Microsoft.UI.Xaml.dll` 的 `0xC000027B`。本机还实测到 `OverlappedPresenter.Minimize()` 与 `Restore()` 会直接破坏 XAML 内容树，即使用按钮事件排队执行也无效。当前正常路径是：收起时用 CsWin32 `ShowWindow(SW_MINIMIZE)`，再设 `AppWindow.IsShownInSwitchers = false`；唤回时先设回 `true`，再调用 `ShowWindow(SW_SHOWNOACTIVATE)`，最后 `Activate()` 和 `BringToFront()`。不要移动窗口到屏幕外，也不要用 `SW_HIDE`。

**系统关闭按钮会留下 caption 悬停状态**：窗口使用内置 `TitleBar` 时，点击系统关闭按钮会结束非客户区鼠标跟踪；若在 `AppWindow.Closing` 中取消关闭并立即收起，窗口可能收不到 pointer-exited，唤回后关闭按钮仍是红色。收起仍走 `SW_MINIMIZE`，唤回前先调用 `WindowInterop.ClearCaptionButtonHover` 发送 `WM_NCMOUSEMOVE` / `HTNOWHERE`。该清理对标准标题栏无副作用。

**关闭回调不要直接切换 presenter**：`AppWindow.Closing` 中先设置 `Cancel = true`，再用 `DispatcherQueue.TryEnqueue` 执行收起；事件回调栈内直接改变窗口状态会与 WinUI 消息处理重入。真正退出只由托盘菜单触发。

**UI Automation `InvokePattern` 不适合关闭按钮生命周期回归**：对未激活窗口直接 invoke 导航或关闭按钮，可能产生真实鼠标不会触发的 `Unloaded`。回归黑屏时必须先切换到目标窗口，再用 `winapp ui click` 执行鼠标路径；不要用截图黑屏反推生产用户操作必然失败。

**`ContentDialog` 内部滚动器默认禁用垂直滚动**：WinUI 默认模板的 `ContentScrollViewer` 是 `VerticalScrollBarVisibility="Disabled"`，高表单在矮窗口里会被直接裁切。业务 `Content` 必须再包一层 `ScrollViewer`，按 `XamlRoot.Size.Height` 设置 `MaxHeight`，并订阅 `XamlRoot.Changed` 处理对话框打开期间的窗口缩放。错误提示应放在滚动区外，否则用户停留在表单顶部提交时看不到校验结果。

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

**页面 Unloaded 后禁止继续操作 XAML**：异步请求、图片解码、延迟滚动和窗口激活回调都可能在页面已经卸载后继续执行。此时设置控件属性、调用 `Focus()` 或 `ScrollIntoView()` 会把无效窗口句柄交给 WinUI，表现为黑帧并最终以 `0xC000027B` 退出。所有异步续体必须先检查 `IsLoaded` 和 `XamlRoot is not null`；卸载后只允许保存文件等非 UI 工作。AI 页不再订阅全局 `Window.Activated` 来抢输入焦点。

**页面卸载后不要用 `Frame.Navigate` 抢救**：实测长停用或 UIA 激活后 `Frame` 仍报告 `CurrentSourcePageType`，但页面已经卸载；此时再调用 `Navigate` 会在 `Microsoft.UI.Xaml.dll` 中以 `0xC000027B` 崩溃，WER 前置异常为 `0x80070578`（无效窗口句柄）。所有导航入口都要先检查 `NavFrame.Content is Page { IsLoaded: true, XamlRoot: not null }`，页面失效时只暂存目标并交给恢复计时器；恢复检查必须同时匹配预期页面类型，不能把另一个仍挂载的旧页面当成恢复完成，导航成功后也要基于新代次重新检查目标。仍然失效时启动新进程并传入当前页面；子进程进入 WinUI 启动流程后必须通过命名事件与旧进程握手，旧进程确认握手完成且子进程仍存活，才释放托盘、窗口状态和 `AppHost`，再用 `Environment.Exit(0)` 退出。自动重启最多一次，且只有预期页面稳定挂载后才清空重启预算。不要调用 `Application.Exit()` 处理已经损坏的 XAML 树，也不要继续对旧树导航。

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

**系统通知不要重新引入 `AppNotificationManager`**：Windows App SDK 2.5.1 的未打包自包含组合仍有注册崩溃。待办提醒复用现有 WinUIEx 托盘图标：先枚举当前进程窗口并用 `Shell_NotifyIconGetRect` 找到图标宿主，再通过 `Shell_NotifyIcon(NIF_INFO)` 显示原生横幅。该路径只在主窗口可见且未最小化时调用，隐藏期间到期的项目在窗口重新激活后补发；Windows 11 横幅是短暂的，不保证进入通知中心。

**WinUI 语言资源不受 `SatelliteResourceLanguages` 控制**：`Microsoft.ui.xaml.dll.mui` 等原生资源会随 WindowsAppSDK 一起进入发布目录，`SatelliteResourceLanguages` 只影响 .NET 卫星程序集。要在单文件里真正删掉其它语言，必须在 `_ComputeFilesToBundle` **之前**过滤 `ResolvedFileToPublish`；写成 `BeforeTargets="GenerateSingleFileBundle"` 时，`_ComputeFilesToBundle` 已经把条目搬进 `_FilesToBundle`，过滤不会生效（实测尺寸完全不变）。

**XAML 编译器的报错很含糊**（如 `MSB3073` / `XamlCompiler.exe`）：先退回最简结构确认，检查 `UseWinUI`、`WindowsPackageType` 相关属性与命名空间，不要自创恢复结构。

## 本机环境

**本机 .NET 8 运行时卸载不掉**：它们带 `SystemComponent=1` 且被 Visual Studio 当作组件安装，`winget` / `msiexec` 都返回成功但实际不动。要清只能走 Visual Studio Installer，**不要强删目录**（会破坏 MSI 数据库）。

**编译需要 .NET 10 SDK**：9 的 SDK 不能编译 `net10` 目标框架。
