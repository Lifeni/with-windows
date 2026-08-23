using System.Drawing;
using System.Windows.Forms;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Notepad;

namespace WithWindows;

/// <summary>
/// 常驻宿主：系统托盘图标 + 热键分发。无主窗口。
/// 动作执行结果以托盘气泡提示（自定义图标），失败记录日志并气泡报错；
/// 未发生实际变化时只记录日志、不弹通知。
/// </summary>
public sealed class App : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly HotkeyManager _hotkeys = new();
    private readonly ActionRegistry _registry;
    private readonly AutoThemeScheduler _autoTheme;
    private readonly Logger _log;
    private readonly string _configPath;
    private readonly SingleInstance _singleInstance;
    private readonly NotepadHost _notepad;
    private object? _toggleArgs;
    private object? _themeArgs;
    private readonly ModernMenuItem _displayItem;
    private readonly ModernMenuItem _themeItem;
    private readonly ModernMenuItem _notepadItem;
    private string? _displayHotkey;
    private string? _themeHotkey;
    private string? _notepadHotkey;

    /// <summary>菜单/默认切换参数:与配置默认一致的 internal ↔ extend 循环。</summary>
    private static readonly object DefaultToggleArgs =
        MiniJson.Parse("""{ "mode": "toggle", "modes": ["internal", "extend"] }""");

    /// <summary>菜单/默认切换参数:与配置默认一致的亮 ↔ 暗循环。</summary>
    private static readonly object DefaultThemeArgs =
        MiniJson.Parse("""{ "mode": "toggle" }""");

    public App(ActionRegistry registry, Logger log, AutoThemeScheduler autoTheme, string configPath, SingleInstance singleInstance, NotepadHost notepad)
    {
        _registry = registry;
        _log = log;
        _autoTheme = autoTheme;
        _configPath = configPath;
        _singleInstance = singleInstance;
        _notepad = notepad;

        var menu = new ModernMenu();
        _notepadItem = new ModernMenuItem
        {
            Text = "快捷记事",
            IconGlyph = "\uE70F", // Edit
            OnClick = () => _notepad.Show(),
        };
        menu.Add(_notepadItem);
        _displayItem = new ModernMenuItem
        {
            Text = "切换投影",
            IconGlyph = "\uE7F4", // TVMonitor
            OnClick = () => Execute(_registry.Find("display_mode")!, _toggleArgs ?? DefaultToggleArgs),
        };
        menu.Add(_displayItem);
        _themeItem = new ModernMenuItem
        {
            Text = "切换亮暗",
            IconGlyph = "\uE706", // Sun
            OnClick = () => Execute(_registry.Find("theme")!, _themeArgs ?? DefaultThemeArgs),
        };
        menu.Add(_themeItem);
        menu.Add(new ModernMenuItem
        {
            Text = "打开配置",
            IconGlyph = "\uE713", // Settings
            OnClick = OpenConfigFile,
        });
        menu.AddSeparator();
        // 勾选项：只显示勾选位，不再叠加图标（与 Win11 惯例一致，避免重复视觉）
        menu.Add(new ModernMenuItem
        {
            Text = "自动亮暗",
            Checkable = true,
            IsChecked = () => _autoTheme.Enabled,
            OnClick = ToggleAutoTheme,
        });
        menu.Add(new ModernMenuItem
        {
            Text = "开机自启",
            Checkable = true,
            IsChecked = AutoStart.IsEnabled,
            OnClick = ToggleAutoStart,
        });
        menu.AddSeparator();
        menu.Add(new ModernMenuItem
        {
            Text = "重启应用",
            IconGlyph = "\uE72C", // Refresh
            OnClick = RestartApp,
        });
        menu.Add(new ModernMenuItem
        {
            Text = "恢复配置",
            IconGlyph = "\uE777", // Restore
            OnClick = RestoreConfig,
        });
        menu.Add(new ModernMenuItem
        {
            Text = $"版本 v{GetVersion()}",
            IconGlyph = "\uE774", // Globe
            OnClick = OpenGitHubPage,
        });
        menu.Add(new ModernMenuItem
        {
            Text = "退出应用",
            IconGlyph = "\uE711", // Close
            OnClick = () => Application.Exit(),
        });

        _tray = new TrayIcon(LoadTrayIcon(), menu);
    }

    /// <summary>动作名 → 中文显示名（启动通知列出生效热键时使用）。</summary>
    private static string ActionDisplayName(string action) => action switch
    {
        "display_mode" => "切换投影",
        "theme" => "切换亮暗",
        "notepad" => "快捷记事",
        _ => action,
    };

    private void ToggleAutoTheme()
    {
        if (_autoTheme.Enabled)
        {
            _autoTheme.Stop();
            return;
        }

        string? error = _autoTheme.TryStart();
        if (error is not null)
        {
            _log.Error($"自动亮暗切换启用失败: {error}");
            _tray.ShowBalloon("With Windows", $"自动亮暗切换启用失败：{error}");
        }
    }

    /// <summary>用系统默认程序打开配置文件（记事本/关联编辑器）。</summary>
    private void OpenConfigFile()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_configPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"打开配置文件失败: {ex}");
            _tray.ShowBalloon("With Windows", $"打开配置文件失败：{ex.Message}");
        }
    }

    /// <summary>项目 GitHub 仓库地址（版本菜单项点击跳转）。</summary>
    private const string GitHubUrl = "https://github.com/Lifeni/with-windows";

    /// <summary>读取程序集版本号（csproj 的 Version，如 0.1.0）。</summary>
    private static string GetVersion()
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>用系统默认浏览器打开 GitHub 项目页。</summary>
    private void OpenGitHubPage()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"打开 GitHub 页面失败: {ex}");
            _tray.ShowBalloon("With Windows", $"打开 GitHub 页面失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 恢复默认配置：删除运行时配置、记事本内容与注册表设置（自动亮暗标志、记事本位置、开机自启），
    /// 重新生成默认配置并重启应用。确认对话框防误触。
    /// </summary>
    private void RestoreConfig()
    {
        var confirm = MessageBox.Show(
            "将删除运行时配置、注册表设置与记事本内容，并恢复默认配置（应用会重启）。是否继续？",
            "With Windows", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
            return;

        try
        {
            // 先关闭记事本（其关闭时会把内容写回 notepad.txt），再删除生成文件
            _notepad.Dispose();
            string dataDir = Path.GetDirectoryName(_configPath)!;
            foreach (string file in new[] { _configPath, Path.Combine(dataDir, "notepad.txt") })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\WithWindows", throwOnMissingSubKey: false);
            AutoStart.Disable();
        }
        catch (Exception ex)
        {
            _log.Error($"恢复配置失败: {ex}");
            _tray.ShowBalloon("With Windows", $"恢复配置失败：{ex.Message}");
            return;
        }

        new ConfigStore(_configPath).EnsureExists(_log);
        RestartApp();
    }

    /// <summary>
    /// 重启应用（配置只在启动时读取一次，修改后需重启生效）。
    /// 先释放单实例互斥体再启动新实例，否则新进程会被单实例守卫挡掉；
    /// 启动失败则不退出，继续运行。
    /// </summary>
    private void RestartApp()
    {
        try
        {
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                throw new InvalidOperationException("无法定位当前可执行文件路径");

            _singleInstance.Release();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log.Error($"重启失败: {ex}");
            _tray.ShowBalloon("With Windows", $"重启失败：{ex.Message}");
            return;
        }
        Application.Exit();
    }

    private static void ToggleAutoStart()
    {
        if (AutoStart.IsEnabled())
            AutoStart.Disable();
        else
            AutoStart.Enable();
    }

    /// <summary>注册全部配置条目；返回失败项列表（热键冲突、格式错误、未知动作），不中断其余注册。
    /// 注册完成后弹一次"已在后台运行"通知，列出生效热键。</summary>
    public IReadOnlyList<string> RegisterAll(IEnumerable<ConfigEntry> entries)
    {
        var failures = new List<string>();
        var registered = new List<string>();

        foreach (var entry in entries)
        {
            // 声明式条目（无热键，如 auto_theme）：由对应组件消费，不注册热键
            if (entry.Hotkey is null)
                continue;

            // 记录第一条 display_mode 条目的参数与热键,托盘菜单"切换投影显示模式"与热键行为保持一致
            if (entry.Action == "display_mode" && _toggleArgs is null)
            {
                _toggleArgs = entry.Args;
                _displayHotkey = entry.Hotkey;
                _displayItem.Shortcut = _displayHotkey;
            }

            // 记录第一条 theme 条目的参数与热键,托盘菜单"切换亮暗"与热键行为保持一致
            if (entry.Action == "theme" && _themeArgs is null)
            {
                _themeArgs = entry.Args;
                _themeHotkey = entry.Hotkey;
                _themeItem.Shortcut = _themeHotkey;
            }

            // 记录第一条 notepad 条目的热键,托盘菜单"快捷记事"显示快捷键提示
            if (entry.Action == "notepad" && _notepadHotkey is null)
            {
                _notepadHotkey = entry.Hotkey;
                _notepadItem.Shortcut = _notepadHotkey;
            }

            if (!HotkeyParser.TryParse(entry.Hotkey, out var hotkey, out var parseError))
            {
                failures.Add($"“{entry.Hotkey}”：{parseError}");
                continue;
            }

            var action = _registry.Find(entry.Action);
            if (action is null)
            {
                failures.Add($"“{entry.Hotkey}”：未知动作“{entry.Action}”");
                continue;
            }

            if (!_hotkeys.Register(hotkey, () => Execute(action, entry.Args), out var registerError))
            {
                failures.Add($"“{entry.Hotkey}”：{registerError}");
                continue;
            }

            registered.Add($"{entry.Hotkey}（{ActionDisplayName(entry.Action)}）");
            _log.Info($"热键 {entry.Hotkey} → {entry.Action} 已注册");
        }

        if (registered.Count > 0)
            _tray.ShowBalloon("With Windows", $"已在后台运行\n热键：{string.Join("、", registered)}");

        return failures;
    }

    private void Execute(IAction action, object? args)
    {
        try
        {
            var result = action.Execute(args);
            if (result.Changed)
            {
                _log.Info($"[{action.Name}] {result.Message}");
                if (result.Notify)
                    _tray.ShowBalloon("With Windows", result.Message);
            }
            else
            {
                _log.Info($"[{action.Name}] {result.Message}（未变化，不提示）");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[{action.Name}] 执行失败: {ex}");
            _tray.ShowBalloon("With Windows", $"动作失败: {ex.Message}");
        }
    }

    /// <summary>加载嵌入资源中的图标；失败时回退系统默认图标。</summary>
    private static Icon LoadTrayIcon() => IconLoader.Load();

    public void Dispose()
    {
        _hotkeys.Dispose();
        _tray.Dispose();
    }
}
