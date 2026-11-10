namespace WithWindows.Core.Scheduling;

public sealed class PythonEnvironmentSnapshot
{
    public PythonEnvironmentSnapshot(
        string path,
        string version,
        int bits,
        PythonInterpreterSource source,
        string? packageFamilyName)
    {
        Path = path;
        Version = version;
        Bits = bits;
        Source = source;
        PackageFamilyName = packageFamilyName;
    }

    public string Path { get; }
    public string Version { get; }
    public int Bits { get; }
    public PythonInterpreterSource Source { get; }
    public string? PackageFamilyName { get; }

    public string DisplayText
        => $"Python {Version}・x{Bits}・{SourceText}・{Path}";

    private string SourceText => Source switch
    {
        PythonInterpreterSource.Path => "PATH",
        PythonInterpreterSource.PyLauncher => "Python Launcher",
        PythonInterpreterSource.StoreAlias => "Microsoft Store",
        PythonInterpreterSource.Manual => "手动选择",
        _ => "未知来源",
    };
}
