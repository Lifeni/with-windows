using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace WithWindows.Core.Interop;

/// <summary>显示配置（CCD）API 的薄封装，只做缓冲区管理与错误码转换，不做业务判断。</summary>
internal static class DisplayConfigInterop
{
    /// <summary>读取当前拓扑编号；失败返回 null。</summary>
    internal static unsafe uint? QueryCurrentTopology()
    {
        try
        {
            uint numPath = 0, numMode = 0;
            WIN32_ERROR sizeStatus = PInvoke.GetDisplayConfigBufferSizes(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_DATABASE_CURRENT,
                &numPath,
                &numMode);
            if (sizeStatus != WIN32_ERROR.ERROR_SUCCESS || numPath == 0)
                return null;

            var paths = new DISPLAYCONFIG_PATH_INFO[(int)numPath];
            var modes = new DISPLAYCONFIG_MODE_INFO[(int)numMode];
            uint pathCount = numPath;
            uint modeCount = numMode;
            DISPLAYCONFIG_TOPOLOGY_ID topology;

            fixed (DISPLAYCONFIG_PATH_INFO* pathPtr = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* modePtr = modes)
            {
                WIN32_ERROR status = PInvoke.QueryDisplayConfig(
                    QUERY_DISPLAY_CONFIG_FLAGS.QDC_DATABASE_CURRENT,
                    &pathCount,
                    pathPtr,
                    &modeCount,
                    modePtr,
                    &topology);

                return status == WIN32_ERROR.ERROR_SUCCESS ? (uint)topology : null;
            }
        }
        catch
        {
            // 查询失败按“无法判定”处理，调用方保守执行
            return null;
        }
    }

    /// <summary>按拓扑标志应用显示配置；失败抛 <see cref="InvalidOperationException"/>。</summary>
    internal static void ApplyTopology(SET_DISPLAY_CONFIG_FLAGS topology)
    {
        int hr = PInvoke.SetDisplayConfig(
            ReadOnlySpan<DISPLAYCONFIG_PATH_INFO>.Empty,
            ReadOnlySpan<DISPLAYCONFIG_MODE_INFO>.Empty,
            topology | SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY);

        if (hr != 0)
            throw new InvalidOperationException($"SetDisplayConfig 失败（Win32 错误 {hr}）");
    }
}
