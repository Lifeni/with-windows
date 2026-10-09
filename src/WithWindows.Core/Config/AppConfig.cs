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
    // 默认宽度需不小于 NavigationView 的 ExpandedModeThresholdWidth（1008），否则首启左栏只显示汉堡按钮
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 720;
    public double? X { get; set; }
    public double? Y { get; set; }
    public double NotepadFontSize { get; set; } = 14;
}
