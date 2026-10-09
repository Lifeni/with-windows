# 界面自检基线

本文件记录界面自检的可复现流程与当前基线，供后续 UI 改动对照。**每次改动 UI 后按同样流程复查，并把新的基线追加到文末。**

## 一、可复现流程

前置：应用已构建。

```powershell
# 1. 启动（非阻塞；不要用前台 dotnet run，会挂住终端）
$exe = "src/WithWindows.App/bin/x64/Debug/net10.0-windows10.0.22621.0/win-x64/WithWindows.exe"
$p = Start-Process -FilePath (Resolve-Path $exe).Path -PassThru
Start-Sleep -Seconds 7

# 2. 确认窗口存在
winapp ui list-windows -a WithWindows

# 3. 看交互元素（-i 只列可交互项，depth 自动提到 8）
winapp ui inspect -a WithWindows -i

# 4. 按 AutomationId 精确查找控件
winapp ui search NotepadEditor -a WithWindows

# 5. 切换页面
winapp ui invoke NavItemHotkeys -a WithWindows

# 6. 截图（返回 JSON，含文件路径与尺寸）
winapp ui screenshot -a WithWindows --json

# 7. 结束进程
Stop-Process -Id $p.Id -Force
```

要点：

- `inspect` / `search` / `click` / `invoke` 都需要 `-a <进程名、窗口标题或 PID>`。
- 截图默认落在当前工作目录的 `screenshot.png`，需移到 `artifacts/ui/` 下改名（`artifacts/` 已加入 `.gitignore`）。
- `winapp` 仍是公共预览版，命令可能变动；执行前用 `winapp ui --help` 核对。

## 二、基线（2026-10-09，提交 d69fa20）

窗口：`"With Windows" (window, 1920x997, foreground) [WinUIDesktopWin32WindowClass]`

`winapp ui inspect -a WithWindows -i` 的关键输出：

```text
Window > Pane > Pane > TitleBar
        PART_PaneToggleButton Button "切换导航"
Window > Pane > Pane > Custom > Window > Pane > Group
              NavItemNotepad ListItem "记事本"
              NavItemHotkeys ListItem "快捷键"
              SettingsItem ListItem "设置"
Window > TitleBar
    Minimize-Restore Button "最小化"
    Maximize-Restore Button "最大化"
    Close Button "关闭"
```

页面内容（用 `search` 验证）：

- 启动后默认页为记事本：`NotepadPlaceholder Text "记事本"`
- `invoke NavItemHotkeys` 之后出现：`HotkeysPlaceholder Text "快捷键"`

截图：`artifacts/ui/shell-hotkeys-2026-10-09.png`（1902×988，深色主题）

目视结论：标题栏、左栏导航（记事本 / 快捷键 / 设置置底）、内容区标题均正常渲染，中文无乱码，Mica 背景生效，无布局错乱。

## 三、已知的视觉待确认项

- 导航壳当前为深色主题（跟随系统）。浅色主题尚未单独截图验证，见 spec 第 12 节风险清单。
- 窗口较宽时内容区偏空，属正常（页面尚无内容）。

## 四、修订记录

### 2026-10-09 标题栏修复（提交 73f12ce）

**问题**：窗口同时出现两条标题栏——系统标题栏 + 自定义 `TitleBar` 控件，且自定义标题栏的图标渲染为透明棋盘格。

**原因**：只放了 `TitleBar` 控件，但没在 code-behind 里把内容扩展到标题栏区域，因此系统 chrome 仍然存在。

**修复**（`MainWindow.xaml.cs` 构造函数）：

```csharp
ExtendsContentIntoTitleBar = true;
SetTitleBar(AppTitleBar);
AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
AppWindow.SetIcon("Assets/with-windows.ico");
```

同时把 `TitleBar.IconSource` 从 `with-windows.png` 改为 `with-windows.ico`（PNG 在标题栏里会渲染成透明棋盘格）。

**修复后基线**：`artifacts/ui/shell-titlebar-2026-10-09.png` —— 单条标题栏（汉堡按钮 + 应用图标 + 标题 + 窗口按钮），左栏导航正常，无重复标题。

> 教训：使用 WinUI 3 的 `TitleBar` 控件时，**必须同时**在 code-behind 里设置 `ExtendsContentIntoTitleBar = true` 与 `SetTitleBar(...)`，否则会得到两条标题栏。这条已写入 `docs/agents/winui3-hard-rules.md` 的待补清单。

### 2026-10-09 工具目录驱动导航（提交 `feat(工具): 目录驱动导航与热键分派`）

**改动**：`MainWindow` 不再手写导航项，改由 `ToolCatalog.Tools` 生成（`ShowInNav` 的项进左栏）；无障碍标识规则为 `"NavItem" + 首字母大写(工具 ID)`，因此旧基线里的 `NavItemNotepad` / `NavItemHotkeys` 名称保持不变。

**启动自检**（截图：`artifacts/ui/shell-nav-catalog-2026-10-09.png`，1902×988）：

```text
Window > Pane > Pane > Custom > Window > Pane > Group
      NavItemNotepad ListItem "记事本"
      NavItemHotkeys ListItem "快捷键"
      SettingsItem ListItem "设置"
```

- 默认页：`NotepadPlaceholder Text "记事本"`
- `invoke NavItemHotkeys`（经 `SelectionItemPattern`）后：`HotkeysPlaceholder Text "快捷键"` 出现
- 目视：左栏三项与设置置底正常，内容区无错乱，Mica 背景生效

### 2026-10-09 生命周期服务组装（提交 `feat(宿主): 组装单实例、配置、日志与热键`）

**改动**：`AppHost` 接管单实例、日志（`%APPDATA%\WithWindows\log.txt`）、配置（v1）与热键注册；`App` 用实例字段持有宿主。窗口与导航外观未变。

**启动自检**（截图：`artifacts/ui/shell-apphost-2026-10-09.png`）：

- `--smoke`：退出码 0，日志写入 `冒烟：notepad 未绑定热键` / `display_mode 未绑定热键` / `冒烟完成：配置 2 个工具，成功注册 0 个热键`（未预置默认热键，符合预期）
- 正常启动：窗口 `"With Windows" (1920x997, foreground)` 出现，日志写入 `应用已启动`
- 第二个实例：立即退出（退出码 0），首个实例继续存活——单实例守卫生效
- 首次启动时 `%APPDATA%\WithWindows\config.json` 由旧 v3 重建为 v1（`tools[].hotkey` 全为 `null`），旧内容留在 `config.json.bak`
- 界面：`NavItemNotepad` / `NavItemHotkeys` / `SettingsItem` 与默认页 `NotepadPlaceholder` 均正常

### 2026-10-09 托盘宿主与关闭到托盘（提交 `feat(托盘): 常驻托盘与关闭到托盘`）

**改动**：新增 `TrayHost`（WinUIEx `TrayIcon`，由字段持有防 GC）；关闭窗口改为隐藏——`Window.Closed` 在 WinUI 3 里**不可取消**，必须用 `AppWindow.Closing`（`AppWindowClosingEventArgs.Cancel`）。

**启动自检**（截图：`artifacts/ui/shell-tray-2026-10-09.png`）：

- 启动：窗口 `"With Windows" (1920x997, foreground)` 正常
- `invoke` 关闭按钮 → `list-windows` 变为 `Found 0 windows`，进程仍存活（PID 48280）——**关闭到托盘生效**
- 托盘图标：`HKCU\Control Panel\NotifyIconSettings` 出现本工作树 exe 条目（提示 `With Windows`），说明已注册给外壳
- 日志无异常，仅 `应用已启动`

**注意**：Win11 默认把新出现的托盘图标收进「隐藏的图标」溢出区，需用户手动固定到任务栏；这是系统行为，不是缺陷。

### 2026-10-09 快捷键页与录入控件（提交 `feat(快捷键页): 列表化热键录制与立即执行`）

**改动**：新增 `HotkeyInputBox`（聚焦后捕获组合键，Esc 取消，清除按钮）与快捷键页；行由 `ToolCatalog` 生成，保存即热重载，失败原因就地显示。

**启动自检**（截图：`artifacts/ui/hotkeys-page-2026-10-09.png`，1902×988，深色）：

```text
HotkeyBox_notepad Button (1395,523 329x76)
HotkeyBox_display_mode Button (1395,675 329x76)
HotkeyEnabled_notepad Button "启用" [on]
HotkeyRunNow_notepad Button "立即执行"
```

端到端实测（注入键盘，非模拟）：`send-keys "ctrl+shift+f14" --target HotkeyBox_display_mode --via send-input` → 配置写入 `"hotkey": "Ctrl+Shift+F14"`，日志无注册失败。

**全局热键分发实测**：给记事本录 `Ctrl+Alt+F13` → 切到设置页 → 注入该组合键 → 自动回到记事本页（`NotepadPlaceholder` 出现），证明 `WM_HOTKEY → AppHost.ToolInvoker → HotkeyRouter → ShowAndNavigate` 全链路打通。验证后已把两个热键清空，恢复「不绑定」。

**踩坑**：`UserControl` 在 UIA 里**没有节点**——把 `AutomationProperties.AutomationId` 设在 `UserControl` 上等于没设，且无法被 `focus` / `send-keys --target` 定位。改为根元素使用 `Button`（可聚焦），并在 `Loaded` 时把页面传入的 AutomationId 转交给该按钮。

### 2026-10-09 设置页（提交 `feat(设置页): 自启、投屏候选与关于`）

**改动**：设置页三段——启动（开机自启）、投屏候选模式（四项复选，至少保留一项）、关于（版本 / 配置路径 / 日志路径）。

**启动自检**（截图：`artifacts/ui/settings-page-2026-10-09.png`，1902×988，深色）：

```text
AutoStartToggle Button "开机自启 已开启" [on]
ModeCheckBox_internal CheckBox "仅当前屏幕" [on]
ModeCheckBox_extend CheckBox "扩展模式" [on]
ModeCheckBox_external CheckBox "仅外接屏幕" [off]
ModeCheckBox_clone CheckBox "复制模式" [off]
AboutConfigPath Edit / AboutLogPath Edit
```

实测：

- 取消「扩展模式」→ `displayMode.modes` 变为 `["internal"]`；重新勾选 → 恢复 `["internal", "extend"]`（取消最后一项会回弹）
- 开机自启：开启 → `HKCU\...\Run` 出现本 exe 路径；关闭 → 值被删除
- 关于：版本显示 `0.5.0`（不用带提交哈希的 InformationalVersion），配置与日志路径正确

**说明**：验证前该机器 `Run\WithWindows` 已有值（指向已退役旧构建），测试后已按其**原始内容**写回，未擅自改动用户的启动配置。

### 2026-10-09 记事本页（提交 `feat(记事本页): 编辑、时钟、缩放与草稿`）

**改动**：记事本页 = 标题 + 秒级时钟 + `A−`/`A+` + 置顶开关 + `RichEditBox`；草稿防抖 500 ms 写入 `%APPDATA%\WithWindows\notepad.txt`；字号写入 `windowState.notepadFontSize`。

**启动自检**（截图：`artifacts/ui/notepad-page-2026-10-09.png`，1902×988，深色）：

```text
NotepadFontDecrease Button "缩小字体"
NotepadFontIncrease Button "放大字体"
NotepadPinToggle Button "置顶 关" [off]
NotepadEditor Edit
NotepadClock Text（get-value 取到 09:10:38，2 秒后为 09:10:41，秒级刷新生效）
```

实测：`send-keys --target NotepadEditor --via send-input` 输入文本后 2 秒内草稿文件即更新（防抖写盘生效）；重启应用后 `get-value NotepadEditor` 读回同一内容（草稿恢复生效）。

**踩坑**：

- `ITextParagraphFormat.LineSpacing` 是**只读**的，必须用 `SetLineSpacing(LineSpacingRule.Multiple, 1.05f)`；且载入文本会重置段落格式，只设默认值对**已有内容**不生效，要对选区 `(0, int.MaxValue)` 再应用一次。
- `DispatcherQueueTimer` 在 `Microsoft.UI.Dispatching` 与 `Windows.System` 下同名，同时 `using` 会报 CS0104，需明确别名。

### 2026-10-09 工具箱整体基线（P3/P4 收尾）

三个工具页与托盘全部就位后的整体自检（截图：`artifacts/ui/final-check-2026-10-09.png`，1902×988，深色）：

| 检查 | 结果 |
|---|---|
| 解决方案构建 | 0 错误 0 警告 |
| Core 测试 | 100/100 通过 |
| 记事本页 | `NotepadEditor Edit` 可见，时钟秒级刷新，草稿读回正确 |
| 快捷键页 | `HotkeyBox_notepad` / `HotkeyBox_display_mode` + 启用开关 + 立即执行 |
| 设置页 | `AutoStartToggle` / `ModeCheckBox_*` / `AboutVersion` / `AboutConfigPath` / `AboutLogPath` |
| 托盘 | `NotifyIconSettings` 有本 exe 条目（提示 `With Windows`）；关闭窗口后进程存活 |
| 全局热键 | 录制 → 注册 → 触发 → 路由到对应页面，全链路实测通过 |

### 2026-10-09 独立审查（`codex exec review --base 5f4bb33`）发现与修复

**背景**：六步闭环由实现者自跑，缺独立性。改用 `codex exec review`（独立进程、独立上下文）审查 P3/P4 整批提交。

**发现 4 项，全部核实属实并已修复**（提交 `fix(快捷键页): 修复录制框无法重复录入等问题`）：

| 级别 | 问题 | 处置 |
|---|---|---|
| P2 | 录制框完成一次录入后**无法再次点击重录**——`_capturing` 只在 `GettingFocus` 置 true，而录入成功/Esc 后焦点仍在按钮上，再点不产生焦点变化 | 增加 `BoxButton.Click` 重入入口；Esc 分支的 `Focus` 改到按钮上 |
| P2 | `ShowStartupError` 是 `async void` 且无 `try/catch`，违反 `winui3-hard-rules.md:89`；`ShowAsync` 失败会变成未观测异常直接杀进程 | 整段包 `try/catch`，失败时仍走 `Exit()` |
| P3 | `AppHost.Dispose` 从未被调用，「应用退出」日志与热键/日志/互斥体释放是死代码 | `ExitApplication` 里调用 `App.Host?.Dispose()` |
| P3 | 清除按钮 `AutomationId` 写死，同页多个录制框出现重复 ID | 按实例派生为 `{录制框 ID}Clear` |

**验证**（截图同 `artifacts/ui/final-check-2026-10-09.png`）：

- 清除按钮 ID 已唯一：`HotkeyBox_notepadClear` / `HotkeyBox_display_modeClear`
- 用**真实鼠标点击**（`winapp ui click`，而非 `--target` 聚焦，后者测不到这个 bug）连续两次录入同一个录制框：先 `Ctrl+Shift+F14`、再 `Ctrl+Alt+F13`，两次都写入成功
- 验证后已把热键清空，恢复「不绑定」

### 2026-10-09 第二轮独立审查（`codex exec review --base 5f4bb33`）发现与修复

第二轮审查判定「patch is correct」（无阻塞缺陷），另给 4 条非阻塞项。**逐条实测后**的处理：

| 级别 | 问题 | 实测结论与处置 |
|---|---|---|
| P2 | `App.Host` 在创建窗口之后赋值，页面可能在 `Loaded` 里读到 null | 实测**不复现**（配置字号 24 → UI 报 18pt = 24px，40 → 钳到 32 后报 24pt，说明配置确实被读到）；仍按建议把赋值移到建窗之前，消除时序依赖 |
| P3 | 提示文本 `HotkeyHint` ID 在同页重复 | 属实，改为按实例派生（`HotkeyBox_display_modeHint`，已实测可见） |
| P3 | 点「清除」会连带进入录制态并吞掉下一次按键 | 症状属实但机制与审查所述不同：WinUI 的 `RoutedEventArgs` **没有 `Handled`**，且 `Button` 会把 `PointerPressed` 标记为已处理，**XAML 上的普通订阅收不到**。最终方案见下方「稳定性修复」 |
| P3 | `--smoke` 用局部 `using` 持有 `Logger`/`HotkeyManager`，违反 AGENTS 硬性约束 | 属实，改为由 `App` 实例字段持有 |

### 2026-10-09 录制框稳定性修复（同一批次的收尾）

第二轮修复把「点一下重新录制」从 `Click` 改到 `PointerPressed` 后，**连续三轮重录实测出现 2/3 失败**。加临时追踪日志定位到两个真实原因：

1. **`Button` 会把 `PointerPressed` 标记为已处理**，XAML 上的普通 `PointerPressed="…"` 订阅**永远不会被调用**——重入入口是死代码。必须用 `AddHandler(UIElement.PointerPressedEvent, handler, handledEventsToo: true)`。
2. **指针点击会引起控件内部的焦点抖动**（`LosingFocus` → `GettingFocus`）。`OnLosingFocus` 无条件把 `_capturing` 置 false，若按键正好落在这个空档就被丢弃，表现为「点了没反应」。改为仅在焦点真正离开本控件时才结束录制。

修复后实测（截图：`artifacts/ui/fix-rerecord-2026-10-09.png`）：

- 连续三轮点击同一录制框并录入：`Ctrl+Alt+F13` → `Ctrl+Shift+F14` → `Ctrl+Alt+F13`，三次都写入成功
- 点「清除」：不再出现「请按下组合键…」，随后按键也不会被吞（配置保持为空）
- 提示文本 ID 唯一：`HotkeyBox_display_modeHint`
- 诊断代码已移除，构建 0 错误 0 警告，Core 测试 100/100

### 2026-10-09 第三轮独立审查（`codex exec review --base 5f4bb33`）

判定仍为「patch is correct」（无阻塞缺陷），给 3 条非阻塞项。逐条实测后的处置：

| 级别 | 问题 | 实测结论与处置 |
|---|---|---|
| P2 | `Ctrl+滚轮` 缩放可能收不到滚轮事件（`RichEditBox` 内部 `ScrollViewer` 吞掉） | **短内容实测能收到**（注入滚轮后处理器记录到 delta ±120）；「内容可滚动时是否被吞」未能自动化验证（需要往编辑器灌长文本，会写进用户真实草稿），故仍按建议改为 `AddHandler(..., handledEventsToo: true)` 做防御 |
| P3 | 置顶开关只写不读，切页返回后显示与实际不符 | 属实，已在 `OnLoaded` 按 `presenter.IsAlwaysOnTop` 回读；实测：开启置顶 → 切快捷键页 → 切回，开关仍显示「开」 |
| P3 | 自检文档仍引用已删除的 `NotepadPlaceholder` | 属实，可复现流程里的命令已改为 `NotepadEditor`（`ui-baseline.md` 与 `HANDOFF.md` 各一处）；历史条目保留原标识，因为那是当时的真实记录 |

**过程中的一次数据事故（已还原）**：为验证「可滚动内容是否吞掉滚轮」，我向记事本编辑器灌入 120 行长文本，结果有 13 个换行被写进你的真实草稿 `%APPDATA%\WithWindows\notepad.txt`。已按「原文是追加内容的前缀」精确还原（96 字符，含末尾 3 个 CR），内容与事故前一致。教训已记入 `known-pitfalls.md`：验证不得污染用户数据。

### 2026-10-09 记事本「仅打开即改写草稿」修复

排查上面那次数据事故时发现一个**真实缺陷**：只要打开记事本页（不做任何编辑），退出后草稿文件就会被改写，末尾空行每次 +2。原因是 `RichEditBox` 取回文本时会**规范化行尾**，而 `TextChanged` 是异步触发的——`_loading` 早已复位，于是规范化后的文本被当成「用户编辑」写回磁盘；反复开关会不断累积空行。

**修复**：以「载入完成后编辑器内的实际文本」作为基线（`_editorBaseline`），`TextChanged` 里只有与该基线不同才认为用户真的改了内容。注意不能拿文件内容直接比较——文件与编辑器内的文本天然不同，那样等于没修。

实测：

- 仅打开 → 切页 → 退出：草稿长度保持 96 字符（修复前为 98，且会逐次增长）
- 真正输入一个字符：草稿被写回（长度 98）
- 验证后草稿已还原为原文 96 字符

### 2026-10-09 窗口状态记忆与最小尺寸（提交 `feat(窗口): 尺寸位置记忆与最小尺寸`）

**改动**：新增 `WindowStateStore`——尺寸/位置去抖写回 `config.windowState`，启动时恢复并做可见性校验；最小尺寸用 `OverlappedPresenter.PreferredMinimumWidth/Height`。

实测（`winapp ui list-windows` 与 `GetWindowRect`）：

| 场景 | 结果 |
|---|---|
| 默认启动 | 900×640（配置默认值） |
| 移动/缩放到物理 1100×700 @ (150,120) 后退出 | `windowState` 落盘 `1650×1050 @ (225,180)`（即物理值） |
| 重启 | 恢复为 1650×1050 |
| 写入超屏位置 `(-5000,-5000)` 后重启 | 不再跑到屏幕外（走主屏居中回退） |
| 配置写 300×200 后重启 | 被限制为 **640×480**（最小尺寸生效） |

**注意**：`AppWindow` 的尺寸/位置是**物理像素**，而 DPI 不感知的进程（如普通 PowerShell 调 `GetWindowRect`）读到的是虚拟化坐标（本机 150% 缩放下相差 1.5 倍），排查时别误判成 bug。

**补充修正（默认窗口与 DIP）**：默认窗口最初是 900×640，首启时左栏只剩一个汉堡按钮——因为配置里的宽高被当成**物理像素**，本机 150% 缩放下 1100 物理像素只有约 733 DIP，落在 `NavigationView` 的紧凑区间（641–1008 DIP）。

修法：

1. 配置里的宽高语义改为 **DIP**（与 WinUI 一致），应用前按窗口 DPI 换算成物理像素——同一份配置在不同缩放的屏幕上视觉大小一致；
2. 缩放率用 **CsWin32 的 `GetDpiForWindow`** 取（新增 `Core.Interop.DisplayScale`），**不要用 `XamlRoot.RasterizationScale`**：窗口首次激活、内容尚未完成布局时它返回 1.0，会按 100% 误算；
3. 恢复尺寸改到首个 `Activated` 之后执行；
4. 默认窗口改为 **1100×720 DIP**（当时的理由是让左栏展开；后来改成紧凑图标栏后，这个尺寸按「内容区不局促」保留）。

实测（本机 150% 缩放）：窗口 1650×1080 物理 = 1100×720 DIP，导航项宽 467（展开态，含文字），`config.windowState` 落盘 `1100×720`。

### 2026-10-09 P5 最终审查（`codex exec review --base 080eac8`）与修复

审查结论：应用侧改动自洽，但发布产物有一处阻塞问题。逐条处置：

| 级别 | 问题 | 处置 |
|---|---|---|
| P1 | 便携版只上传裸 exe，`Assets/` 不进 Release → 该下载途径丢托盘与窗口图标（托盘是唤回窗口/退出的唯一入口） | ① 便携版改为打包成 `WithWindows-<tag>-x64-portable.zip`（含 `Assets/`）；② csproj 加 `ApplicationIcon`（资源管理器里显示图标）。**注意：没有运行期图标兜底**——`TrayIcon` 只接受 `.ico`/`.svg`，所以 `Assets` 必须随包分发，缺文件时按下方第 2 条处理 |
| P3 | `installer/README` 把 Assets 分发错误归因于 `CopyToPublishDirectory` | 已改述为「`CopyToPublishDirectory` 单独无效，真正生效的是 `CopyAssetsToPublish` 目标」 |
| P3 | `HANDOFF` 仍写「P5 计划还没写」 | 已改为指向 P5 计划文件 |

### 2026-10-09 导航栏改为紧凑图标栏（提交 `fix(界面): 导航栏改为紧凑图标栏`）

**改动**：`NavigationView` 设 `PaneDisplayMode="LeftCompact"` 且 `IsPaneOpen="False"`——左栏默认只占约 60 px，图标常驻可见（悬停出工具名），点汉堡按钮才展开完整栏；内容区宽度相应变宽。

实测（`winapp ui inspect`，截图 `artifacts/ui/nav-compact-2026-10-09.png`）：

```text
PART_PaneToggleButton Button "切换导航" (242,244 60x42)
NavItemNotepad ListItem "记事本" (246,311 60x54)
NavItemHotkeys ListItem "快捷键" (246,371 60x54)
SettingsItem ListItem "设置" (246,1233 60x54)
```

导航项宽度 60（紧凑态），窗口 1100×720 DIP。

**同日修复（独立审查发现 2 条 P2）**：

1. `OnFirstActivated` 里漏删的 `NavView.IsPaneOpen = true` 会把紧凑栏又展开成全宽，与「默认紧凑」的意图矛盾——已删除，并实测确认首启即为紧凑态。
2. **图标兜底不成立**：WinUIEx 的 `TrayIcon` 只接受 `.ico` / `.svg`，拿 `exe` 路径当兜底无效，缺图标时还可能抛异常导致启动失败。已改为：`AppIcon` 只暴露 `Assets\with-windows.ico` 与其存在性；`TrayHost` 创建失败时标记 `IsAvailable = false` 并写日志，此时**窗口关闭改为真正退出**（没有托盘就没有唤回入口，不能让应用变成无入口的后台进程）。

**关于「便携版会不会缺图标」的实测澄清**（此前我与审查都判断错了）：单文件自包含应用运行时 `AppContext.BaseDirectory` 指向解压目录 `%TEMP%\.net\WithWindows\<hash>\`，且 `Assets\with-windows.ico` **就在那里**——单文件发布会把 `Content` 打进 exe。诊断输出：

```text
诊断：BaseDirectory=C:\Users\LFN\AppData\Local\Temp\.net\WithWindows\BZXpOz-3f4GS\  AppIcon.Exists=True  TrayAvailable=True
```

因此「剥掉 `dist\Assets` 只留 exe」时图标依然可用；publish 目录里那份 `Assets\` 主要给安装器与 CI 校验用。

### 2026-10-09 记事页重构（方案 1：工具条 + 全页编辑器 + 状态栏）

**改动**（提交 `feat(记事页): 改为工具条与状态栏布局`）：

- 名称「记事本」→「**记事**」（`ToolCatalog` 标题；工具 `id` 仍是 `notepad`，配置不变）
- 顶部 `CommandBar`：**保存、复制、粘贴** + 自动换行（默认开）+ 缩小 / 放大 / 重置缩放
- 中间 `RichEditBox` 占满整页；底部状态栏：`行 x，列 y` / `n 个字符` / `缩放 n%`
- **移除**页内标题、时钟、置顶开关、A−/A+ 按钮
- **不做格式化**（纯文本）；**关闭程序不再自动复制到剪贴板**，复制粘贴一律手动

实测（截图 `artifacts/ui/notepad-toolbar-2026-10-09.png`）：

```text
NotepadSave Button "保存" / NotepadCopy "复制" / NotepadPaste "粘贴"
NotepadWrap Button "自动换行" [on] / NotepadZoomOut "缩小" / NotepadZoomIn "放大" / NotepadZoomReset "重置缩放"
NotepadEditor Edit
```

- 状态栏随编辑更新：空文档 `行 1，列 1 / 0 个字符 / 缩放 100%`；输入后 `行 1，列 9 / 8 个字符`；点两次放大后 `缩放 114%`
- 复制/粘贴自证：输入文本 → 点「复制」→ 点「粘贴」，内容原样插回（长度翻倍），说明两者都生效
- 「保存」写盘有效（状态栏显示「（已保存）」，草稿文件同步更新）

### 2026-10-09 启动即崩（0xC000027B）的根因与修复

改完记事页后应用**启动即崩**，退出码 `0xC000027B`（XAML 层原生崩溃），日志只有「应用已启动」。

定位手段有用：给 `App` 挂 `UnhandledException` 并把异常写进日志，拿到真实消息——

```text
Microsoft.UI.Xaml.Markup.XamlParseException:
Failed to assign to property 'Microsoft.UI.Xaml.Controls.Primitives.ToggleButton.IsChecked'. [Line: 58 Position: 17]
```

**根因**：`AppBarToggleButton` 上写 `IsChecked="True"` 会在 **XAML 解析期立刻触发 `Checked` 事件**，而我的处理器引用了在同一 XAML 中**定义在其之后**的 `Editor`——此时它还是 `null`，处理器抛 `NullReferenceException`，XAML 层把它包装成「无法给 IsChecked 赋值」，看起来像属性赋值错误。

**修法**：事件处理器里对后定义的元素做空值保护（`if (Editor is null) return;`）。教训已补进 `known-pitfalls.md`。

### 2026-10-09 记事页第二轮微调（按用户反馈）

**改动**（提交 `fix(记事页): 工具栏收窄并调整状态栏`）：

1. **编辑器去掉边框与聚焦高亮**：模板的 `BorderElement` 在 `Focused` 状态会切到 `TextControlBorderBrushFocused` / `TextControlBorderThemeThicknessFocused`，因此在本页 `Page.Resources` 里把这组键（含 `PointerOver`）覆盖为透明 / 0，并关掉系统焦点虚线（`UseSystemFocusVisuals="False"`）。
2. **工具条按钮只留图标并收窄**：`CommandBar` 的命令容器有 68 DIP 固定最小宽度，`MinWidth` 压不下去（实测仍 102 物理像素），因此**改用自绘工具条**（`Grid` + 36×32 的透明 `Button` + `FontIcon`）。实测按钮宽度 54 物理像素 = 36 DIP。
3. **去掉缩小 / 放大 / 重置缩放三个按钮**（缩放改为只保留 `Ctrl+滚轮`，状态栏仍显示比例）。
4. **自动换行移到状态栏并改纯文字样式**：换成 `ToggleButton`（透明背景 / 无边框），并覆盖 `ToggleButtonBackground*Checked` 等键去掉勾选态实心填充，用**文字颜色**表示开关。
5. **时钟字体加大并在工具条垂直居中**：`FontSize=18`、`FontWeight=SemiBold`、`VerticalAlignment=Center`，放在工具条左侧。

实测（截图 `artifacts/ui/notepad-final-2026-10-09.png`）：

```text
NotepadSave Button "保存" (1485,125 54x48)
NotepadCopy Button "复制" (1542,125 53x48)
NotepadPaste Button "粘贴" (1598,125 54x48)
NotepadWrap Button "自动换行" [on] (372,1068 95x30)   ← 状态栏内
NotepadClock Text "15:52:23"（每秒刷新）
```

**注**：用户反馈「注入输入丢字符」的真因是**中文输入法**（见 `known-pitfalls.md` 已更正），不是应用缺陷。

### 2026-10-09 投屏页与设置页（去掉通用「快捷键」页）

**改动**：

- 左栏第二项「快捷键」→「**投屏**」，页面重写为 `DisplayPage`（`HotkeysPage` 已删除）
- **投屏页**：投屏快捷键行（录制框 + 启用 + 立即执行）、当前模式（只读）、候选模式复选（从设置页迁来）
- **设置页**：保留开机自启；新增「呼出主窗口」快捷键行（绑定 `notepad`，热键唤回主窗口并打开记事）；关于区改为版本 + 「打开配置目录」「打开日志文件」两个按钮（`explorer.exe` 打开）
- 抽出可复用控件 `Controls/HotkeyRow`（标题 + 录制框 + 启用 + 立即执行 + 就地错误提示），投屏页与设置页共用
- 托盘菜单标题改为取自 `ToolCatalog`，避免改名后左栏与托盘叫法不一致

实测：

```text
NavItemNotepad ListItem "记事" / NavItemDisplay ListItem "投屏" / SettingsItem ListItem "设置"
投屏页：HotkeyBox_display_mode / HotkeyEnabled_display_mode / HotkeyRunNow_display_mode / ModeCheckBox_*
        当前模式：扩展模式
设置页：AutoStartToggle / HotkeyBox_notepad / HotkeyEnabled_notepad / HotkeyRunNow_notepad
        OpenConfigFolder / OpenLogFile
```

**修掉一个次序坑**：`HotkeyRow` 在自己的 `Loaded` 里才给子控件赋 `AutomationId`，而子控件的 `Loaded` 已经先跑过，导致录制框在可访问性树里没有 ID。改为在 `HotkeyInputBox` 里用 `RegisterPropertyChangedCallback` 监听 `AutomationId` 变化并即时转交给内部按钮，任何赋值顺序都成立。

### 2026-10-09 卡片化改版（投影页 / 设置页 / 记事状态栏）

**参考**：用户提供的系统记事本设置页截图（左图标 + 标题 + 说明 + 右侧控件，内容区限宽，卡片分行）。

**改动**：

1. **记事状态栏**：文本与开关统一 `VerticalAlignment="Center"`；「自动换行」前加对勾图标（开启显示、关闭隐藏）。
2. **最小窗口**：480×360 DIP（原 640×480）。
3. **设置页**：内容 `MaxWidth=760` 左对齐；分「启动 / 快捷键 / 关于」三组卡片；开机自启与呼出主窗口用卡片行；关于卡片含版本 + 两个路径按钮 + 路径说明。
4. **投影页**（原投屏页）：导航与标题改名「投影」，图标换成显示器样式；新增「启用投影功能」开关卡片；「切换模式」改为 **2×2 大卡片**（图标 + 名称 + 说明 + 选中对勾，选中态高亮）；快捷键行用同一卡片风格。
5. 抽出 `Controls/CardRow`（图标 + 标题 + 说明 + 右侧控件）统一卡片行；`HotkeyRow` 改为基于它实现。

实测（截图 `artifacts/ui/projection-page-2026-10-09.png`、`settings-cards-2026-10-09.png`）：

```text
投影页：DisplayEnabledToggle [on] / HotkeyBox_display_mode / ModeCard_internal [on] / ModeCard_extend [on] / ModeCard_external [off] / ModeCard_clone [off]
设置页：AutoStartToggle [off] / HotkeyBox_notepad / HotkeyRunNow_notepad "打开主窗口" / OpenConfigFolder / OpenLogFile
记事页：NotepadWrap "自动换行" [on] 113x30（含对勾）
```

**顺带修掉审查发现的三处**：

- `HotkeyRow.RunButtonText` 在构造函数里被读取，XAML 赋值晚于它 → 按钮文案退回「立即执行」：改为 setter 即时写入；
- 投影页「立即执行」切换后「当前模式」不刷新：`HotkeyRow` 暴露 `Executed` 事件，页面收到后重读；
- 记事页每次移动光标都整篇取文：拆成「字符数只在文本变化时算」，光标移动只取前缀算行列。

### 2026-10-09 PowerToys 风格收敛（快捷键控件 / 展开组件 / 限宽居中）

**调研**（按用户要求先看现成实现，不自己试错）：从 GitHub 拉取了 PowerToys 的 `ShortcutControl.xaml(.cs)`
（`src/settings-ui/Settings.UI/SettingsXAML/Controls/ShortcutControl/`）。要点：

- 快捷键控件 = **Subtle 按钮**里放 `ItemsControl`（每项一个 `KeyVisual` 按键方块，强调色）+ 无值时占位文案，点击**弹对话框**；
- 对话框 = 说明文字（「快捷方式应以 Windows 键、Ctrl、Alt 或 Shift 开头」）+ 录制区 + 重置 / 清除 + 保存 / 取消。

**改动**：

1. **快捷键控件（PowerToys 式）**：`HotkeyChips`（按键方块）+ 铅笔图标装在次按钮里；点击弹出 `HotkeyDialog`（说明 + 录制区 + 重置 / 清除 + 保存 / 取消）。`HotkeyRow` 里原来的「启用开关」「立即执行」按钮**已移除**（用户要求），只剩标题/说明 + 快捷键按钮 + 注册失败提示。
2. **录制更稳**：`HotkeyInputBox` 自己跟踪按下的修饰键（`HashSet<VirtualKey>`，KeyDown 加入、KeyUp 移除），并与 `GetKeyStateForCurrentThread` 取并集。原因：注入输入在弹窗里读不到线程键状态，只靠它会让组合键录制失效。
3. **投影页「切换模式」**：改为 **`Expander`（默认折叠）+ 原生 `CheckBox`**（每项两行：图标 + 名称 / 说明），当前模式显示在展开组件标题下（原独立的「当前模式」行已合并）。
4. **限宽居中**：设置页与投影页内容 `MaxWidth=1000` + `HorizontalAlignment=Center`。
5. **记事工具条**：新增撤销（E7A7）/ 还原（E7A6）；「保存」改为「**另存为**」（正文本就自动保存，另存为走 `FileSavePicker`，按未打包应用要求先 `InitializeWithWindow`）；自动换行的对勾由「折叠」改为「透明占位」，避免文字位置跳变。
6. **设置页开关**：状态文字（开 / 关）放在开关左侧、开关贴右边缘（原先开关右侧的 OnContent 文案造成右侧留白）。

实测（截图 `artifacts/ui/notepad-undo-2026-10-09.png`、`projection-expander-2026-10-09.png`）：

```text
记事工具条：NotepadUndo "撤销" / NotepadRedo "还原" / NotepadSave "另存为" / NotepadCopy / NotepadPaste
投影页：DisplayModesExpander [collapsed] → 展开后 ModeCheckBox_internal [on] / _extend [on] / _external [off] / _clone [off]
```

**测试分工**：快捷键录制涉及真实键盘与输入法，用户自行做界面验证；自动化只验证结构（控件存在、展开折叠、配置写入）。

### 2026-10-09 浅色主题与窄宽度适配（提交 `fix(界面): 适配浅色主题与窄窗口`）

**硬编码色值检查**：`rg -n "#[0-9A-Fa-f]{6}" src/WithWindows.App` → 无结果（全部走 `ThemeResource`）。

**浅色主题**（截图：`artifacts/ui/light-notepad-2026-10-09.png`、`light-hotkeys-…`、`light-settings-…`；验证后已把系统主题还原为深色）：

目视确认白底深字、开关与勾选态正常、无「白底白字」；导航在 1000 像素宽时已自动折叠为图标。

**窄宽度**（窗口 640×480，截图：`artifacts/ui/narrow-hotkeys-2026-10-09.png`）：

发现并修复一个真实问题：快捷键行原为四列单行布局，窄窗口下「立即执行」被挤出可视区（`winapp ui inspect` 报 `[offscreen]`）。改为按宽度自适应——**宽（≥760 DIP）单行、窄两行**：第一行「工具名 + 启用开关」，第二行「录制框 + 清除 + 立即执行」。

修复后实测同一窗口宽度下：`HotkeyBox_notepad`、`HotkeyBox_notepadClear`、`HotkeyEnabled_notepad`、`HotkeyRunNow_notepad` 全部在可视区内；第二行工具位于垂直视口外（页面本身可滚动，属预期）。

**注意**：窄窗口下 `NavigationView` 处于折叠态，菜单项不在 UIA 树里；自检脚本需先 `invoke PART_PaneToggleButton` 展开左栏再 `invoke NavItem*`。

### 2026-10-09 Inno Setup 安装器（提交 `feat(打包): 新增 Inno Setup 安装器脚本`）

**新增**：`installer/WithWindows.iss`（用户级安装、开始菜单项、可选桌面快捷方式与开机自启、卸载清理）与 `installer/README.md`（编译方法）。

实测（本机 Inno Setup 6.7.3）：

| 步骤 | 结果 |
|---|---|
| `dotnet publish -c Release -p:SelfContained=true -p:PublishSingleFile=true` | `dist\WithWindows.exe` 约 225 MB |
| `ISCC.exe /DAppVersion=0.5.0` | 产出 `WithWindows-0.5.0-x64-setup.exe` 约 59.86 MB |
| 静默安装到临时目录 | 退出码 0；目录含 `WithWindows.exe` 与 `Assets\with-windows.ico` |
| 启动已安装的 exe | 窗口正常、记事本页可编辑、托盘图标注册在安装路径 |
| 静默卸载 | 退出码 0；目录与开始菜单项均被清理 |

**发现的打包缺陷（已修）**：单文件发布**不会**把 `Assets/` 复制到 publish 目录（只靠 `CopyToPublishDirectory` 无效，与旧项目记录的「WASDK publish 不复制 XBF/PRI」同类）。后果是安装后托盘图标与窗口图标丢失。修法：csproj 增加 `CopyAssetsToPublish` 目标，在 `Publish` 之后显式复制 `Assets\**`。

### 2026-10-09 查找面板按系统记事本重做 + 工具条右对齐 + 投影模式图标与缩进

**用户反馈**：工具条里的查找替换 / 另存为要右对齐；查找面板背景色不对、输入框聚焦背景与面板「融为一体」、输入框下方与右侧有大片空白；投影的模式选项要加图标、缩进再多一些、不需要说明文字；关于的图标太大。

**参考实现**：直接用 `winapp ui screenshot` 抓本机系统记事本（`Microsoft.WindowsNotepad 11.2607.14.0`）的查找条，逐像素取样后套用同一套主题资源。

| 取样点 | 系统记事本 | 改动前（本应用） | 改动后 |
|---|---|---|---|
| 面板底色 | `#282828` | `#1C1C1C`（比页面还暗，故「背景色不对」） | `#282828` |
| 输入框（未聚焦） | `#353535` | `#292929` | `#353535` |
| 输入框（聚焦） | — | `#1D1D1D`（≈ 面板底色，即「融为一体」） | `#212121` |

**改动**：

1. **记事工具条**：三列网格（左侧秒级时钟 / 中间格式按钮 / 右侧实用按钮右对齐），右侧只留「查找替换」「另存为」——复制粘贴按钮删除（`Ctrl+C` / `Ctrl+V` 由编辑器原生提供）。
2. **查找面板重做**：底色改 `SolidBackgroundFillColorTertiaryBrush`，边框改 `ControlStrokeColorDefaultBrush`（与系统记事本一致）；输入框不再自定背景，走主题默认填充（未聚焦浅填充、聚焦输入态深色底）；结构对齐系统记事本——默认只有查找行（展开箭头 / 查找框 / 下一个 / 上一个 / 选项 / 关闭），点最左侧箭头展开替换行（替换框 + 替换 / 全部替换）；「区分大小写」收进「选项」菜单；删掉结果文本行，输入框宽度 360 → 300 DIP，面板由约 662×131 DIP 收到约 524×56 DIP，右下不再有大片空白。
3. **投影页模式选项**：加图标（`E7F7` 笔记本 / `E7F9` 双屏拼接 / `E7F4` 电视 / `E7FA` 双屏镜像——原 `E8B2`、`E8C4` 查图标表后确认是 `Movies`、`ShowBcc`，本来就是错码点），去掉说明文字，缩进 32 DIP（展开组件内容区比标题左 16 DIP，净缩进 16 DIP）。
4. **关于图标** 32 → 20 px，与卡片行图标同尺寸。

**顺带修掉的重复**：`Ctrl+F` / `Ctrl+H` 原先打开的是与浮动面板功能重复的 `ContentDialog`（`ShowFindReplaceAsync`），而且它和面板用了同一批 `AutomationId`（`FindBox`、`ReplaceBox`、`FindMatchCase`…），在可访问性树里是重复 ID。现在统一打开浮动面板（`Ctrl+H` 直接展开替换行），对话框、`_lastFindText` / `_lastReplaceText` / `_matchCase` 字段一并删除。

**实测**（截图 `artifacts/ui/final-find-collapsed.png`、`final-find-expanded.png`、`final-display.png`、`after-settings.png`）：

| 检查 | 结果 |
|---|---|
| 构建 / 测试 | 0 错误 0 警告；Core 测试 100/100 |
| 面板配色 | 面板 `#282828`、查找框 `#353535`、聚焦替换框 `#212121`（与系统记事本同位置取样一致） |
| 面板结构 | 折叠：`FindExpandButton` / `FindBox` / `FindNextButton` / `FindPrevButton` / `FindOptionsButton` / `FindCloseButton`；展开后 `ReplaceBox` / `ReplaceButton` / `ReplaceAllButton` 可见 |
| 工具条 | `NotepadFind`、`NotepadSave` 贴右边缘；`NotepadCopy` / `NotepadPaste` 已删除 |
| 投影模式 | `ModeCheckBox_*` x=601、`DisplayModesTitle` x=577（净缩进 16 DIP），无说明文字 |
| 关于图标 | 20×20，与「开机自启」「显示窗口」行图标同尺寸 |

#### 同日追加（用户第二轮反馈）

**投影页「切换模式」标题行加图标**：原先只有标题 + 当前模式，与上面的卡片行不同构；现在改成 `图标 + 标题/当前模式`（字形 `E7F9`，与左栏「投影」项同一个），展开后的**子选项不再带图标**（上一轮加的图标按用户反馈撤掉），只留模式名。

**「关于」改用字体图标**：上一轮把 `Image` 从 32 改到 20 只是「凑尺寸」——应用图标 PNG 是 256 的方图、中间闪电只占一小块，20 DIP 显示时实际墨迹只有约 9×16 DIP，而且 `Stretch` 默认 `Uniform` 居中，看起来又小又偏。改为 `FontIcon` + `E945`（LightningBolt，就是画那枚 PNG 用的同一枚闪电）后尺寸与位置和其它卡片图标完全一致。顺带删掉 `AboutIconImage` 的加载代码与已成死代码的 `OpenPath` / `OnOpenConfigFolderClick` / `OnOpenLogFileClick`。

**又踩一次的坑**：展开组件的头部（`Expander.Header` 里的 Grid）与内容区**都已经**被展开组件自身内缩了 16 DIP，所以给头部 Grid 再写 `Padding="16,12"` 会让图标比上面的卡片行图标右移整整一格（实测卡片图标 x=139、关于图标 x=170）。两处头部都改成 `Padding="0,12"`。

**实测**（截图 `artifacts/ui/check-display-icon.png`、`check-settings-icon.png`，图标墨迹以亮度阈值测量）：

| 检查 | 结果 |
|---|---|
| 设置页图标列 | 开机自启 x=139..166、显示窗口 x=138..167、关于 x=141..163（同行卡片图标同列同尺寸） |
| 投影页图标列 | 启用投影 x=139..166、切换模式 x=138..167、投影快捷键 x=138..167 |
| 子选项缩进 | `ModeCheckBox_*` x=216、标题 `DisplayModesTitle` x=192（净缩进 16 DIP），子选项无图标 |

#### 同日追加（用户第三轮反馈：去掉说明文字 / 行高一致 / 托盘菜单）

**去掉说明文字后暴露的两个问题（都已修）**：

1. **标题不垂直居中**：`CardRow` 的说明 `TextBlock` 默认是 `Visible`，没设说明文字时仍占一行高度，标题被挤到上半区（实测偏上 7.7 DIP）。修法：构造时把 `_description.Visibility` 置为 `Collapsed`（设了文字的行再置回 `Visible`）。
2. **展开框比卡片矮一截**：开关行的 `ToggleSwitch` 约 40 DIP，纯文字行只有 20 DIP，去掉说明后两者天然高低不一。修法：新增应用级资源 `SettingCardMinHeight`（64 DIP），`CardRow` 的根 `Grid` 与两个展开组件的标题 `Grid` 都用它 —— 三处共用同一个值。

实测（窗口 2267×1937，截图 `artifacts/ui/fix4-display-big.png`、`fix4-settings-big.png`，按像素色带量行高）：

| 行 | 高度 |
|---|---|
| 投影·启用功能 / 投影·快捷键 / 投影·切换模式（标题行） | 均 100 px = 66.7 DIP |
| 设置·开机自启 / 设置·显示窗口 / 设置·关于（标题行） | 均 100 px = 66.7 DIP |
| 单行标题墨迹中心与行中心 | 相差 ≤ 2.5 px（约 1.7 DIP，是字体 ascent/descent 的固有偏差，非布局问题） |

**托盘右键菜单对齐 `main`**：选项改为「记事 / 切换投影（分隔线）设置（分隔线）退出」，与 `main` 分支一致；标题仍取自工具目录。**未能自动化验证**：Win11 把新托盘图标收进溢出区（`HKCU\Control Panel\NotifyIconSettings` 里本应用的 `IsPromoted` 为空），溢出窗口是 XAML 岛（`TopLevelWindowForOverflowXamlIsland`），UIA 读不到元素、窗口截图也是空白，因此无法用 `winapp ui` 完成「右键 → 看到菜单」的端到端验证。**结论：这一条需要人工右击托盘图标确认。**

**顺带修掉一个会杀进程的缺陷**：记事页「清除格式」在空选区（只有插入点）时 `ITextRange.Link = ""` 抛 `ArgumentException`，异常没人兜 → 应用直接退出（事件日志 19:56:15 的 `0xC000027B` 就是它，堆栈停在 `NotepadPage.OnClearFormatClick`）。现在空选区直接返回，赋值另外包 try/catch 并写日志。

**未查明的一处**：今天共有三次同签名的原生崩溃（`0xC000027B`，`Microsoft.UI.Xaml.dll` 偏移 `0x3a9dbd`，19:29:34、19:56:15、22:12:31），其中 19:56 已确认是上面那个 bug；19:29 发生在本次改动之前，22:12 发生在记事页空闲时（日志里没有对应的托管异常）。同一偏移更像是 XAML 层「未处理异常」的统一收口点，而不是同一个根因。**待人工确认：22:12 前后是否点过托盘右键菜单 / 其它按钮。** 这也是本次把托盘菜单改动标记为「需人工确认」的原因。

#### 同日追加（第四轮反馈：标题垂直居中）

**两处不同的问题，分别处理**：

1. **「关于」标题明显偏上**（实测墨迹中心比行中心高 9 DIP）：`Expander.Header` 里的 `StackPanel` 没写 `VerticalAlignment`，Grid 的子元素默认 `Stretch`，只剩一行文字时就贴在行的顶部。**修法：补 `VerticalAlignment="Center"`**。（投影页的「切换模式」是两行，早就写了居中，故无此问题。）
2. **卡片标题「稍微偏下」**（实测墨迹中心比行中心低约 1.3–1.7 DIP）：这是中文字形在行盒里的固有偏差（行盒居中了，墨迹不居中）。**修法：标题 `RenderTransform` 上移 2 DIP**（`CardRow` 的标题与「关于」标题都加），用渲染变换而不是内外边距，这样图标与右侧控件的居中完全不受影响。**这是人工像素级微调，不要按「理论居中」改回去。**

实测（截图 `artifacts/ui/title-align-after.png`，同一窗口 910×673；行高与墨迹中心按像素色带量）：

| 行 | 行高 | 图标墨迹中心 − 行中心 | 文字墨迹中心 − 行中心 |
|---|---|---|---|
| 设置·开机自启 | 100 px（66.7 DIP） | +1.5 px | −0.5 px |
| 设置·显示窗口 | 100 px | −1 px | −1 px |
| 设置·关于 | 100 px | 0 px | −1 px |
