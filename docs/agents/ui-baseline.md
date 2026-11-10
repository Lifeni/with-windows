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
- 页面入口：`NavItemNotepad`、`NavItemTodo`、`NavItemAi_chat`、`NavItemScheduled`、`NavItemDisplay`、`SettingsItem`
- 关闭窗口和 `Alt+F4` 隐藏到托盘，真正退出只走托盘菜单。

## 记事

- 工具栏：`NotepadToolbar`、`NotepadClock`、`NotepadFind`、`NotepadSave`
- 编辑器：`NotepadEditor`
- 状态栏：`NotepadStatusCaret`、`NotepadStatusCount`、`NotepadWrap`、`NotepadStatusZoom`
- 查找替换：`FindPanel`、`FindBox`、`ReplaceBox`、`FindNextButton`、`FindPrevButton`、`ReplaceButton`、`ReplaceAllButton`

编辑区和顶部工具栏均为透明背景，切换页面后编辑器自动获得焦点。

## 待办

- 底部输入栏：`TodoToolbar`
- 输入：`TodoNewInput`
- 列表与摘要：`TodoList`、`TodoSummary`
- 状态提示：`TodoReminderBar`、`TodoErrorBar`、`TodoEmptyState`
- 行操作：`TodoComplete_*`、`TodoReminder_*`、`TodoDelete_*`
- 提醒对话框：`TodoReminderDate`、`TodoReminderTime`、`TodoReminderValidation`

待办输入栏位于页面底部且高度为 48 DIP。输入框保持透明背景、透明边框且不显示焦点边框；按 `Enter` 添加，不显示发送按钮。

提醒对话框直接显示日期与时间选择器，不使用“启用提醒”开关；已有提醒通过次级按钮移除，两个选择器高度均为 40 DIP。

## AI 对话

- 标题：`ChatSessionTitle`、`ChatModelInfo`
- 操作：`ChatNewSessionButton`、`ChatConfigureButton`、`ChatBusyRing`
- 消息：`ChatMessageList`、`ChatEmptyState`、`ChatErrorBar`
- 输入与附件：`ChatInput`、`ChatPendingAttachments`、`ChatRemoveAttachmentButton`
- 发送状态与停止按钮：`ChatReplyStatus`、`ChatReplyStatusText`

空状态使用与待办页一致的 42 DIP 字体图标和说明文字。输入栏使用主题填充色和透明边框，单行文字垂直居中。`Enter` 发送，`Shift+Enter` 换行，输入法组合期间不发送。发送期间输入栏暂时替换为发送状态按钮，显示进度环和“AI 正在思考…” / “AI 正在回复…”，可按 `Esc` 或点击其所在状态条停止，完成或停止后恢复输入栏。

## Lua 脚本

- 页面头部：`ScheduledTaskSummary`、`ScheduledTaskNewButton`
- 列表与空状态：`ScheduledTaskList`、`ScheduledTaskEmptyState`、`ScheduledTaskListItem_<taskId>`
- 行操作：`ScheduledTaskRun_<taskId>`、`ScheduledTaskStop_<taskId>`、`ScheduledTaskHistory_<taskId>`、`ScheduledTaskEdit_<taskId>`、`ScheduledTaskDelete_<taskId>`
- 状态提示：`ScheduledTaskErrorBar`
- 编辑对话框：`ScheduledTaskEditorDialog`、`ScheduledTaskNameBox`、`ScheduledTaskScheduleTypeComboBox`、`ScheduledTaskDatePicker`、`ScheduledTaskTimePicker`、`ScheduledTaskWeekdaySelector`、`ScheduledTaskDayOfMonthBox`
- 脚本与运行：`ScheduledTaskScriptEditor`、`ScheduledTaskTimeoutBox`、`ScheduledTaskEditorValidation`
- 运行日志对话框：`ScheduledTaskHistoryDialog`、`ScheduledTaskRunSummary`、`ScheduledTaskRunHistoryList`、`ScheduledTaskRunItem_<runId>`、`ScheduledTaskRunDetails`、`ScheduledTaskRunMetadata`、`ScheduledTaskOutputTabs`、`ScheduledTaskStdoutTab`、`ScheduledTaskStderrTab`、`ScheduledTaskStdoutOutput`、`ScheduledTaskStderrOutput`

页面以脚本列表为主体，添加和编辑都在独立对话框中完成。标题区与 AI 对话页共用 48 DIP 高度，左上角以 14 DIP 单行文字显示脚本数量。执行方式包含仅手动、单次、每天、每周和每月；应用退出后不再执行计划，关闭窗口并驻留托盘时继续调度。单次模式沿用待办提醒对话框的日期、时间双列样式，两个选择器均为 40 DIP 且宽度一致。代码编辑器使用内置 `RichEditBox`，提供 Lua 基础词法高亮和同步行号；计划设置在编辑器下方。

运行日志对话框与编辑对话框使用相同宽度上限。左侧为运行记录，右侧为选中记录的状态摘要、标准输出和错误输出；打开对话框时默认选中最近一次运行。

## 投影与设置

- 投影：`DisplayModesExpander`、`DisplayCurrentMode`
- 设置：`AutoStartToggle`、`DefaultWindowPageComboBox`（记事 / 待办 / AI 对话）、`HotkeyBox_notepad`
- 关于：`AboutExpander`、`AboutVersion`、`AboutBuildDate`、`AboutRepository`

投影页与设置页的主要选项组之间统一使用 8 DIP 垂直间距。

## 截图

- [记事](../images/notepad.png)
- [待办](../images/todo.png)
- [AI 对话](../images/ai-chat.png)
- [投影](../images/projection.png)

新增或重命名 UI Automation 标识时，必须同步更新本文件。
