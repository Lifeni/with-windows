using System.Text.Json;
using WithWindows.Core.Chat;
using WithWindows.Core.Config;
using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ChatScriptProposalTests
{
    [Fact]
    public async Task Create_脚本意图请求包含提案工具()
    {
        using HttpRequestMessage request = ChatRequestFactory.Create(
            new ChatSettings
            {
                BaseUrl = "https://example.test/v1",
                Model = "demo",
                SystemPrompt = "",
            },
            "",
            [new ChatMessage(ChatRole.User, "帮我添加一个每天执行的 C# 脚本")],
            allowScriptProposal: true);

        string json = await request.Content!.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement tool = document.RootElement.GetProperty("tools")[0];

        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal(
            CSharpScriptProposalTool.Name,
            tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(
            "auto",
            document.RootElement.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task Create_普通聊天不携带脚本工具()
    {
        using HttpRequestMessage request = ChatRequestFactory.Create(
            new ChatSettings
            {
                BaseUrl = "https://example.test/v1",
                Model = "demo",
                SystemPrompt = "",
            },
            "",
            [new ChatMessage(ChatRole.User, "解释一下递归")]);

        string json = await request.Content!.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.TryGetProperty("tools", out _));
        Assert.False(document.RootElement.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public void ParseSse_读取流式工具调用参数片段()
    {
        ChatStreamEvent result = ChatStreamParser.Parse(
            """
            {
              "choices": [
                {
                  "delta": {
                    "tool_calls": [
                      {
                        "index": 0,
                        "id": "call_1",
                        "type": "function",
                        "function": {
                          "name": "propose_csharp_script",
                          "arguments": "{\"name\":"
                        }
                      }
                    ]
                  }
                }
              ]
            }
            """);

        Assert.Equal(ChatStreamEventKind.ToolCall, result.Kind);
        Assert.NotNull(result.ToolCall);
        Assert.Equal(0, result.ToolCall.Index);
        Assert.Equal("call_1", result.ToolCall.Id);
        Assert.Equal("propose_csharp_script", result.ToolCall.Name);
        Assert.Equal("{\"name\":", result.ToolCall.ArgumentsDelta);
    }

    [Fact]
    public void Accumulator_拼接同一工具调用的参数()
    {
        var accumulator = new ChatToolCallAccumulator();
        accumulator.Add(new ChatToolCallDelta(0, "call_1", "propose_csharp_script", "{\"name\":"));
        accumulator.Add(new ChatToolCallDelta(0, null, null, "\"巡检\"}"));

        ChatToolCall call = Assert.Single(accumulator.Build());
        Assert.Equal("call_1", call.Id);
        Assert.Equal("propose_csharp_script", call.Name);
        Assert.Equal("{\"name\":\"巡检\"}", call.Arguments);
    }

    [Fact]
    public void ParseTask_把每周提案转换为可保存任务()
    {
        DateTimeOffset now =
            new(2026, 11, 11, 10, 0, 0, TimeSpan.FromHours(8));
        string arguments =
            """
            {
              "name": "每周巡检",
              "script": "Console.WriteLine(\"hello\");",
              "timeoutSeconds": 60,
              "schedule": {
                "kind": "weekly",
                "time": "18:30",
                "daysOfWeek": ["monday", "friday"]
              }
            }
            """;

        ScheduledTask task = CSharpScriptProposalParser.ParseTask(arguments, now);

        Assert.Equal("每周巡检", task.Name);
        Assert.Equal("Console.WriteLine(\"hello\");", task.ScriptText);
        Assert.Equal(ScheduleKind.Weekly, task.Schedule.Kind);
        Assert.Equal(new TimeOnly(18, 30), task.Schedule.TimeOfDay);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Friday],
            task.Schedule.DaysOfWeek);
        Assert.Equal(60, task.TimeoutSeconds);
        Assert.True(ScheduledTaskValidation.IsValidTaskId(task.Id));
    }

    [Fact]
    public void ParseTask_拒绝缺少星期的每周提案()
    {
        string arguments =
            """
            {
              "name": "每周巡检",
              "script": "Console.WriteLine(\"hello\");",
              "schedule": {
                "kind": "weekly",
                "time": "18:30"
              }
            }
            """;

        Assert.Throws<ArgumentException>(() =>
            CSharpScriptProposalParser.ParseTask(
                arguments,
                DateTimeOffset.Now));
    }

    [Theory]
    [InlineData("帮我创建一个每天执行的脚本", true)]
    [InlineData("解释一下这个 C# 脚本", false)]
    [InlineData("创建一个待办", false)]
    public void IntentDetector_只对明确的脚本创建请求启用(
        string text,
        bool expected)
    {
        bool actual = ChatScriptIntentDetector.ShouldOfferProposal(
            [new ChatMessage(ChatRole.User, text)]);

        Assert.Equal(expected, actual);
    }
}
