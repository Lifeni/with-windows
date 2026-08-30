using System.Runtime.InteropServices;

namespace WithWindows.Interop;

// DISPLAYCONFIG_PATH_INFO / MODE_INFO：仅作为 QueryDisplayConfig 的缓冲区类型，
// 拓扑判定直接使用系统返回的 DISPLAYCONFIG_TOPOLOGY_ID，不读取路径字段。
// 各段字节数为 wingdi.h 权威定义（见注释），必须与原生布局完全一致，否则系统写入越界。

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    // DISPLAYCONFIG_PATH_SOURCE_INFO： adapterId（8） + id（4） + modeInfoIdx（4） + statusFlags（4） = 20
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] SourceInfo;

    // DISPLAYCONFIG_PATH_TARGET_INFO： adapterId（8） + id（4） + modeInfoIdx（4） + outputTechnology（4）
    //   + rotation（4） + scaling（4） + refreshRate（8） + scanLineOrdering（4） + targetAvailable（4） + statusFlags（4） = 48
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
    public byte[] TargetInfo;

    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigModeInfo
{
    public uint InfoType;
    public uint Id;
    public uint AdapterIdLow;
    public uint AdapterIdHigh;

    // 联合： DISPLAYCONFIG_TARGET_MODE（48，含 VIDEO_SIGNAL_INFO 的 8+8+8+8+8+4+4）
    //   vs DISPLAYCONFIG_SOURCE_MODE（20），取较大者 48
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
    public byte[] ModeInfo;
}
