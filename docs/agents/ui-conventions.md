# 界面约定

## 一、壳层结构

- `MainWindow` 用 `NavigationView` + `Frame` 组成导航壳：左栏是工具列表，底部固定「设置」。
- 内容路由由 `NavigationViewItem.Tag` 驱动，映射表**由工具目录生成**：`ToolCatalog.Tools` 里 `ShowInNav` 的项进左栏，`MainWindow.BuildNavigation` 据此建 `_routes`。新增有页面的工具 = 在 `ToolCatalog` 加一条 + 写一个页面，**不要改 `MainWindow`**。
- 标题栏用 WinUI 3 的 `TitleBar` 控件，左栏折叠按钮交给它，不要自己画一个标题栏。
- 窗口背景用 `MicaBackdrop`（`Window.SystemBackdrop`）。
- 关闭主窗口 = 隐藏到托盘，不销毁窗口；因此不要依赖 `Closed` 事件做清理。

## 二、主题与颜色

- **默认同时支持浅色与深色**，不做单主题。颜色、字体、间距一律走 `ThemeResource` 与系统画刷，禁止硬编码色值。
- 需要强调色时用系统强调色资源，不自己取色。
- 高对比度模式下不得丢失可读性，所以不用自绘背景替代系统画刷。

## 三、控件选择顺序

按这个顺序取第一个能满足需求的：

1. WinUI 3 内置控件与系统资源
2. CommunityToolkit.WinUI 的控件与助手（**不在第三方白名单里，引入前先问**；设置项优先用 `SettingsControls`）
3. 自己写

不要为了视觉定制就替换标准控件的交互；需要定制时，先尝试**组合、模板化或重设样式**内置控件。

## 四、布局

- 分组命令（页内工具栏、格式操作）优先用 `CommandBar` 或其他现成命令面，不要用 `StackPanel` 拼一排按钮。
- 集合类布局要明确滚动归属：页面本身已经纵向滚动时，不要假设嵌套的 `GridView` 还能正确渲染横向海报条。
- 不要给区块、列表、卡片套多余的 `Border` 包裹；子项本身已是卡片时再套一层就是「双卡片」。
- 响应式是「壳 + 页」的问题，不只是控件缩放：要明确宽、中、窄三档下导航形态、内边距、内容密度与页脚的处理。

## 五、图标

- 统一用 `FontIcon` + Segoe Fluent Icons 字形，不引入图片图标，除非是应用图标本身。
- 纯图标按钮必须给 `AutomationProperties.Name`，否则读屏与自动化都定位不到。
- **字形一律从 WinUI Gallery 的图标表里查**，不要凭记忆猜码点：

  ```powershell
  gh api "/repos/microsoft/WinUI-Gallery/contents/WinUIGallery/Samples/Iconography/IconsData.json" --jq '.content' |
    ForEach-Object { [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($_ -replace '\s',''))) } |
    ConvertFrom-Json | Where-Object { $_.Name -like '*Monitor*' } | Format-Table Name, Code, Tags
  ```

  例：投影用的 `DeviceMonitorRightPic` = `E7F9`（tags: projection/present），本项目导航与托盘都用它。

## 六、参考实现（写代码前先看这两处）

用户已明确要求**直接参考现成实现**，不要自己从头试错：

| 参考库 | 看什么 |
|---|---|
| [microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery) | 控件用法与页面排版（设置页、关于页、示例页）；`WinUIGallery/Samples/Iconography/IconsData.json` 是**图标名 → 码点**的权威表；可直接照搬它的 XAML 结构 |
| [microsoft/PowerToys](https://github.com/microsoft/PowerToys) | 设置页形态：`src/settings-ui/Settings.UI/SettingsXAML/` 下的 `SettingsExpander` 卡片行、`Controls/ShortcutControl/`（快捷键控件：按键方块 + 铅笔按钮 + 弹窗录制） |

取代码用 `gh`（已登录）：

```powershell
gh search code --repo microsoft/WinUI-Gallery "关键词"
gh api "/repos/<owner>/<repo>/contents/<path>" --jq '.content'   # base64，解码后即文件内容
```

## 七、像素级细节由人决定

间距微调、配色选择、动效曲线这类视觉判断**不要由 AI 自行拍板**。研究显示模型面对重复 UI 模式时会"按模式补全"而非按像素还原。这类决策请标为「待人工确认」再交付。
