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
winapp ui search NotepadPlaceholder -a WithWindows

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
