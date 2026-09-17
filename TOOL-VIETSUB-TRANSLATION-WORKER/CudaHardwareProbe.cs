using System.Runtime.InteropServices;
using System.Text;
using TOOL_LOCAL.Vietsub.Translation;

namespace VideoMaker.Vietsub.Translation.Worker;

// Driver calls run only in the isolated worker, never in the desktop process.
internal static class CudaHardwareProbe
{
    public static VietsubTranslationHardwareResult Capture()
    {
        try
        {
            if (cuInit(0) != 0 || cuDriverGetVersion(out var version) != 0 || version < 12040)
                return new([], "TRANSLATION_GPU_DRIVER");
            if (nvmlInit_v2() != 0) return new([], "TRANSLATION_GPU_QUERY_UNAVAILABLE");
            try
            {
                var driver = new StringBuilder(96);
                if (nvmlSystemGetDriverVersion(driver, 96) != 0 || cuDeviceGetCount(out var count) != 0)
                    return new([], "TRANSLATION_GPU_QUERY_UNAVAILABLE");
                var devices = new List<VietsubTranslationGpuDevice>();
                for (var ordinal = 0; ordinal < Math.Min(count, 16); ordinal++)
                {
                    if (cuDeviceGet(out var device, ordinal) != 0) continue;
                    var uuid = new byte[16];
                    var name = new StringBuilder(256);
                    if (cuDeviceGetUuid(uuid, device) != 0 || cuDeviceGetName(name, 256, device) != 0
                        || cuDeviceGetAttribute(out var major, 75, device) != 0) continue;
                    var hex = Convert.ToHexString(uuid).ToLowerInvariant();
                    var id = $"GPU-{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
                    if (nvmlDeviceGetHandleByUUID(id, out var handle) != 0
                        || nvmlDeviceGetMemoryInfo(handle, out var memory) != 0) continue;
                    devices.Add(new(id, name.ToString(), ordinal, driver.ToString(), memory.Total, memory.Free, major));
                }
                return new(devices, devices.Count == 0 ? "TRANSLATION_GPU_UNAVAILABLE" : null);
            }
            finally { nvmlShutdown(); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new([], "TRANSLATION_GPU_UNAVAILABLE");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo { public ulong Total; public ulong Free; public ulong Used; }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll")] private static extern int cuInit(uint flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll")] private static extern int cuDriverGetVersion(out int version);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll")] private static extern int cuDeviceGetCount(out int count);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll")] private static extern int cuDeviceGet(out int device, int ordinal);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll")] private static extern int cuDeviceGetUuid([Out] byte[] uuid, int device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll", CharSet = CharSet.Ansi)] private static extern int cuDeviceGetName(StringBuilder name, int length, int device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvcuda.dll")] private static extern int cuDeviceGetAttribute(out int value, int attribute, int device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlInit_v2();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlShutdown();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int nvmlSystemGetDriverVersion(StringBuilder version, uint length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int nvmlDeviceGetHandleByUUID(string uuid, out IntPtr device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out MemoryInfo memory);
}
