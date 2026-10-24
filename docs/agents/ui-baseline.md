# 界面基线

本文记录当前界面结构与稳定的 UI Automation 标识。更早的逐次验证记录保留在 Git 历史中。

## 基本检查

```powershell
winapp ui inspect -a WithWindows -i
winapp ui screenshot -a WithWindows
```

项目同时支持浅色、深色和高对比度主题。README 截图为 150% 系统缩放下的深色主题。

## 窗口与导航

- 顶层标题栏：`AppTitleBar`、`PaneToggleButton`
- 主导航：`MainNavView`
- 页面入口：`NavItemNotepad`、`NavItemTodo`、`NavItemAi_chat`、`NavItemDisplay`、`SettingsItem`
- 关闭窗口和 `Alt+F4` 隐藏到托盘，真正退出只走托盘菜单。

## 记事

- 工具栏：`NotepadToolbar`、`NotepadClock`、`NotepadFind`、`NotepadSave`
- 编辑器：`NotepadEditor`
- 状态栏：`NotepadStatusCaret`、`NotepadStatusCount`、`NotepadWrap`、`NotepadStatusZoom`
- 查找替换：`FindPanel`、`FindBox`、`ReplaceBox`、`FindNextButton`、`FindPrevButton`、`ReplaceButton`、`ReplaceAllButton`

编辑区和顶部工具栏均为透明背景，切换页面后编辑器自动获得焦点。

## 待办

- 顶部工具栏：`TodoToolbar`
- 输入与发送：`TodoNewInput`、`TodoAddButton`
- 列表与摘要：`TodoList`、`TodoSummary`
- 状态提示：`TodoReminderBar`、`TodoErrorBar`、`TodoEmptyState`
- 行操作：`TodoComplete_*`、`TodoReminder_*`、`TodoDelete_*`

待办工具栏高度为 48 DIP。输入框保持透明背景和透明边框，键盘聚焦时显示系统焦点框；文字与发送图标视觉居中。

## AI 对话

- 标题：`ChatSessionTitle`、`ChatModelInfo`
- 操作：`ChatNewSessionButton`、`ChatConfigureButton`、`ChatBusyRing`
- 消息：`ChatMessageList`、`ChatEmptyState`、`ChatErrorBar`
- 输入与附件：`ChatInput`、`ChatPendingAttachments`、`ChatRemoveAttachmentButton`

输入栏使用主题填充色和透明边框，单行文字垂直居中。`Enter` 发送，`Shift+Enter` 换行，输入法组合期间不发送。

## 投影与设置

- 投影：`DisplayEnabledToggle`、`DisplayModesExpander`、`DisplayCurrentMode`
- 设置：`AutoStartToggle`、`DefaultWindowPageComboBox`（记事 / 待办 / AI 对话）、`HotkeyBox_notepad`
- 关于：`AboutExpander`、`AboutVersion`、`AboutBuildDate`、`AboutRepository`

## 截图

- [记事](../images/notepad.png)
- [待办](../images/todo.png)
- [AI 对话](../images/ai-chat.png)
- [投影](../images/projection.png)

新增或重命名 UI Automation 标识时，必须同步更新本文件。
