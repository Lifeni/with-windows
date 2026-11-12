namespace WithWindows.Core.Chat;

/// <summary>AI 对话中允许模型调用的唯一脚本工具。</summary>
public static class CSharpScriptProposalTool
{
    public const string Name = "propose_csharp_script";

    public const string SystemInstruction =
        "当用户明确要求创建、添加或编写 C# 脚本时，必须调用 propose_csharp_script 工具。"
        + "工具只生成待用户确认的草案，不会自动运行、覆盖或删除脚本。"
        + "脚本可包含 using 指令和异步顶层语句；优先使用 using (...) { } 块。"
        + "调用工具后不要再次输出完整脚本正文，只用简短文字说明草案已生成。";

    public static object Definition => new Dictionary<string, object?>
    {
        ["type"] = "function",
        ["function"] = new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["description"] =
                "生成一个待用户确认的 C# 脚本草案，包含可选的执行计划。"
                + "这不会保存或运行脚本。",
            ["parameters"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "脚本名称，最多 80 个字符。",
                    },
                    ["script"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] =
                            "完整 C# 脚本正文，可使用 using 指令和异步顶层语句。",
                    },
                    ["timeoutSeconds"] = new Dictionary<string, object?>
                    {
                        ["type"] = "integer",
                        ["minimum"] = 5,
                        ["maximum"] = 86400,
                        ["description"] = "执行超时秒数，默认 600。",
                    },
                    ["schedule"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["kind"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = new[]
                                {
                                    "manual",
                                    "once",
                                    "daily",
                                    "weekly",
                                    "monthly",
                                },
                            },
                            ["date"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["description"] = "单次执行的本地日期，格式 yyyy-MM-dd。",
                            },
                            ["time"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["description"] = "本地时间，格式 HH:mm。",
                            },
                            ["daysOfWeek"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "string",
                                },
                                ["description"] =
                                    "每周任务的星期：monday、tuesday、wednesday、"
                                    + "thursday、friday、saturday、sunday。",
                            },
                            ["dayOfMonth"] = new Dictionary<string, object?>
                            {
                                ["type"] = "integer",
                                ["minimum"] = 1,
                                ["maximum"] = 31,
                            },
                        },
                        ["required"] = new[] { "kind" },
                    },
                },
                ["required"] = new[] { "name", "script", "schedule" },
            },
        },
    };
}
