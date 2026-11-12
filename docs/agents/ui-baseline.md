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

## 页面宽度

除记事页的全宽编辑画布和 AI 对话底部的全宽输入栏外，工具页内容区统一使用 `PageContentMaxWidth`，当前值为 1000 DIP。待办、AI 对话消息区、脚本、投影与设置页在大窗口下居中显示，窄窗口下跟随可用宽度。

## 动效

页面切换使用 `EntranceNavigationTransitionInfo`。待办、对话、脚本和运行记录列表使用 `AddDeleteThemeTransition` 与 `RepositionThemeTransition`；投影模式列表使用带错峰的原生入场过渡。主窗口首次内容使用 `EntranceThemeTransition`。所有程序化 `ContentDialog` 在显示前显式应用 `DefaultContentDialogStyle`，规避当前 WinUI 版本缺失入场动画的回归，并继续使用原生关闭动画；复杂对话框内部表单额外使用 `ChildrenTransitions`。运行日志 `Popup` 使用 `PopupThemeTransition` 统一打开和关闭动画，并使用应用内显式遮罩处理点击外部与 `Esc` 关闭，避免脚本启动前台程序后系统轻触关闭层自动关闭日志。以上动画均遵循系统动效设置。

折叠组件通过应用资源 `ExpanderChevronMargin` 将展开箭头右侧留白统一为 16 DIP，与设置卡片行的右侧控件对齐。

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
- AI 脚本提案：`ChatScriptProposalDialog`、`ChatScriptProposalSummary`、`ChatScriptProposalPreview`

空状态使用与待办页一致的 42 DIP 字体图标和说明文字。输入栏使用主题填充色和透明边框，单行文字垂直居中。`Enter` 发送，`Shift+Enter` 换行，输入法组合期间不发送。发送期间输入栏暂时替换为发送状态按钮，显示进度环和“AI 正在思考…” / “AI 正在回复…”，可按 `Esc` 或点击其所在状态条停止，完成或停止后恢复输入栏。用户明确要求创建脚本时，模型只能调用 `propose_csharp_script` 生成草案；应用校验脚本与计划后显示预览，用户确认后才写入脚本存储，不会自动运行、覆盖或删除已有脚本。

AI 对话页使用 `NavigationCacheMode=Required`。切换到其他工具页时请求不取消，页面实例、流式内容和发送状态继续保留；关闭主窗口隐藏到托盘时同样继续生成。后台完成的脚本提案只暂存草案，返回 AI 页后再显示确认对话框；只有应用真正退出时才取消进行中的请求。

## 脚本

- 页面头部：`ScheduledTaskSummary`、`ScheduledTaskNewButton`
- 列表与空状态：`ScheduledTaskList`、`ScheduledTaskEmptyState`、`ScheduledTaskListItem_<taskId>`
- 行操作：`ScheduledTaskRun_<taskId>`、`ScheduledTaskStop_<taskId>`、`ScheduledTaskHistory_<taskId>`、`ScheduledTaskEdit_<taskId>`、`ScheduledTaskDelete_<taskId>`
- 状态提示：`ScheduledTaskErrorBar`
- 编辑对话框：`ScheduledTaskEditorDialog`、`ScheduledTaskNameBox`、`ScheduledTaskScheduleTypeComboBox`、`ScheduledTaskDatePicker`、`ScheduledTaskTimePicker`、`ScheduledTaskWeekdaySelector`、`ScheduledTaskDayOfMonthBox`
- 脚本与运行：`ScheduledTaskScriptEditor`、`ScheduledTaskTimeoutBox`、`ScheduledTaskEditorValidation`
- 运行日志：`ScheduledTaskHistoryDialog`、`ScheduledTaskRunHistoryList`、`ScheduledTaskRunItem_<runId>`、`ScheduledTaskRunEmptyState`、`ScheduledTaskRunOutput_<runId>`、`ScheduledTaskRunOutputEmpty_<runId>`

页面以脚本列表为主体，添加和编辑都在独立对话框中完成。标题区与 AI 对话页共用 48 DIP 高度，左上角以 14 DIP 单行文字显示脚本数量。执行方式包含仅手动、单次、每天、每周和每月；应用退出后不再执行计划，关闭窗口并驻留托盘时继续调度。计划区域按模式只显示相关字段：单次为日期与时间双列，每天只显示执行时间，每周在时间下方显示 7 个等宽星期切换按钮，每月为执行时间与每月日期双列。代码编辑器使用内置 `RichEditBox`，提供 C# 基础词法高亮和同步行号；计划设置在编辑器下方。脚本通过隐藏 helper 编译执行，停止或超时只终止 helper，脚本主动启动的程序可以继续运行。

运行日志与编辑对话框使用相同宽度上限。运行记录以单列圆角折叠卡片展示，默认展开最近一次，且可同时展开多条；展开后显示一个合并输出区，每行按实际到达顺序显示毫秒时间戳，用蓝色和红色文字区分标准输出与错误输出。弹窗使用固定默认高度，展开或折叠记录时不会发生窗口跳动；展开靠下记录时自动带入可视区域。手动运行结束后自动打开；窗口使用原生轻触关闭，可点击空白处或按 `Esc` 关闭。

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
