using System.Text.Json;

namespace WithWindows.Core.Scheduling;

public sealed record LuaRunSpec(
    string ScriptPath,
    string WorkingDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static LuaRunSpec? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            LuaRunSpec? spec = JsonSerializer.Deserialize<LuaRunSpec>(
                json,
                JsonOptions);
            return spec is
            {
                ScriptPath: { Length: > 0 } script,
                WorkingDirectory: { Length: > 0 } workingDirectory,
            }
                && System.IO.Path.IsPathFullyQualified(script)
                && System.IO.Path.IsPathFullyQualified(workingDirectory)
                    ? spec
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
