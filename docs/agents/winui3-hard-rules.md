# WinUI 3 硬规则

本文收录**能编译通过但在运行时失败**的坑。这类问题编译器不报错，只能靠规则避免。

## 一、API 代际混淆（最高频）

AI 的训练数据里 UWP / WPF / WinUI 2 的样本远多于 WinUI 3，容易写出"看起来对"的错代码。以下映射必须记牢。

| 禁止（UWP / 旧代） | 正确（WinUI 3） |
|---|---|
| `Windows.UI.Xaml.*` 全部命名空间 | `Microsoft.UI.Xaml.*` |
| `Windows.UI.Colors` | `Microsoft.UI.Colors` |
| `CoreDispatcher` / `Dispatcher.RunAsync` | `DispatcherQueue.TryEnqueue` |
| `MessageDialog`（`Windows.UI.Popups`） | `ContentDialog` |
| `Window.Current` | `App.MainWindow`（本项目在 `App` 上声明的静态属性） |
| `ApplicationView` / `CoreWindow` | `AppWindow` |
| `CoreApplicationViewTitleBar` | `AppWindowTitleBar` |
| 任何 `*GetForCurrentView()` | 对应的 `AppWindow` 属性或 `XamlRoot` |
| `IBackgroundTask` | Windows App SDK 的 AppLifecycle 激活模型 |

窗口与线程的替换细节：

| UWP | WinUI 3 |
|---|---|
| `ApplicationView.TryResizeView()` | `AppWindow.Resize()` |
| `AppWindow.TryCreateAsync()` | `AppWindow.Create()` |
| `CoreDispatcher.RunAsync(priority, cb)` | `DispatcherQueue.TryEnqueue(priority, cb)` |
| `Dispatcher.HasThreadAccess` | `DispatcherQueue.HasThreadAccess` |
| `DisplayInformation.GetForCurrentView()` | `GetDpiForWindow()` 或 `XamlRoot.RasterizationScale` |
| `SystemNavigationManager.GetForCurrentView()` | 直接在 `NavigationView` 里处理返回 |

## 二、运行时才会炸的坑

### `ContentDialog` 必须设置 `XamlRoot`

```csharp
// 错：运行期抛 InvalidOperationException
var dialog = new ContentDialog { Title = "确认", Content = "继续吗？" };
await dialog.ShowAsync();

// 对
var dialog = new ContentDialog
{
    Title = "确认",
    Content = "继续吗？",
    PrimaryButtonText = "是",
    CloseButtonText = "否",
    XamlRoot = this.Content.XamlRoot,   // 必须
};
```

### 文件选择器必须先绑定窗口句柄

```csharp
// 错：直接 new 出来就调用，运行期抛异常
var picker = new FileOpenPicker();
var file = await picker.PickSingleFileAsync();

// 对
var picker = new FileOpenPicker();
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
picker.FileTypeFilter.Add(".txt");
var file = await picker.PickSingleFileAsync();
```

### 不要把 UI 线程当作有 ASTA 保护

UWP 有 ASTA 重入保护，WinUI 3 用的是标准 STA，**没有**这层保护。`async` 期间消息泵会让重入成为可能，涉及共享状态时要自己加保护。

### 绑定路径写错不会编译报错

XAML 绑定路径错误是静默失败——界面空白且没有任何异常。改完绑定必须实际启动并观察，或直接用 `x:Bind`（编译期检查）。

## 三、异步规则

- 禁止 `ConfigureAwait(false)` 出现在 ViewModel：异常会在错误的同步上下文里被静默吞掉，表现为「数据不显示但没有任何错误」
- 禁止 fire-and-forget：不许出现未被 `await` 也未被显式处理的 `async` 调用
- `async void` 仅允许用于事件处理器，且内部必须全包 `try/catch`
- 回到 UI 线程统一用 `DispatcherQueue.TryEnqueue`

## 四、可访问性（不是可选项）

每个可交互控件都必须有稳定标识，否则界面自动化无法定位：

```xml
<Button
    AutomationProperties.AutomationId="SaveButton"
    AutomationProperties.Name="保存"
    Content="保存"
    Command="{x:Bind ViewModel.SaveCommand}" />
```

`AutomationId` 用英文 PascalCase 且保持稳定（改名等于让既有自检脚本失效）；`Name` 用中文，面向读屏软件。
