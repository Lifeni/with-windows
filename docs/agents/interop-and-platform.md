# 互操作与平台约定

## 一、P/Invoke 一律走 CsWin32

- 新代码**禁止手写 `DllImport`**。需要新的 Win32 API 时，在 `NativeMethods.txt` 里声明，由 `Microsoft.Windows.CsWin32` 源生成。
- **禁止编辑 CsWin32 生成的代码**，只改声明文件。
- 已封装的调用集中在 `WithWindows.Core/Interop`，禁止散落到各处。
- 例外：若某个 API 无法用 CsWin32 干净表达（例如需要函数指针的窗口类注册），允许保留一处最小手写互操作，但必须在代码注释里写明原因。

## 二、全局热键：必须用独立隐藏消息窗口

**禁止**子类化 WinUI 窗口来接收 `WM_HOTKEY`（`SetWindowSubclass` / 自定义 `WndProc` 挂到 WinUI 窗口句柄上）。本项目已验证两次：这样做会与 WinUI 3 的消息泵冲突，导致原生崩溃 `0xc000027b`。

正确做法：自己注册一个窗口类并 `CreateWindowEx` 出一个**隐藏消息窗口**，只用于接收 `WM_HOTKEY`；窗口过程委托必须由实例字段持有以防 GC 回收。

其他要点：

- `RegisterHotKey` 失败**不抛异常**，返回 `false` 并设置 Win32 错误码。必须把原因回传给界面（`1409` = 已被占用），否则用户只会觉得「按了没反应」。
- 不设默认热键。默认值必然与部分用户的既有快捷键冲突。
- 工具被禁用或热键被清空时，必须先 `UnregisterHotKey` 再刷新，避免残留绑定继续占用按键。

## 三、显示拓扑

- 切换模式用 `SetDisplayConfig` + `SDC_APPLY` 直接应用拓扑，比 `DisplaySwitch.exe` 可靠且可验证结果。
- **判定当前模式**必须用 `QueryDisplayConfig(QDC_DATABASE_CURRENT)` 返回的权威 `DISPLAYCONFIG_TOPOLOGY_ID`，不要用路径数量之类启发式去猜。
- `DISPLAYCONFIG_PATH_INFO` / `DISPLAYCONFIG_MODE_INFO` 的字段布局必须与 `wingdi.h` 完全一致——系统会往这些缓冲区里写入，尺寸不符会越界。用 CsWin32 生成而非手工拼字节数组。
- 目标模式与当前模式相同时返回「无变化」，宿主不重复提示成功。

## 四、托盘图标

- 用 WinUIEx 的 `TrayIcon`。
- **必须由字段持有引用**，否则会被 GC 回收，图标消失且没有任何报错。

## 五、注册表与开机自启

- 开机自启写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，用户级、无需管理员权限。
- 写入的值必须是 exe 全路径**并加引号**，否则路径含空格时启动失败。
- `--tray` 首次启动保留新窗口的初始未显示状态，不要提前调用 `ShowWindow` 最小化、隐藏或恢复窗口状态；这些操作会在 WinUI 启动栈内触发 XAML 原生崩溃。窗口尺寸在用户首次唤出并激活后恢复。
- 读注册表只做只读判断；测试里不得写入真实注册表。

## 六、单实例

- 用命名 Mutex（`Local\WithWindows.SingleInstance`）。
- 互斥体必须由 `App` 的字段持有到进程退出。**禁止**写成 `OnLaunched` 里的局部 `using var`——方法返回即释放，常驻期间第二个实例照样能起来。
- 生产 `AbandonedMutexException`（前一个实例异常退出）时应当接管，而不是拒绝启动。

## 七、未打包应用的能力边界

本项目是未打包应用，**没有包身份**，因此以下能力不可用或需要额外处理：`startupTask` 清单扩展、某些依赖包身份的通知与文件关联。开机自启因此走注册表 Run 键。

反之，未打包**不受** MSIX 的完整性级别限制，需要管理员权限的操作可以按需提权。
