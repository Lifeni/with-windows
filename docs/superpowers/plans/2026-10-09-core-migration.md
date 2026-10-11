# With Windows 工具箱 · P2 Core 迁移 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把旧实现（`src/WithWindows.UI`）的逻辑内核迁移到 `WithWindows.Core`，接入 CsWin32 生成互操作代码，落地新配置 schema v1，并删除旧项目，为 P3 页面接线准备好全部纯逻辑能力。

**Architecture:** `WithWindows.Core` 是纯逻辑类库（禁止任何 `Microsoft.UI.Xaml` 类型），按 `Hotkeys/`、`Config/`、`Actions/`、`Interop/`、`Diagnostics/`、`App/` 分区。普通 P/Invoke 一律由 CsWin32 从 `Interop/NativeMethods.txt` 生成；热键注册走独立隐藏消息窗口，绝不子类化 WinUI 窗口。测试全部落在 `tests/WithWindows.Core.Tests`（只引用 Core）。

**Tech Stack:** C# 14 / .NET 10 / WinUI 3（本计划不写 UI）/ Microsoft.Windows.CsWin32 / xunit

**Spec:** `docs/superpowers/specs/2026-10-09-winui3-toolbox-design.md`

## Global Constraints

- 目标框架固定 `net10.0-windows10.0.22621.0`；平台固定 x64（`-p:Platform=x64`），RID 固定 `win-x64`
- `WithWindows.Core` 中禁止出现任何 `Microsoft.UI.Xaml` 类型（用 `rg` 验证）
- `PublishTrimmed` 必须为 `false`
- 运行时数据全部位于 `%APPDATA%\WithWindows\`
- 第三方依赖只允许：WindowsAppSDK、WinUIEx、CommunityToolkit.Mvvm、Microsoft.Windows.CsWin32
- 注释与用户可见文案使用中文；中文用中文标点；中英文之间、中文与数字之间加空格
- 提交信息用 Conventional Commits，中文描述，不加任何 AI 署名
- 每条验证命令都要真实执行并看输出，不得凭记忆断言

## 本计划与 HANDOFF 的偏差（已裁定）

HANDOFF 第六节把「托盘宿主」「页面接线」也列在 P2 之下；但设计文档第 11 节的分阶段表把页面归 P3、工具抽象归 P4。**本计划按设计文档执行**：P2 = Core 迁移 + 删除旧项目。托盘、页面接线留给后续 P3/P4 计划。偏差原因：托盘与页面是 UI 工作，需要 P4 的 `ITool`/`ToolRegistry` 抽象先定型，提前做会返工。

## File Structure

| 文件 | 职责 |
|---|---|
| `src/WithWindows.Core/Interop/NativeMethods.txt` | CsWin32 声明清单（唯一互操作入口声明处） |
| `src/WithWindows.Core/Interop/HotkeyInterop.cs` | `HotkeyModifiers` → Win32 `HOT_KEY_MODIFIERS` 映射；注册错误码 → 中文文案 |
| `src/WithWindows.Core/Interop/MessageWindow.cs` | 隐藏消息窗口（`CreateWindowEx` + 自持 `WNDPROC`），只接收 `WM_HOTKEY` |
| `src/WithWindows.Core/Interop/DisplayConfigInterop.cs` | `GetDisplayConfigBufferSizes` / `QueryDisplayConfig` / `SetDisplayConfig` 的薄封装 |
| `src/WithWindows.Core/Hotkeys/HotkeyManager.cs` | 注册/注销/分发全局热键；失败返回错误码不抛异常 |
| `src/WithWindows.Core/Config/AppConfig.cs` | v1 配置模型（`tools[]` / `displayMode` / `windowState`） |
| `src/WithWindows.Core/Config/ConfigStore.cs` | 原子写 + `.bak` 回退 + 自愈清理未知工具 |
| `src/WithWindows.Core/Actions/ActionResult.cs` | 动作结果（`Changed` 语义） |
| `src/WithWindows.Core/Actions/DisplayTopology.cs` | 只读查询当前拓扑（权威 `DISPLAYCONFIG_TOPOLOGY_ID`） |
| `src/WithWindows.Core/Actions/DisplayModeAction.cs` | 应用拓扑 + toggle 目标选择（纯函数） |
| `src/WithWindows.Core/Diagnostics/Logger.cs` | 1 MB 轮转日志 |
| `src/WithWindows.Core/App/SingleInstance.cs` | 命名 Mutex 单实例守卫 |
| `src/WithWindows.Core/App/AutoStart.cs` | `HKCU\...\Run` 开机自启 |
| `src/WithWindows.Core/CoreAssemblyInfo.cs` | `InternalsVisibleTo("WithWindows.Core.Tests")` |

---

### Task 1: CsWin32 接入与互操作层

**Files:**
- Modify: `src/WithWindows.Core/WithWindows.Core.csproj`
- Modify: `src/WithWindows.Core/Diagnostics/AppPaths.cs`（新增 `LogRotatedPath`）
- Create: `src/WithWindows.Core/Interop/NativeMethods.txt`
- Create: `src/WithWindows.Core/Interop/HotkeyInterop.cs`
- Create: `src/WithWindows.Core/Interop/Win32Constants.cs`
- Create: `src/WithWindows.Core/CoreAssemblyInfo.cs`
- Test: `tests/WithWindows.Core.Tests/HotkeyInteropTests.cs`

**Interfaces:**
- Consumes: `WithWindows.Core.Hotkeys.HotkeyModifiers`（已有）
- Produces:
  - CsWin32 生成类 `Windows.Win32.PInvoke`（含 `RegisterHotKey` / `UnregisterHotKey` / `RegisterClassEx` / `CreateWindowEx` / `DefWindowProc` / `DestroyWindow` / `GetModuleHandle` / `GetDisplayConfigBufferSizes` / `QueryDisplayConfig` / `SetDisplayConfig`）
  - `internal static class HotkeyInterop`，成员：
    - `internal const int ErrorHotkeyAlreadyRegistered = 1409;`
    - `internal const int ErrorClassAlreadyExists = 1410;`
    - `internal static HOT_KEY_MODIFIERS ToNativeModifiers(HotkeyModifiers modifiers)`
    - `internal static string DescribeRegistrationError(int win32Error)`
  - `internal static class Win32Constants`（`WM_HOTKEY = 0x0312` 等本项目自用常量）

**注意**：CsWin32 的 `BOOL` 没有隐式转 `bool` 时，用 `.Value != 0` 判定；执行时以编译器实际行为为准，不要凭记忆写。

- [ ] **Step 1: 写 `Interop/NativeMethods.txt`**

```
CreateWindowEx
DefWindowProc
DestroyWindow
GetDisplayConfigBufferSizes
GetModuleHandle
QueryDisplayConfig
RegisterClassEx
RegisterHotKey
SetDisplayConfig
UnregisterHotKey
WNDCLASSEXW
WNDPROC
```

- [ ] **Step 2: 改 `WithWindows.Core.csproj` 引入 CsWin32**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <RootNamespace>WithWindows.Core</RootNamespace>
    <Platforms>x64</Platforms>
    <IsPackable>false</IsPackable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWin32" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="Interop\NativeMethods.txt" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: 验证 CsWin32 真的生成**

Run:
```powershell
dotnet build src/WithWindows.Core/WithWindows.Core.csproj -p:Platform=x64 -v:quiet -clp:ErrorsOnly
rg --files src/WithWindows.Core/obj | rg "\.g\.cs$"
```
Expected: 构建退出码 0；第二条命令列出 CsWin32 生成的 `.g.cs`（含 `PInvoke`）。若没有生成文件，把 `NativeMethods.txt` 移到工程根目录并改用默认扫描再试。

- [ ] **Step 4: 写 `CoreAssemblyInfo.cs`**

```csharp
using System.Runtime.CompilerServices;

// 纯逻辑的决策函数保持 internal，仅对测试工程开放，避免扩大公共 API 面。
[assembly: InternalsVisibleTo("WithWindows.Core.Tests")]
```

- [ ] **Step 5: 写 `Interop/HotkeyInterop.cs`**

```csharp
using Windows.Win32.UI.Input.KeyboardAndMouse;
using WithWindows.Core.Hotkeys;

namespace WithWindows.Core.Interop;

/// <summary>核心层修饰键枚举 → Win32 注册标志的映射，以及注册错误码的中文说明。</summary>
internal static class HotkeyInterop
{
    /// <summary>Win32 错误码：热键已被其他程序占用。</summary>
    internal const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>Win32 错误码：窗口类已注册（重复注册同名类时出现，可容忍）。</summary>
    internal const int ErrorClassAlreadyExists = 1410;

    /// <summary>
    /// 映射为 RegisterHotKey 的标志位。始终附加 MOD_NOREPEAT：
    /// 「按住不重复触发」是注册层的固定策略，不暴露给上层配置。
    /// </summary>
    internal static HOT_KEY_MODIFIERS ToNativeModifiers(HotkeyModifiers modifiers)
    {
        HOT_KEY_MODIFIERS result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= HOT_KEY_MODIFIERS.MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) result |= HOT_KEY_MODIFIERS.MOD_WIN;
        return result | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
    }

    /// <summary>注册错误码 → 中文原因。未知错误码保留原始编号，便于排查。</summary>
    internal static string DescribeRegistrationError(int win32Error) => win32Error switch
    {
        ErrorHotkeyAlreadyRegistered => "热键已被其他程序占用",
        0 => "注册失败：系统未返回错误码",
        _ => $"注册失败（Win32 错误 {win32Error}）",
    };
}
```

- [ ] **Step 6: 写 `Interop/Win32Constants.cs`**

```csharp
namespace WithWindows.Core.Interop;

/// <summary>本项目自用的 Win32 常量（CsWin32 只生成 API 与结构体，不覆盖这类字面量）。</summary>
internal static class Win32Constants
{
    /// <summary>热键消息。</summary>
    internal const uint WM_HOTKEY = 0x0312;
}
```

- [ ] **Step 7: 给 `AppPaths` 补轮转日志路径**

在 `AppPaths` 中新增：

```csharp
    /// <summary>轮转后的历史日志路径（当前日志超过上限时改名到此）。</summary>
    public static string LogRotatedPath { get; } = Path.Combine(DataRoot, "log.1.txt");
```

- [ ] **Step 8: 写失败测试 `HotkeyInteropTests.cs`**

```csharp
using WithWindows.Core.Hotkeys;
using WithWindows.Core.Interop;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace WithWindows.Core.Tests;

public class HotkeyInteropTests
{
    [Fact]
    public void ToNativeModifiers_无修饰键_仍附加_NOREPEAT()
        => Assert.Equal(HOT_KEY_MODIFIERS.MOD_NOREPEAT, HotkeyInterop.ToNativeModifiers(HotkeyModifiers.None));

    [Fact]
    public void ToNativeModifiers_四键全开_映射为对应位()
    {
        HOT_KEY_MODIFIERS expected = HOT_KEY_MODIFIERS.MOD_CONTROL
            | HOT_KEY_MODIFIERS.MOD_ALT
            | HOT_KEY_MODIFIERS.MOD_SHIFT
            | HOT_KEY_MODIFIERS.MOD_WIN
            | HOT_KEY_MODIFIERS.MOD_NOREPEAT;

        HotkeyModifiers all = HotkeyModifiers.Control | HotkeyModifiers.Alt
            | HotkeyModifiers.Shift | HotkeyModifiers.Windows;

        Assert.Equal(expected, HotkeyInterop.ToNativeModifiers(all));
    }

    [Fact]
    public void DescribeRegistrationError_已占用_给出中文原因()
        => Assert.Equal("热键已被其他程序占用", HotkeyInterop.DescribeRegistrationError(1409));

    [Fact]
    public void DescribeRegistrationError_未知码_保留编号()
        => Assert.Contains("87", HotkeyInterop.DescribeRegistrationError(87));
}
```

- [ ] **Step 9: 运行测试（绿）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 全部通过。

- [ ] **Step 10: 确认 Core 仍无 UI 类型**

Run:
```powershell
rg "Microsoft\.UI\.Xaml" src/WithWindows.Core
```
Expected: 无匹配。

- [ ] **Step 11: Commit**

```bash
git add src/WithWindows.Core tests/WithWindows.Core.Tests
git commit -m "feat(互操作): 接入 CsWin32 并新增热键与显示拓扑声明"
```

---

### Task 2: HotkeyManager（隐藏消息窗口 + 注册分发）

**Files:**
- Create: `src/WithWindows.Core/Interop/MessageWindow.cs`
- Create: `src/WithWindows.Core/Hotkeys/HotkeyManager.cs`
- Test: `tests/WithWindows.Core.Tests/HotkeyManagerTests.cs`

**Interfaces:**
- Consumes: Task 1 的 `HotkeyInterop`、`Win32Constants`；已有的 `Hotkey`
- Produces:
  - `internal sealed class MessageWindow : IDisposable`，构造参数 `Action<IntPtr> onHotkey`，属性 `IntPtr Handle`
  - `public sealed class HotkeyManager : IDisposable`：`bool Register(Hotkey hotkey, Action handler, out string? error)`；`void UnregisterAll()`；`void Dispose()`

**实现要点（硬约束）**：窗口过程委托必须由字段持有防 GC；绝不子类化 WinUI 窗口；注册失败返回 `false` + 原因，不抛异常。

- [ ] **Step 1: 写失败测试 `HotkeyManagerTests.cs`**

平台相关只做「注册一个几乎不可能被占用的键，随后立即注销」的瞬时验证，不改变持久状态：

```csharp
using WithWindows.Core.Hotkeys;

namespace WithWindows.Core.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void Register_合法热键_返回成功且无错误()
    {
        using var manager = new HotkeyManager();
        bool ok = manager.Register(new Hotkey(HotkeyModifiers.None, 0x87), () => { }, out string? error);
        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void Register_同一热键重复注册_第二次返回占用原因()
    {
        using var manager = new HotkeyManager();
        var hotkey = new Hotkey(HotkeyModifiers.None, 0x87);
        Assert.True(manager.Register(hotkey, () => { }, out _));
        bool second = manager.Register(hotkey, () => { }, out string? error);
        Assert.False(second);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void UnregisterAll_之后可重新注册()
    {
        using var manager = new HotkeyManager();
        var hotkey = new Hotkey(HotkeyModifiers.None, 0x87);
        Assert.True(manager.Register(hotkey, () => { }, out _));
        manager.UnregisterAll();
        Assert.True(manager.Register(hotkey, () => { }, out _));
    }
}
```

- [ ] **Step 2: 运行测试确认失败（红）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 编译失败（`HotkeyManager` 未定义）。

- [ ] **Step 3: 写 `Interop/MessageWindow.cs`**

```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WithWindows.Core.Interop;

/// <summary>
/// 隐藏消息窗口：只用于接收 WM_HOTKEY 等线程消息。
/// 绝不在 WinUI 窗口上做子类化——项目已验证两次会与 WinUI 3 消息泵冲突并原生崩溃（0xc000027b）。
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const string ClassName = "WithWindows.HotkeyWindow";

    // 防 GC：窗口过程委托必须存活于窗口生命周期
    private readonly WNDPROC _proc;
    private readonly Action<IntPtr> _onHotkey;
    private HWND _hwnd;

    internal MessageWindow(Action<IntPtr> onHotkey)
    {
        _onHotkey = onHotkey;
        _proc = WndProc;

        HINSTANCE instance = PInvoke.GetModuleHandle(null);
        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _proc,
            lpszClassName = ClassName,
            hInstance = instance,
        };
        PInvoke.RegisterClassEx(wndClass); // 重复注册（1410）由系统容忍

        _hwnd = PInvoke.CreateWindowEx(
            0, ClassName, ClassName, 0, 0, 0, 0, 0,
            HWND.Null, null, instance, null);
    }

    internal IntPtr Handle => _hwnd;

    private LRESULT WndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == Win32Constants.WM_HOTKEY)
            _onHotkey(wParam.Value);
        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }
    }
}
```

**执行提示**：CsWin32 生成的确切签名以编译器为准（`CreateWindowEx` 的参数个数、`HWND` 判空方式、`BOOL` 判定方式）。若签名不符，按生成结果就地修正，不要改回手写 `DllImport`。

- [ ] **Step 4: 写 `Hotkeys/HotkeyManager.cs`**

```csharp
using System.Runtime.InteropServices;
using WithWindows.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WithWindows.Core.Hotkeys;

/// <summary>
/// 全局热键注册与分发。内部持有隐藏消息窗口接收 WM_HOTKEY；句柄随窗口销毁由系统注销，
/// <see cref="Dispose"/> 时再显式注销一遍。注册失败返回错误原因，不抛异常。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly List<int> _registeredIds = new();
    private int _nextId = 1;

    public HotkeyManager() => _window = new MessageWindow(OnHotkeyMessage);

    /// <summary>注册热键；失败返回 false 并给出中文原因（1409 = 已被占用）。</summary>
    public bool Register(Hotkey hotkey, Action handler, out string? error)
    {
        int id = _nextId++;
        bool ok = PInvoke.RegisterHotKey(
            new HWND(_window.Handle),
            id,
            HotkeyInterop.ToNativeModifiers(hotkey.Modifiers),
            hotkey.VirtualKey);

        if (!ok)
        {
            error = HotkeyInterop.DescribeRegistrationError(Marshal.GetLastWin32Error());
            return false;
        }

        _handlers[id] = handler;
        _registeredIds.Add(id);
        error = null;
        return true;
    }

    private void OnHotkeyMessage(IntPtr wParam)
    {
        int id = wParam.ToInt32();
        if (_handlers.TryGetValue(id, out Action? handler))
            handler();
    }

    /// <summary>注销全部热键（热重载前调用；再次 Register 即重新注册）。</summary>
    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            PInvoke.UnregisterHotKey(new HWND(_window.Handle), id);
        _registeredIds.Clear();
        _handlers.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }
}
```

- [ ] **Step 5: 运行测试（绿）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 全部通过（F24 若恰被占用会失败，属环境冲突，日志里能看出来）。

- [ ] **Step 6: Commit**

```bash
git add src/WithWindows.Core tests/WithWindows.Core.Tests
git commit -m "feat(热键): 迁移全局热键注册与隐藏消息窗口"
```

---

### Task 3: ConfigStore 与 v1 schema

**Files:**
- Create: `src/WithWindows.Core/Config/AppConfig.cs`
- Create: `src/WithWindows.Core/Config/ConfigStore.cs`
- Test: `tests/WithWindows.Core.Tests/ConfigStoreTests.cs`（新建；不要动旧的 UI.Tests）

**Interfaces:**
- Produces:
  - `public sealed class AppConfig`：`const int CurrentVersion = 1`；`int Version`；`List<ToolBinding> Tools`；`DisplayModeConfig DisplayMode`；`WindowStateConfig WindowState`；`static string[] KnownToolIds`；`static List<ToolBinding> CreateDefaultTools()`
  - `public sealed class ToolBinding`：`string Id`；`bool Enabled`；`string? Hotkey`（`null`/空 = 不绑定）
  - `public sealed class DisplayModeConfig`：`List<string> Modes`（默认 `internal` / `extend`）
  - `public sealed class WindowStateConfig`：`double Width = 900`；`double Height = 640`；`double? X`；`double? Y`；`double NotepadFontSize = 14`
  - `public sealed class ConfigStore`：`ConfigStore(string path)`；`string Path`；`string BackupPath`；`bool EnsureExists(Action<string>? log = null)`；`AppConfig Load()`；`void Save(AppConfig config)`

- [ ] **Step 1: 写失败测试 `ConfigStoreTests.cs`**

```csharp
using WithWindows.Core.Config;

namespace WithWindows.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ww-cfg-" + Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConfigStore NewStore() => new(System.IO.Path.Combine(_dir, "config.json"));

    [Fact]
    public void Load_全新配置_不预置任何热键()
    {
        var store = NewStore();
        store.EnsureExists();
        var config = store.Load();
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.All(config.Tools, t => Assert.True(string.IsNullOrEmpty(t.Hotkey)));
        Assert.Contains(config.Tools, t => t.Id == "notepad");
        Assert.Contains(config.Tools, t => t.Id == "display_mode");
    }

    [Fact]
    public void Load_未知工具_被清理并回写()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 1,
              "tools": [
                { "id": "notepad", "enabled": true, "hotkey": null },
                { "id": "ghost", "enabled": true, "hotkey": "F14" }
              ]
            }
            """);
        var config = store.Load();
        Assert.DoesNotContain(config.Tools, t => t.Id == "ghost");
        Assert.DoesNotContain("ghost", File.ReadAllText(store.Path));
    }

    [Fact]
    public void Load_缺失的已知工具_自动补回()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "version": 1, "tools": [] }""");
        var config = store.Load();
        Assert.Contains(config.Tools, t => t.Id == "notepad");
        Assert.Contains(config.Tools, t => t.Id == "display_mode");
    }

    [Fact]
    public void Load_热键为空串_视为不绑定()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """{ "version": 1, "tools": [ { "id": "notepad", "enabled": true, "hotkey": "" } ] }""");
        var tool = store.Load().Tools.Single(t => t.Id == "notepad");
        Assert.True(string.IsNullOrEmpty(tool.Hotkey));
    }

    [Fact]
    public void Load_旧数组格式_重建为默认配置()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """[ { "hotkey": "F13", "action": "notepad" } ]""");
        var config = store.Load();
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.All(config.Tools, t => Assert.True(string.IsNullOrEmpty(t.Hotkey)));
    }

    [Fact]
    public void Load_版本号不符_重建为默认配置()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "version": 3, "bindings": { "notepad": "F13" } }""");
        var config = store.Load();
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.All(config.Tools, t => Assert.True(string.IsNullOrEmpty(t.Hotkey)));
    }

    [Fact]
    public void Load_位置_0_是合法坐标()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "version": 1, "windowState": { "x": 0, "y": 0 } }""");
        var state = store.Load().WindowState;
        Assert.Equal(0d, state.X);
        Assert.Equal(0d, state.Y);
    }

    [Fact]
    public void Save_保留上一份为备份且不留临时文件()
    {
        var store = NewStore();
        var first = new AppConfig();
        first.Tools.Single(t => t.Id == "notepad").Hotkey = "Ctrl+Alt+N";
        store.Save(first);
        var second = new AppConfig();
        second.Tools.Single(t => t.Id == "notepad").Hotkey = "Ctrl+Alt+M";
        store.Save(second);
        Assert.Equal("Ctrl+Alt+M", store.Load().Tools.Single(t => t.Id == "notepad").Hotkey);
        Assert.True(File.Exists(store.BackupPath));
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void Load_主文件损坏_从备份恢复()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Tools.Single(t => t.Id == "notepad").Hotkey = "Ctrl+Alt+N";
        store.Save(config);
        store.Save(config);
        File.WriteAllText(store.Path, "{ \"version\": 1, ");
        Assert.Equal("Ctrl+Alt+N", store.Load().Tools.Single(t => t.Id == "notepad").Hotkey);
    }

    [Fact]
    public void Load_主文件与备份都损坏_抛异常()
    {
        var store = NewStore();
        store.Save(new AppConfig());
        store.Save(new AppConfig());
        File.WriteAllText(store.Path, "{ 坏");
        File.WriteAllText(store.BackupPath, "{ 也坏");
        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void Save_往返一致()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.DisplayMode.Modes = new List<string> { "internal", "external" };
        config.WindowState.Width = 1024;
        store.Save(config);
        var reloaded = store.Load();
        Assert.Equal(new[] { "internal", "external" }, reloaded.DisplayMode.Modes);
        Assert.Equal(1024d, reloaded.WindowState.Width);
    }
}
```

- [ ] **Step 2: 运行测试确认失败（红）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 编译失败（`ConfigStore` 未定义）。

- [ ] **Step 3: 写 `Config/AppConfig.cs`**

```csharp
namespace WithWindows.Core.Config;

/// <summary>应用配置（v1）：工具绑定 + 显示模式候选 + 窗口状态。不与旧 v3 兼容，加载到旧格式时重建默认值。</summary>
public sealed class AppConfig
{
    /// <summary>当前配置模型版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>当前支持的工具 ID。加载时用于自愈式清理未知条目。</summary>
    public static readonly string[] KnownToolIds = { "notepad", "display_mode" };

    /// <summary>配置模型版本。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>工具绑定列表；每个工具一项，含启用开关与热键（null / 空 = 不绑定）。</summary>
    public List<ToolBinding> Tools { get; set; } = CreateDefaultTools();

    public DisplayModeConfig DisplayMode { get; set; } = new();
    public WindowStateConfig WindowState { get; set; } = new();

    /// <summary>默认工具列表：启用、不预置任何热键。</summary>
    public static List<ToolBinding> CreateDefaultTools() => new()
    {
        new ToolBinding { Id = "notepad" },
        new ToolBinding { Id = "display_mode" },
    };
}

/// <summary>单个工具的绑定：ID、启用开关、热键。热键为空表示不注册任何全局热键。</summary>
public sealed class ToolBinding
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>热键字符串（如 "Ctrl+Shift+F14"）；null 或空表示不绑定。</summary>
    public string? Hotkey { get; set; }
}

/// <summary>屏幕切换参数。</summary>
public sealed class DisplayModeConfig
{
    /// <summary>toggle 循环的候选模式，默认 internal / extend。</summary>
    public List<string> Modes { get; set; } = new() { "internal", "extend" };
}

/// <summary>窗口状态记忆（尺寸/位置/字号）。位置 null = 尚未保存，(0, 0) 是合法坐标。</summary>
public sealed class WindowStateConfig
{
    public double Width { get; set; } = 900;
    public double Height { get; set; } = 640;
    public double? X { get; set; }
    public double? Y { get; set; }
    public double NotepadFontSize { get; set; } = 14;
}
```

- [ ] **Step 4: 写 `Config/ConfigStore.cs`**

```csharp
using System.Text.Json;

namespace WithWindows.Core.Config;

/// <summary>
/// 配置读写（v1）。原子替换（临时文件 + File.Replace）+ 旧内容留 .bak + 主文件损坏回退 + 自愈清理未知工具。
/// 旧 v2 数组格式与旧 v3 对象格式都不兼容：识别到即重建为默认配置并回写。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path { get; }

    /// <summary>备份路径：保存时自动留一份上一次的内容，主文件损坏时可回退。</summary>
    public string BackupPath => Path + ".bak";

    public ConfigStore(string path) => Path = path;

    /// <summary>配置文件不存在时创建默认配置；已存在则不动。返回是否新建。</summary>
    public bool EnsureExists(Action<string>? log = null)
    {
        if (File.Exists(Path)) return false;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        Save(new AppConfig());
        log?.Invoke($"已创建默认配置：{Path}");
        return true;
    }

    /// <summary>加载配置；损坏时回退备份；旧格式或不支持版本重建默认值。</summary>
    public AppConfig Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (FileNotFoundException)
        {
            return new AppConfig();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppConfig();
        }

        AppConfig? config = TryDeserialize(text);
        if (config is null)
        {
            AppConfig? recovered = RecoverFromBackup();
            if (recovered is not null) return recovered;
            throw new InvalidDataException("配置 JSON 解析失败，且没有可用的备份");
        }

        if (Normalize(config))
            Save(config);
        return config;
    }

    /// <summary>解析 v1 配置；旧格式、版本不符或 JSON 损坏都返回 null。</summary>
    private static AppConfig? TryDeserialize(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("version", out var version)) return null;
            if (version.ValueKind != JsonValueKind.Number || version.GetInt32() != AppConfig.CurrentVersion) return null;
            return doc.RootElement.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>自愈：丢弃未知工具、去重、补回缺失的已知工具。返回是否发生了变化。</summary>
    private static bool Normalize(AppConfig config)
    {
        bool changed = false;
        int removed = config.Tools.RemoveAll(t => !AppConfig.KnownToolIds.Contains(t.Id));
        changed |= removed > 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int duplicates = config.Tools.RemoveAll(t => !seen.Add(t.Id));
        changed |= duplicates > 0;

        foreach (string id in AppConfig.KnownToolIds)
        {
            if (config.Tools.All(t => t.Id != id))
            {
                config.Tools.Add(new ToolBinding { Id = id });
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>原子写入：先写同目录临时文件再整体替换，旧内容留到 <see cref="BackupPath"/>。</summary>
    public void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, json);

        try
        {
            if (File.Exists(Path))
                File.Replace(temp, Path, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(temp, Path);
        }
        catch (IOException)
        {
            if (File.Exists(Path))
                File.Copy(Path, BackupPath, overwrite: true);
            File.Copy(temp, Path, overwrite: true);
            File.Delete(temp);
        }
    }

    /// <summary>从 .bak 恢复；解析成功才写回主文件。失败返回 null。</summary>
    private AppConfig? RecoverFromBackup()
    {
        if (!File.Exists(BackupPath)) return null;
        try
        {
            AppConfig? config = TryDeserialize(File.ReadAllText(BackupPath));
            if (config is null) return null;
            Normalize(config);
            File.WriteAllText(Path, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: 运行测试（绿）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 全部通过。

- [ ] **Step 6: Commit**

```bash
git add src/WithWindows.Core tests/WithWindows.Core.Tests
git commit -m "feat(配置): 落地配置 schema v1 与自愈式清理"
```

---

### Task 4: 显示拓扑与切换动作

**Files:**
- Create: `src/WithWindows.Core/Actions/ActionResult.cs`
- Create: `src/WithWindows.Core/Interop/DisplayConfigInterop.cs`
- Create: `src/WithWindows.Core/Actions/DisplayTopology.cs`
- Create: `src/WithWindows.Core/Actions/DisplayModeAction.cs`
- Test: `tests/WithWindows.Core.Tests/DisplayModeActionTests.cs`
- Test: `tests/WithWindows.Core.Tests/DisplayTopologyTests.cs`

**Interfaces:**
- Consumes: Task 1 的 CsWin32 声明
- Produces:
  - `public readonly record struct ActionResult(bool Changed, string Message)`
  - `public static class DisplayTopology`：`public static string? GetCurrentMode()`；`internal static string? TopologyName(uint topologyId)`
  - `public sealed class DisplayModeAction`：`DisplayModeAction(string[]? toggleModes = null)`；`public ActionResult Execute(string mode)`；`internal static (string Target, bool IsChange) Decide(string? current, string requested, string[] toggleModes)`；`internal static string PickToggleTarget(string? current, string[] toggleModes)`；`internal static uint TopologyFor(string mode)`

- [ ] **Step 1: 写失败测试（红）**

`DisplayModeActionTests.cs`：

```csharp
using WithWindows.Core.Actions;

namespace WithWindows.Core.Tests;

public class DisplayModeActionTests
{
    [Theory]
    [InlineData("internal", 0x00000001u)]
    [InlineData("clone", 0x00000002u)]
    [InlineData("extend", 0x00000004u)]
    [InlineData("external", 0x00000008u)]
    public void TopologyFor_已知模式_映射为标志(string mode, uint expectedFlag)
        => Assert.Equal(expectedFlag, DisplayModeAction.TopologyFor(mode));

    [Fact]
    public void TopologyFor_未知模式_抛异常()
        => Assert.Throws<ArgumentException>(() => DisplayModeAction.TopologyFor("triple"));

    [Fact]
    public void Execute_未知模式_在改动显示状态前抛异常()
        => Assert.Throws<ArgumentException>(() => new DisplayModeAction().Execute("triple"));

    [Theory]
    [InlineData("internal", "internal", false)]
    [InlineData("extend", "internal", true)]
    [InlineData("internal", "extend", true)]
    public void Decide_显式模式_与当前比较(string current, string requested, bool expectedChange)
    {
        var (target, isChange) = DisplayModeAction.Decide(current, requested, Array.Empty<string>());
        Assert.Equal(requested, target);
        Assert.Equal(expectedChange, isChange);
    }

    [Fact]
    public void Decide_当前拓扑未知_保守执行切换()
    {
        var (target, isChange) = DisplayModeAction.Decide(null, "internal", Array.Empty<string>());
        Assert.Equal("internal", target);
        Assert.True(isChange);
    }

    [Theory]
    [InlineData("internal", "extend")]
    [InlineData("extend", "internal")]
    public void PickToggleTarget_两模式循环(string current, string expected)
        => Assert.Equal(expected, DisplayModeAction.PickToggleTarget(current, new[] { "internal", "extend" }));

    [Fact]
    public void PickToggleTarget_三模式按序循环()
    {
        string[] modes = { "internal", "clone", "extend" };
        Assert.Equal("clone", DisplayModeAction.PickToggleTarget("internal", modes));
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget("extend", modes));
    }

    [Fact]
    public void PickToggleTarget_当前不在列表_回退第一项()
        => Assert.Equal("internal", DisplayModeAction.PickToggleTarget("clone", new[] { "internal", "extend" }));

    [Fact]
    public void PickToggleTarget_当前未知_返回第一项()
        => Assert.Equal("internal", DisplayModeAction.PickToggleTarget(null, new[] { "internal", "extend" }));

    [Fact]
    public void PickToggleTarget_候选为空_默认内部与扩展()
    {
        Assert.Equal("extend", DisplayModeAction.PickToggleTarget("internal", Array.Empty<string>()));
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget(null, Array.Empty<string>()));
    }

    [Fact]
    public void Decide_toggle_解析目标与变化()
    {
        var (target, isChange) = DisplayModeAction.Decide("internal", "toggle", new[] { "internal", "extend" });
        Assert.Equal("extend", target);
        Assert.True(isChange);
    }
}
```

`DisplayTopologyTests.cs`：

```csharp
using WithWindows.Core.Actions;

namespace WithWindows.Core.Tests;

public class DisplayTopologyTests
{
    [Theory]
    [InlineData(1u, "internal")]
    [InlineData(2u, "clone")]
    [InlineData(4u, "extend")]
    [InlineData(8u, "external")]
    [InlineData(0u, null)]
    [InlineData(3u, null)]
    public void TopologyName_映射已知编号(uint id, string? expected)
        => Assert.Equal(expected, DisplayTopology.TopologyName(id));

    [Fact]
    public void GetCurrentMode_返回合法拓扑名或_null()
    {
        string? mode = DisplayTopology.GetCurrentMode();
        Assert.True(mode is null or "internal" or "clone" or "extend" or "external", $"实际值：{mode ?? "<null>"}");
    }
}
```

- [ ] **Step 2: 运行测试确认失败（红）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 编译失败（`DisplayModeAction` 未定义）。

- [ ] **Step 3: 写 `Actions/ActionResult.cs`**

```csharp
namespace WithWindows.Core.Actions;

/// <summary>
/// 动作执行结果。<see cref="Changed"/> 为 false 表示请求的目标状态与当前一致，
/// 没有实际变化——宿主只记录日志，不重复提示成功。
/// </summary>
public readonly record struct ActionResult(bool Changed, string Message);
```

- [ ] **Step 4: 写 `Interop/DisplayConfigInterop.cs`**

```csharp
using Windows.Win32;
using Windows.Win32.Devices.Display;

namespace WithWindows.Core.Interop;

/// <summary>显示配置（CCD）API 的薄封装，只做缓冲区管理与错误码转换，不做业务判断。</summary>
internal static class DisplayConfigInterop
{
    /// <summary>读取当前拓扑编号；失败返回 null。</summary>
    internal static uint? QueryCurrentTopology()
    {
        try
        {
            if (PInvoke.GetDisplayConfigBufferSizes(
                    QUERY_DISPLAY_CONFIG_FLAGS.QDC_DATABASE_CURRENT,
                    out uint numPath, out uint numMode) != 0)
                return null;
            if (numPath == 0)
                return null;

            var paths = new DISPLAYCONFIG_PATH_INFO[numPath];
            var modes = new DISPLAYCONFIG_MODE_INFO[numMode];
            var status = PInvoke.QueryDisplayConfig(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_DATABASE_CURRENT,
                ref numPath, paths,
                ref numMode, modes,
                out DISPLAYCONFIG_TOPOLOGY_ID topology);

            return status == 0 ? (uint)topology : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按拓扑标志应用显示配置；失败抛 <see cref="InvalidOperationException"/>。</summary>
    internal static void ApplyTopology(SET_DISPLAY_CONFIG_FLAGS topology)
    {
        int hr = PInvoke.SetDisplayConfig(0, null, 0, null, topology | SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY);
        if (hr != 0)
            throw new InvalidOperationException($"SetDisplayConfig 失败（Win32 错误 {hr}）");
    }
}
```

**执行提示**：CsWin32 生成的 `SetDisplayConfig` 参数若要求 `Span<DISPLAYCONFIG_PATH_INFO>`，就传 `Span<DISPLAYCONFIG_PATH_INFO>.Empty`；以编译器为准。

- [ ] **Step 5: 写 `Actions/DisplayTopology.cs`**

```csharp
using WithWindows.Core.Interop;

namespace WithWindows.Core.Actions;

/// <summary>
/// 只读查询当前显示拓扑（不修改显示状态）。
/// 用 QDC_DATABASE_CURRENT 返回的权威 DISPLAYCONFIG_TOPOLOGY_ID 判定，不用路径启发式。
/// </summary>
public static class DisplayTopology
{
    /// <summary>返回当前拓扑名："internal" | "clone" | "extend" | "external"；无法判定时返回 null。</summary>
    public static string? GetCurrentMode()
        => DisplayConfigInterop.QueryCurrentTopology() is uint id ? TopologyName(id) : null;

    /// <summary>拓扑编号 → 模式名；未知值返回 null。纯函数，便于测试。</summary>
    internal static string? TopologyName(uint topologyId) => topologyId switch
    {
        1 => "internal",
        2 => "clone",
        4 => "extend",
        8 => "external",
        _ => null,
    };
}
```

- [ ] **Step 6: 写 `Actions/DisplayModeAction.cs`**

```csharp
using WithWindows.Core.Interop;
using Windows.Win32.Devices.Display;

namespace WithWindows.Core.Actions;

/// <summary>
/// 切换投影模式，基于 SetDisplayConfig 直接应用拓扑。mode 取值：internal / extend / external / clone，
/// 以及 toggle：在候选模式（配置 displayMode.modes，默认 internal/extend）中循环。
/// 目标模式与当前一致时返回 Changed=false。
/// </summary>
public sealed class DisplayModeAction
{
    private readonly string[] _toggleModes;

    /// <summary>构造时注入 toggle 候选模式（来自配置 displayMode.modes；空数组 = 默认 internal/extend）。</summary>
    public DisplayModeAction(string[]? toggleModes = null) => _toggleModes = toggleModes ?? Array.Empty<string>();

    /// <summary>执行切换；mode 为 internal / extend / external / clone / toggle（大小写不敏感）。非法值抛 ArgumentException。</summary>
    public ActionResult Execute(string mode)
    {
        string requested = mode.Trim().ToLowerInvariant();
        if (requested is not ("internal" or "extend" or "external" or "clone" or "toggle"))
            throw new ArgumentException($"未知显示模式“{requested}”（支持 internal|extend|external|clone|toggle）");

        var (target, isChange) = Decide(DisplayTopology.GetCurrentMode(), requested, _toggleModes);
        if (!isChange)
            return new ActionResult(false, $"当前已是{ModeDisplayName(target)}");

        DisplayConfigInterop.ApplyTopology((SET_DISPLAY_CONFIG_FLAGS)TopologyFor(target));
        return new ActionResult(true, $"已切换显示模式：{ModeDisplayName(target)}");
    }

    /// <summary>决定目标模式与是否真正变化。纯函数，便于测试。</summary>
    internal static (string Target, bool IsChange) Decide(string? current, string requested, string[] toggleModes)
    {
        string target = requested == "toggle" ? PickToggleTarget(current, toggleModes) : requested;
        // 当前拓扑无法判定（null）时保守执行切换，不阻塞用户操作
        return (target, current is null || target != current);
    }

    /// <summary>toggle 目标选择：当前模式在候选列表中取下一个（循环），否则取第一个。</summary>
    internal static string PickToggleTarget(string? current, string[] toggleModes)
    {
        string[] modes = toggleModes.Length > 0 ? toggleModes : new[] { "internal", "extend" };
        if (current is null) return modes[0];
        int index = Array.IndexOf(modes, current);
        return index >= 0 ? modes[(index + 1) % modes.Length] : modes[0];
    }

    /// <summary>模式名 → SDC_TOPOLOGY_* 标志数值。供测试断言。</summary>
    internal static uint TopologyFor(string mode) => mode switch
    {
        "internal" => 0x00000001,
        "clone" => 0x00000002,
        "extend" => 0x00000004,
        "external" => 0x00000008,
        _ => throw new ArgumentException($"未知显示模式“{mode}”（支持 internal|extend|external|clone）"),
    };

    private static string ModeDisplayName(string mode) => mode switch
    {
        "internal" => "仅当前屏幕",
        "extend" => "扩展模式",
        "external" => "仅外接屏幕",
        _ => "复制模式",
    };
}
```

- [ ] **Step 7: 运行测试（绿）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 全部通过。

- [ ] **Step 8: Commit**

```bash
git add src/WithWindows.Core tests/WithWindows.Core.Tests
git commit -m "feat(投屏): 迁移显示拓扑查询与切换动作"
```

---

### Task 5: 平台服务迁移（Logger / SingleInstance / AutoStart）

**Files:**
- Create: `src/WithWindows.Core/Diagnostics/Logger.cs`
- Create: `src/WithWindows.Core/App/SingleInstance.cs`
- Create: `src/WithWindows.Core/App/AutoStart.cs`
- Test: `tests/WithWindows.Core.Tests/LoggerTests.cs`
- Test: `tests/WithWindows.Core.Tests/AutoStartTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class Logger : IDisposable`（`namespace WithWindows.Core.Diagnostics`）：`static Logger Open(string dataDir)`；`static Logger Null`；`void Info(string)`；`void Error(string)`；`void Dispose()`
  - `public sealed class SingleInstance : IDisposable`（`namespace WithWindows.Core.App`）：`bool Owned`；`void Release()`；`void Dispose()`
  - `public static class AutoStart`（`namespace WithWindows.Core.App`）：`bool IsEnabled()`；`void Enable()`；`void Disable()`

- [ ] **Step 1: 写测试**

`LoggerTests.cs`：

```csharp
using WithWindows.Core.Diagnostics;

namespace WithWindows.Core.Tests;

public class LoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ww-log-" + Guid.NewGuid().ToString("N"));

    public LoggerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Open_写入后可读回且带级别()
    {
        using (var logger = Logger.Open(_dir))
        {
            logger.Info("测试信息");
            logger.Error("测试错误");
        }
        string text = File.ReadAllText(Path.Combine(_dir, "log.txt"));
        Assert.Contains("[INFO] 测试信息", text);
        Assert.Contains("[ERROR] 测试错误", text);
    }

    [Fact]
    public void Open_超过上限_轮转为_log1()
    {
        string path = Path.Combine(_dir, "log.txt");
        File.WriteAllText(path, new string('x', 1024 * 1024 + 16));
        using (Logger.Open(_dir)) { }
        Assert.True(File.Exists(Path.Combine(_dir, "log.1.txt")));
    }

    [Fact]
    public void Null_不抛异常()
    {
        Logger.Null.Info("丢弃");
        Logger.Null.Error("丢弃");
    }
}
```

`AutoStartTests.cs`（只读断言，不改真实注册表）：

```csharp
using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class AutoStartTests
{
    [Fact]
    public void IsEnabled_只读查询_不抛异常()
    {
        bool enabled = AutoStart.IsEnabled();
        Assert.True(enabled || !enabled);
    }
}
```

- [ ] **Step 2: 运行测试确认失败（红）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 编译失败。

- [ ] **Step 3: 迁移 `Diagnostics/Logger.cs`**

把旧 `src/WithWindows.UI/Logger.cs` 原样复制，只改命名空间为 `WithWindows.Core.Diagnostics`，并把 `Rotate` 中的轮转目标 `Path.Combine(info.DirectoryName!, "log.1.txt")` 改为 `AppPaths.LogRotatedPath`。其余（`MaxBytes = 1024 * 1024`、被占用时降级为 `Null`、`lock` 保护、`AutoFlush`）全部保留。

- [ ] **Step 4: 迁移 `App/SingleInstance.cs`**

把旧 `src/WithWindows.UI/Core/SingleInstance.cs` 原样复制，只改命名空间为 `WithWindows.Core.App`。保留 `Local\WithWindows.SingleInstance`、`AbandonedMutexException` 时接管、`Release()` 幂等。

- [ ] **Step 5: 迁移 `App/AutoStart.cs`**

把旧 `src/WithWindows.UI/Core/AutoStart.cs` 原样复制，只改命名空间为 `WithWindows.Core.App`。保留 `HKCU\...\Run`、值名 `WithWindows`、`"\"{Environment.ProcessPath}\""` 加引号。

- [ ] **Step 6: 运行测试（绿）**

Run:
```powershell
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64 -v:quiet
```
Expected: 全部通过。

- [ ] **Step 7: Commit**

```bash
git add src/WithWindows.Core tests/WithWindows.Core.Tests
git commit -m "feat(平台): 迁移日志、单实例与开机自启到核心层"
```

---

### Task 6: 旧项目退役与文档同步

**Files:**
- Delete: `src/WithWindows.UI/`（整个目录）
- Delete: `tests/WithWindows.UI.Tests/`（整个目录）
- Modify: `WithWindows.sln`（移除两个旧工程）
- Modify: `CHANGELOG.md`、`README.md`、`docs/HANDOFF.md`

**验收**：解决方案只剩 3 个工程（Core / App / Core.Tests），`dotnet build WithWindows.sln -p:Platform=x64` 0 错误，Core 测试全绿。

- [ ] **Step 1: 从解决方案移除旧工程**

```powershell
dotnet sln WithWindows.sln remove src/WithWindows.UI/WithWindows.UI.csproj
dotnet sln WithWindows.sln remove tests/WithWindows.UI.Tests/WithWindows.UI.Tests.csproj
```
Expected: 两条命令都输出「已从解决方案中删除项目」。

- [ ] **Step 2: 删除旧目录**

先确认两条路径都解析在 `.worktrees/dev` 内，再删除（旧实现仍在 `main` 与 git 历史里，可恢复）：

```powershell
Remove-Item -Recurse -Force src/WithWindows.UI
Remove-Item -Recurse -Force tests/WithWindows.UI.Tests
```

- [ ] **Step 3: 全量构建与测试**

Run:
```powershell
dotnet build WithWindows.sln -p:Platform=x64
dotnet test tests/WithWindows.Core.Tests/WithWindows.Core.Tests.csproj -p:Platform=x64
```
Expected: 构建 0 错误；测试全绿。

- [ ] **Step 4: CHANGELOG 增加未发布章节**

在 `# 更新日志` 之下插入：

```markdown
## [未发布]

### 变更
- 项目重构为 WinUI 3 工具箱（进行中）：左侧 `NavigationView` 导航，每个工具一页
- 配置改为全新 schema v1（`tools[]` + `displayMode` + `windowState`），不与旧 v3 兼容；不预置任何默认热键
- 逻辑内核迁移到 `WithWindows.Core` 纯逻辑类库；普通 P/Invoke 改由 CsWin32 生成

### 移除
- 旧实现 `src/WithWindows.UI` 与 `tests/WithWindows.UI.Tests`（`main` 分支保留原样供查阅）

### 其他
- 平台要求提升为 Windows 11 21H2（`net10.0-windows10.0.22621.0`）
```

- [ ] **Step 5: 更新 `docs/HANDOFF.md`**

补上 P2 完成情况，并把「六、下一步」改为 P3（页面接线）与 P4（`ITool`/`ToolRegistry`），指向本计划文档。

- [ ] **Step 6: README 最小同步**

把「快速开始」里的项目路径从 `src/WithWindows.UI` 改成 `src/WithWindows.App`，并在顶部加一句「重构进行中，界面与安装说明将在 P5 重写」。完整重写留给 P5。

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: 退役旧实现并同步文档"
```

---

## 自检结果

**Spec 覆盖检查：**

| Spec 条目 | 对应任务 |
|---|---|
| 6.6 P/Invoke 一律 CsWin32 | Task 1 |
| 6.4 热键（隐藏消息窗口、1409、MOD_NOREPEAT、不设默认值） | Task 2、Task 3 |
| 6.3 配置 schema v1 + 原子写 + .bak + 自愈 | Task 3 |
| 6.5 显示切换（QDC_DATABASE_CURRENT、Changed=false、toggle 纯函数） | Task 4 |
| 6.7 生命周期（Mutex / Run 键 / 1 MB 日志） | Task 5 |
| 8. 复用与重写清单 | Task 1–5 |
| 11. P2 Core 迁移：删旧项目 | Task 6 |
| 9. 测试策略（平台相关只读） | Task 2、4、5 |

**有意推迟到后续计划的：**

- 托盘宿主（WinUIEx `TrayIcon`）与页面接线 → P3/P4
- `ITool` / `ToolRegistry` 抽象 → P4
- Inno Setup 安装器、CI 改造、README 完整重写 → P5

**类型一致性检查：**

- `HotkeyModifiers`（已有）在 Task 1 被 `HotkeyInterop.ToNativeModifiers` 消费，Task 2 的 `HotkeyManager.Register` 复用同一映射
- `AppConfig.CurrentVersion = 1` 与 `ConfigStore.TryDeserialize` 的版本判定一致
- `ToolBinding.Id` 与 `AppConfig.KnownToolIds` 值域一致（`notepad` / `display_mode`）
- `AppPaths.LogRotatedPath` 在 Task 1 定义，Task 5 的 `Logger.Rotate` 消费
- `DisplayModeAction.TopologyFor` 返回值（`uint`）在 `Execute` 中显式转 `SET_DISPLAY_CONFIG_FLAGS`

**已知不确定项（执行时实测确认）：**

1. CsWin32 `NativeMethods.txt` 在子目录 `Interop/` 是否被扫描到 —— Step 3 给了回退方案
2. CsWin32 生成方法的确切签名（`BOOL` 判定、`CreateWindowEx` 参数、`SetDisplayConfig` 数组参数形态）—— 以编译器为准就地修正
3. `RegisterClassEx` 与 `WNDPROC` 委托能否由 CsWin32 干净生成 —— 若不能，按 `docs/agents/interop-and-platform.md` 第一条例外，保留一处最小手写互操作并注明原因
4. 测试注册 F24 是否在该机器被占用 —— 冲突时换 F23/F22
