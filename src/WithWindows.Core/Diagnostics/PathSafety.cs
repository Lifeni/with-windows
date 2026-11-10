namespace WithWindows.Core.Diagnostics;

public static class PathSafety
{
    public static bool IsWithinDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }
}
