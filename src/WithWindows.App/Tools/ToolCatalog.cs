using WithWindows.Views;

namespace WithWindows.Tools;

/// <summary>
/// 工具目录项。左栏导航由 <see cref="ShowInNav"/> 的项生成，新增工具只需在
/// <see cref="ToolCatalog.Tools"/> 加一条；托盘菜单需要显式维护在
/// <see cref="WithWindows.Services.TrayHost"/> 中。
/// </summary>
/// <param name="Id">工具标识，与配置 tools[].id 对应。</param>
/// <param name="Title">界面显示名。</param>
/// <param name="Glyph">Segoe Fluent Icons 字形。</param>
/// <param name="PageType">对应的导航页面；无页面（纯动作）时为 null。</param>
/// <param name="ShowInNav">是否出现在左栏导航。</param>
public sealed record ToolEntry(
    string Id,
    string Title,
    string Glyph,
    Type? PageType,
    bool ShowInNav);

/// <summary>首版工具目录。新增有页面的工具 = 加一条目录项 + 一个页面，主窗口代码不动。</summary>
public static class ToolCatalog
{
    public const string NotepadId = "notepad";
    public const string TodoId = "todo";
    public const string AiChatId = "ai_chat";
    public const string ScheduledId = "scheduled";
    public const string DisplayPageId = "display";
    public const string DisplayModeId = "display_mode";
    public const string ThemeToggleId = "theme_toggle";

    /// <summary>全部工具；顺序即导航顺序。</summary>
    public static IReadOnlyList<ToolEntry> Tools { get; } = new[]
    {
        new ToolEntry(NotepadId, "记事", "\uE70B", typeof(NotepadPage), ShowInNav: true),
        // E9D5 = CheckList（WinUI Gallery 图标表，tags: tasks, items, organize, progress）
        new ToolEntry(TodoId, "待办", "\uE9D5", typeof(TodoPage), ShowInNav: true),
        // E8F2 = ChatBubbles（WinUI Gallery 图标表，tags: chat, messages）
        new ToolEntry(AiChatId, "对话", "\uE8F2", typeof(ChatPage), ShowInNav: true),
        // E943 = Code（WinUI Gallery 图标表，tags: bracket, slash）
        new ToolEntry(ScheduledId, "脚本", "\uE943", typeof(ScheduledTasksPage), ShowInNav: true),
        // E7C4 = TaskView（WinUI Gallery 图标表）
        new ToolEntry(DisplayPageId, "切换", "\uE7C4", typeof(DisplayPage), ShowInNav: true),
    };

    /// <summary>导航项的可访问性标识：由工具 ID 生成（如 notepad → NavItemNotepad）。</summary>
    public static string NavAutomationId(string id)
        => "NavItem" + char.ToUpperInvariant(id[0]) + id[1..];
}
