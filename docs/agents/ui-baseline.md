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
4. 默认窗口改为 **1100×720 DIP**（> `ExpandedModeThresholdWidth` 的 1008），首启即展开左栏。

实测（本机 150% 缩放）：窗口 1650×1080 物理 = 1100×720 DIP，导航项宽 467（展开态，含文字），`config.windowState` 落盘 `1100×720`。

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
