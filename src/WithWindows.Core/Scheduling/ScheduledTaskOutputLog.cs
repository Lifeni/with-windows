using System.Text.Json;
using System.Text.Json.Serialization;

namespace WithWindows.Core.Scheduling;

public enum ScheduledTaskOutputStream
{
    StandardOutput,
    StandardError,
    System,
}

public sealed record ScheduledTaskOutputLogEntry(
    DateTimeOffset Timestamp,
    long Sequence,
    ScheduledTaskOutputStream Stream,
    string Text);

public static class ScheduledTaskOutputLogCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(ScheduledTaskOutputLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return JsonSerializer.Serialize(entry, JsonOptions);
    }

    public static IReadOnlyList<ScheduledTaskOutputLogEntry> Parse(
        string text,
        ScheduledTaskOutputStream fallbackStream,
        DateTimeOffset fallbackTimestamp)
    {
        ArgumentNullException.ThrowIfNull(text);

        var entries = new List<ScheduledTaskOutputLogEntry>();
        using var reader = new StringReader(text);
        long fallbackSequence = -1_000_000_000L;
        while (reader.ReadLine() is { } line)
        {
            if (TryDeserialize(line, out ScheduledTaskOutputLogEntry? entry))
            {
                entries.Add(entry!);
            }
            else
            {
                entries.Add(new ScheduledTaskOutputLogEntry(
                    fallbackTimestamp,
                    fallbackSequence++,
                    fallbackStream,
                    line));
            }
        }

        return entries;
    }

    public static bool TryDeserialize(
        string text,
        out ScheduledTaskOutputLogEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(text) || text[0] != '{')
            return false;

        try
        {
            ScheduledTaskOutputLogEntry? parsed =
                JsonSerializer.Deserialize<ScheduledTaskOutputLogEntry>(
                    text,
                    JsonOptions);
            if (parsed is null
                || parsed.Text is null
                || !Enum.IsDefined(parsed.Stream))
            {
                return false;
            }

            entry = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
