namespace WithWindows.Core.Chat;

/// <summary>仅在用户明确要求创建脚本时才向模型提供脚本工具。</summary>
public static class ChatScriptIntentDetector
{
    private static readonly string[] ScriptSubjects =
    [
        "c#",
        "csharp",
        "csx",
        "脚本",
        "定时任务",
        "计划任务",
    ];

    private static readonly string[] CreationVerbs =
    [
        "添加",
        "创建",
        "生成",
        "编写",
        "制作",
        "新建",
    ];

    public static bool ShouldOfferProposal(
        IReadOnlyList<ChatMessage> messages)
    {
        foreach (ChatMessage message in messages)
        {
            if (message.Role != ChatRole.User)
                continue;

            string text = message.Content.ToLowerInvariant();
            bool mentionsScript = ScriptSubjects.Any(text.Contains);
            bool requestsCreation = CreationVerbs.Any(text.Contains);
            if (mentionsScript && requestsCreation)
                return true;
        }

        return false;
    }
}
