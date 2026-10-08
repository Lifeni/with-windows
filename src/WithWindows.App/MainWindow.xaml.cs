using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WithWindows.Views;

namespace WithWindows;

public sealed partial class MainWindow : Window
{
    /// <summary>导航项 Tag → 页面类型。新增工具页只需在此登记一处。</summary>
    private static readonly Dictionary<string, Type> Routes = new(StringComparer.Ordinal)
    {
        [NotepadPage.RouteTag] = typeof(NotepadPage),
        [HotkeysPage.RouteTag] = typeof(HotkeysPage),
        [SettingsPage.RouteTag] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            Navigate(SettingsPage.RouteTag);
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            Navigate(tag);
    }

    private void Navigate(string tag)
    {
        if (!Routes.TryGetValue(tag, out Type? pageType) || NavFrame.CurrentSourcePageType == pageType)
            return;

        NavFrame.Navigate(pageType);
    }
}
