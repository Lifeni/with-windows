using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Scheduling;

namespace WithWindows.Controls;

/// <summary>展示 AI 生成的脚本草案，用户确认后才允许保存。</summary>
public static class ChatScriptProposalDialog
{
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        ScheduledTask task)
    {
        var summary = new TextBlock
        {
            Text =
                $"脚本名称：{task.Name}\n"
                + $"执行计划：{ScheduledTaskDisplayFormatter.ScheduleText(task)}\n"
                + $"超时：{task.TimeoutSeconds} 秒",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(
            summary,
            "ChatScriptProposalSummary");

        var preview = new TextBox
        {
            AcceptsReturn = true,
            Background = (Brush)Application.Current.Resources[
                "CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources[
                "CardStrokeColorDefaultBrush"],
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 12,
            IsReadOnly = true,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            MaxHeight = 300,
            MinHeight = 220,
            Text = task.ScriptText,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(
            preview,
            "ChatScriptProposalPreview");
        AutomationProperties.SetName(preview, "AI 生成的 C# 脚本预览");

        var content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 500,
        };
        content.Children.Add(summary);
        content.Children.Add(preview);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "AI 生成的脚本草案",
            Content = content,
            PrimaryButtonText = "添加脚本",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 680d;
        AutomationProperties.SetAutomationId(
            dialog,
            "ChatScriptProposalDialog");

        return await App.ShowDialogAsync(dialog) == ContentDialogResult.Primary;
    }
}
