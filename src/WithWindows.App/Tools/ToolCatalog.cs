using WithWindows.Views;

namespace WithWindows.Tools;

/// <summary>
/// 工具目录项。导航项由 <see cref="ShowInNav"/> 的项生成，快捷键行由 <see cref="HasHotkey"/> 的项生成，
/// 新增工具只需在 <see cref="ToolCatalog.Tools"/> 加一条。
/// </summary>
/// <param name="Id">工具标识，与配置 tools[].id 对应。</param>
/// <param name="Title">界面显示名。</param>
/// <param name="Glyph">Segoe Fluent Icons 字形。</param>
/// <param name="PageType">对应的导航页面；无页面（纯动作）时为 null。</param>
/// <param name="ShowInNav">是否出现在左栏导航。</param>
/// <param name="HasHotkey">是否出现在快捷键页。</param>
public sealed record ToolEntry(
    string Id,
    string Title,
    string Glyph,
    Type? PageType,
    bool ShowInNav,
    bool HasHotkey);

/// <summary>首版工具目录。新增有页面的工具 = 加一条目录项 + 一个页面，主窗口代码不动。</summary>
public static class ToolCatalog
{
    public const string NotepadId = "notepad";
    public const string HotkeysId = "hotkeys";
    public const string DisplayModeId = "display_mode";

    /// <summary>全部工具；顺序即导航与快捷键列表顺序。</summary>
    public static IReadOnlyList<ToolEntry> Tools { get; } = new[]
    {
        new ToolEntry(NotepadId, "记事本", "\uE70B", typeof(NotepadPage), ShowInNav: true, HasHotkey: true),
        new ToolEntry(HotkeysId, "快捷键", "\uE765", typeof(HotkeysPage), ShowInNav: true, HasHotkey: false),
        new ToolEntry(DisplayModeId, "切换投屏", "\uE7F8", PageType: null, ShowInNav: false, HasHotkey: true),
    };

    /// <summary>导航项的可访问性标识：由工具 ID 生成（如 notepad → NavItemNotepad）。</summary>
    public static string NavAutomationId(string id)
        => "NavItem" + char.ToUpperInvariant(id[0]) + id[1..];
}
