using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Chat;
using WithWindows.Core.Config;

namespace WithWindows.Controls;

public readonly record struct ChatProviderDialogResult(
    string BaseUrl,
    string Model,
    string SystemPrompt,
    string? NewApiKey,
    bool ClearApiKey);

/// <summary>编辑 OpenAI 兼容端点；不读取或回显已经保存的 API Key。</summary>
public static class ChatProviderDialog
{
    public static async Task<ChatProviderDialogResult?> ShowAsync(
        XamlRoot xamlRoot,
        ChatSettings current,
        bool hasStoredKey)
    {
        var baseUrl = new TextBox
        {
            Header = "Base URL",
            PlaceholderText = "https://example.com/v1",
            Text = current.BaseUrl,
        };
        AutomationProperties.SetAutomationId(baseUrl, "ChatProviderBaseUrl");

        var model = new TextBox
        {
            Header = "模型",
            PlaceholderText = "模型标识",
            Text = current.Model,
        };
        AutomationProperties.SetAutomationId(model, "ChatProviderModel");

        var apiKey = new PasswordBox
        {
            Header = "API Key",
            PlaceholderText = hasStoredKey
                ? "留空表示保留已保存密钥"
                : "无需认证的端点可以留空",
        };
        AutomationProperties.SetAutomationId(apiKey, "ChatProviderApiKey");

        var clearKey = new CheckBox
        {
            Content = "清除已保存密钥",
            IsEnabled = hasStoredKey,
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetAutomationId(clearKey, "ChatProviderClearKey");
        clearKey.Checked += (_, _) => apiKey.IsEnabled = false;
        clearKey.Unchecked += (_, _) => apiKey.IsEnabled = true;

        var systemPrompt = new TextBox
        {
            Header = "系统提示",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            Text = current.SystemPrompt,
        };
        AutomationProperties.SetAutomationId(systemPrompt, "ChatProviderSystemPrompt");

        var validation = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(validation, "ChatProviderValidation");
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);

        var content = new StackPanel { Spacing = 12, MinWidth = 440 };
        content.Children.Add(baseUrl);
        content.Children.Add(model);
        content.Children.Add(apiKey);
        content.Children.Add(clearKey);
        content.Children.Add(systemPrompt);

        var contentScroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(contentScroll, "ChatProviderContentScroll");
        AutomationProperties.SetName(contentScroll, "AI 服务配置内容");

        var contentLayout = new Grid
        {
            MinWidth = 440,
            VerticalAlignment = VerticalAlignment.Top,
        };
        contentLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentLayout.Children.Add(contentScroll);
        Grid.SetRow(validation, 1);
        contentLayout.Children.Add(validation);

        void UpdateContentHeight()
            => contentScroll.MaxHeight = Math.Clamp(
                xamlRoot.Size.Height - 200,
                min: 160,
                max: 520);

        void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => UpdateContentHeight();

        UpdateContentHeight();

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "AI 服务配置",
            Content = contentLayout,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        ChatProviderDialogResult? result = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                string normalized = ChatValidation.NormalizeBaseUrl(baseUrl.Text);
                if (string.IsNullOrWhiteSpace(model.Text))
                    throw new ArgumentException("请填写模型。");
                if (clearKey.IsChecked == true && apiKey.Password.Length > 0)
                    throw new ArgumentException("不能同时输入新密钥并勾选清除。");

                result = new ChatProviderDialogResult(
                    normalized,
                    model.Text.Trim(),
                    systemPrompt.Text.Trim(),
                    apiKey.Password.Length > 0 ? apiKey.Password : null,
                    clearKey.IsChecked == true);
            }
            catch (ArgumentException ex)
            {
                args.Cancel = true;
                validation.Text = ex.Message;
                validation.Visibility = Visibility.Visible;
                validation.DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (validation.XamlRoot is null)
                            return;

                        AutomationPeer? peer =
                            FrameworkElementAutomationPeer.FromElement(validation)
                            ?? FrameworkElementAutomationPeer.CreatePeerForElement(validation);
                        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                    });
            }
        };

        ContentDialogResult shown;
        try
        {
            xamlRoot.Changed += OnXamlRootChanged;
            shown = await dialog.ShowAsync();
        }
        finally
        {
            xamlRoot.Changed -= OnXamlRootChanged;
        }

        return shown == ContentDialogResult.Primary ? result : null;
    }
}
