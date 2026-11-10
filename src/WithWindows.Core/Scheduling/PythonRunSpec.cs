using System.Text.Json;

namespace WithWindows.Core.Scheduling;

public sealed record PythonRunSpec(
    string PythonPath,
    string ScriptPath,
    string WorkingDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static PythonRunSpec? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            PythonRunSpec? spec = JsonSerializer.Deserialize<PythonRunSpec>(
                json,
                JsonOptions);
            return spec is
            {
                PythonPath: { Length: > 0 } python,
                ScriptPath: { Length: > 0 } script,
                WorkingDirectory: { Length: > 0 } workingDirectory,
            }
                && System.IO.Path.IsPathFullyQualified(python)
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
