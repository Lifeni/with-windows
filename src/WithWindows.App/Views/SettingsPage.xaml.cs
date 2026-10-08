using Microsoft.UI.Xaml.Controls;

namespace WithWindows.Views;

public sealed partial class SettingsPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "settings";

    public SettingsPage() => InitializeComponent();
}
