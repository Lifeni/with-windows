namespace WithWindows.Core.Todos;

/// <summary>生成 Windows 原生通知的标题与正文。</summary>
public static class TodoReminderNotificationFormatter
{
    public const string Title = "待办提醒";
    public const int MaximumBodyLength = 255;

    public static string FormatBody(IReadOnlyList<string> titles)
    {
        string[] nonEmpty = titles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToArray();
        if (nonEmpty.Length == 0)
            return "有一项待办已到期。";

        string body = nonEmpty.Length == 1
            ? $"“{nonEmpty[0]}”已到期。"
            : $"{nonEmpty.Length} 项待办已到期：{string.Join("；", nonEmpty)}";
        return body.Length <= MaximumBodyLength
            ? body
            : body[..(MaximumBodyLength - 1)] + "…";
    }
}
