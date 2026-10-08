using Microsoft.UI.Xaml.Controls;

namespace WithWindows.Views;

public sealed partial class NotepadPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "notepad";

    public NotepadPage() => InitializeComponent();
}
