using System.Text.Json;
using System.Text.RegularExpressions;

namespace WithWindows.Core.Scheduling;

public static partial class PythonEnvironmentParser
{
    public static PythonEnvironmentSnapshot? ParseProbeJson(
        string json,
        PythonInterpreterSource source,
        string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string? executable = root.TryGetProperty("executable", out JsonElement executableElement)
                ? executableElement.GetString()
                : null;
            int major = root.TryGetProperty("major", out JsonElement majorElement)
                ? majorElement.GetInt32()
                : -1;
            int minor = root.TryGetProperty("minor", out JsonElement minorElement)
                ? minorElement.GetInt32()
                : -1;
            int micro = root.TryGetProperty("micro", out JsonElement microElement)
                ? microElement.GetInt32()
                : -1;
            int bits = root.TryGetProperty("bits", out JsonElement bitsElement)
                ? bitsElement.GetInt32()
                : -1;

            if (major != 3
                || minor < 0
                || micro < 0
                || bits is not (32 or 64)
                || string.IsNullOrWhiteSpace(executable)
                || !System.IO.Path.IsPathFullyQualified(executable)
                || !string.Equals(
                    System.IO.Path.GetExtension(executable),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string normalized = System.IO.Path.GetFullPath(executable);
            return new PythonEnvironmentSnapshot(
                normalized,
                $"{major}.{minor}.{micro}",
                bits,
                source,
                packageFamilyName);
        }
        catch (Exception ex) when (ex is JsonException
            or InvalidOperationException
            or FormatException
            or NotSupportedException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> ParsePyLauncherPaths(string output)
    {
        var paths = new List<string>();
        foreach (string line in output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = PyLauncherPathRegex().Match(line);
            if (!match.Success)
                continue;

            string path = match.Groups["path"].Value.Trim();
            if (!System.IO.Path.IsPathFullyQualified(path)
                || !string.Equals(
                    System.IO.Path.GetExtension(path),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = System.IO.Path.GetFullPath(path);
            if (!paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                paths.Add(normalized);
        }

        return paths;
    }

    public static bool IsStoreAliasPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string? directory = System.IO.Path.GetDirectoryName(
                System.IO.Path.GetFullPath(path));
            return directory is not null
                && directory.EndsWith(
                    @"\Microsoft\WindowsApps",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [GeneratedRegex(
        @"(?<path>[A-Z]:\\.+?\.exe)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PyLauncherPathRegex();
}
