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
- 最小化、最大化 / 还原、关闭按钮使用系统 caption；亮暗主题变化或在失焦状态切换主题后，按钮前景色必须与当前主题一致，重新激活时再次同步。
- 主导航：`MainNavView`
- 页面入口：`NavItemNotepad`、`NavItemTodo`、`NavItemAi_chat`、`NavItemScheduled`、`NavItemDisplay`、`SettingsItem`
- 关闭窗口和 `Alt+F4` 隐藏到托盘，真正退出只走托盘菜单。

## 页面宽度

除记事页的全宽编辑画布和 AI 对话底部的全宽输入栏外，工具页内容区统一使用 `PageContentMaxWidth`，当前值为 1000 DIP。待办、AI 对话消息区、脚本、切换与设置页在大窗口下居中显示，窄窗口下跟随可用宽度。

## 动效

页面切换使用 `EntranceNavigationTransitionInfo`。待办、对话、脚本和运行记录列表使用 `AddDeleteThemeTransition` 与 `RepositionThemeTransition`。主窗口首次内容使用 `EntranceThemeTransition`。所有程序化 `ContentDialog` 在显示前显式应用 `DefaultContentDialogStyle`，规避当前 WinUI 版本缺失入场动画的回归，并继续使用原生关闭动画；复杂对话框内部表单额外使用 `ChildrenTransitions`。运行日志 `Popup` 使用 `PopupThemeTransition` 统一打开和关闭动画，并使用应用内显式遮罩处理点击外部与 `Esc` 关闭，避免脚本启动前台程序后系统轻触关闭层自动关闭日志。以上动画均遵循系统动效设置。

折叠组件通过应用资源 `ExpanderChevronMargin` 将展开箭头右侧留白统一为 16 DIP，与设置卡片行的右侧控件对齐。

切换页的两个分组使用内置 `Expander` 的默认模板。`SwitchGroupExpanderStyle` 只设置内容区背景、边框、圆角与拉伸对齐，不设置 `ControlTemplate`。标题由原生模板使用 `CardBackgroundFillColorDefaultBrush`，内容使用 `CardBackgroundFillColorSecondaryBrush`，内容边框为 `1,0,1,1`，标题底边形成 1 DIP 接缝；不套外层统一 `Border`。两个分组直接放在同一个 `StackPanel` 中，父级 `SwitchGroupsHost` 使用原生 `RepositionThemeTransition`，让下方分组平滑跟随。不手写 Expander 高度 `Storyboard`、不修改 `Clip`，也不操作模板内部元素。原生展开约 333 ms、收起约 167 ms。

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

运行日志与编辑对话框使用相同宽度上限。运行记录以单列圆角折叠卡片展示，默认展开最近一次，且可同时展开多条；展开后显示一个合并输出区，每行按实际到达顺序显示毫秒时间戳，用蓝色和红色文字区分标准输出与错误输出。弹窗使用固定默认高度，展开或折叠记录时不会发生窗口跳动；展开靠下记录时自动带入可视区域。手动运行结束后自动打开；弹窗使用应用内显式遮罩，可点击空白处或按 `Esc` 关闭。

## 切换与设置

- 切换：`SwitchTitle`、`DisplaySwitchGroup`、`DisplayGroupSubtitle`、`ThemeSwitchGroup`、`ThemeGroupSubtitle`、`DisplayModesTitle`、`ModeCheckBox_*`、`ThemeScheduleToggle`、`ThemeScheduleInfo`、`ThemeWallpaperTitle`、`ThemeWallpaperReset`、`ThemeLightWallpaperName`、`ThemeLightWallpaperButton`、`ThemeDarkWallpaperName`、`ThemeDarkWallpaperButton`、`ThemeWallpaperInfo`
- 设置：`AutoStartToggle`、`DefaultWindowPageComboBox`（记事 / 待办 / AI 对话）、`HotkeyBox_notepad`、`TrayIconStyleCard`、`TrayIconStyleComboBox`（细线闪电 / 黄色图标）
- 关于：`AboutExpander`、`AboutVersion`、`AboutBuildDate`、`AboutRepository`

切换页只保留两个内置 `Expander` 作为唯一一层折叠，默认展开；标题与内容分别是原生模板的两层卡片表面。“显示切换”标题下的副标题显示当前显示拓扑；“主题切换”标题下的副标题显示当前亮暗主题与自动切换状态，定位不可用时显示默认时间范围。标题使用 `CardBackgroundFillColorDefaultBrush`，内容使用 `CardBackgroundFillColorSecondaryBrush`，两层之间保留原生 1 DIP 接缝；不要在外面再套统一卡片或补一层同色背景。显示组内只有候选模式，主题组内只有主题壁纸，均不再提供快捷键行或手动时间选择器。展开后的内容位于透明内容区，`SettingsCardStyle` 只负责内边距，不绘制第三层卡片：左侧 52 DIP 与组标题图标后的文字对齐，右侧 16 DIP 与展开箭头右缘对齐，也与设置页「关于」折叠内容一致；两个分组之间间距 12 DIP；`SwitchGroupsHost` 使用原生 `RepositionThemeTransition` 让兄弟分组平滑跟随，不得手写 Expander 高度动画或套裁切宿主。没有嵌套第二层 `Expander`，也没有第二层小标题或整块大卡片。没有引入 `CommunityToolkit.WinUI.Controls.SettingsControls` 的 `SettingsExpander`，项目依赖白名单未包含该包。

“主题切换”的自动切换 `ToggleSwitch` 放在 `Expander.Header` 的第三列，与标题文字和展开箭头并列。实测在分组展开或收起时点击开关，都只切换开关自身，不会触发展开或收起。组内设置项标题统一使用 `BodyStrongTextBlockStyle`，壁纸文件名等状态文字使用次级小字。内容区与组标题同宽，不再额外设置左右外边距，也不在内容区再套一层卡片。

候选模式位于同一透明内容区，使用原生复选框；复选框行高 36 DIP，候选行之间不另加卡片或分隔线。主题壁纸同样位于透明内容区，使用左右等宽的大预览按钮：亮色和暗色各占一列，图片铺满上半区，模式名称与文件名位于图块底部；未设置时显示“选择壁纸”占位。主题壁纸标题右侧提供“恢复默认”，有任一套自定义壁纸时显示，可一次清除两套壁纸；该链接使用 12 DIP 字号、`MinHeight=0` 和零内边距，不得把标题行撑高。

设置页“托盘图标”卡片左侧使用 `E790` 字形，右侧 `TrayIconStyleComboBox` 提供“细线闪电”“黄色图标”，默认选中细线闪电。闪电字形铺满 ICO 画布，不额外保留安全边距。选择后立即写入 `tray.iconStyle` 并替换托盘图标；闪电样式在亮色主题显示深色闪电，在暗色主题显示白色闪电，系统主题变化后继续跟随。

## 截图

- [记事](../images/notepad.png)
- [待办](../images/todo.png)
- [AI 对话](../images/ai-chat.png)
- [脚本](../images/scheduled-tasks.png)
- [切换](../images/switch.png)
- [设置](../images/settings.png)

新增或重命名 UI Automation 标识时，必须同步更新本文件。
