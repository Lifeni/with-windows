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
