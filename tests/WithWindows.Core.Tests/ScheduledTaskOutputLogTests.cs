using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskOutputLogTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 11, 10, 21, 30, 8, 123, TimeSpan.FromHours(8));

    [Fact]
    public void Serialize_保留时间戳顺序输出流和正文()
    {
        var entry = new ScheduledTaskOutputLogEntry(
            Timestamp,
            42,
            ScheduledTaskOutputStream.StandardError,
            "错误：找不到文件");

        Assert.True(ScheduledTaskOutputLogCodec.TryDeserialize(
            ScheduledTaskOutputLogCodec.Serialize(entry),
            out ScheduledTaskOutputLogEntry? parsed));

        Assert.Equal(entry, parsed);
    }

    [Fact]
    public void Parse_同时兼容时间戳日志和旧版纯文本()
    {
        var structured = new ScheduledTaskOutputLogEntry(
            Timestamp,
            7,
            ScheduledTaskOutputStream.StandardOutput,
            "第一行");
        string text = ScheduledTaskOutputLogCodec.Serialize(structured)
            + Environment.NewLine
            + "旧版输出";

        IReadOnlyList<ScheduledTaskOutputLogEntry> entries =
            ScheduledTaskOutputLogCodec.Parse(
                text,
                ScheduledTaskOutputStream.StandardError,
                Timestamp.AddSeconds(1));

        Assert.Equal(2, entries.Count);
        Assert.Equal(structured, entries[0]);
        Assert.Equal("旧版输出", entries[1].Text);
        Assert.Equal(
            ScheduledTaskOutputStream.StandardError,
            entries[1].Stream);
        Assert.Equal(Timestamp.AddSeconds(1), entries[1].Timestamp);
    }

    [Fact]
    public void Parse_保留显式空白行但不为末尾换行增加空行()
    {
        IReadOnlyList<ScheduledTaskOutputLogEntry> entries =
            ScheduledTaskOutputLogCodec.Parse(
                "第一行" + Environment.NewLine
                + Environment.NewLine
                + "第三行" + Environment.NewLine,
                ScheduledTaskOutputStream.StandardOutput,
                Timestamp);

        Assert.Equal(
            ["第一行", "", "第三行"],
            entries.Select(entry => entry.Text));
    }
}
