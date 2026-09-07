using System.Runtime.InteropServices;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationMemorySnapshot(
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    ulong TotalCommitBytes,
    ulong AvailableCommitBytes,
    uint MemoryLoadPercent,
    DateTime CapturedAtUtc);

internal interface IVietsubTranslationMemoryProbe
{
    bool TryCapture(out VietsubTranslationMemorySnapshot snapshot, out string? error);
}

internal sealed record VietsubTranslationResourceRequirements(
    ulong MinimumTotalPhysicalBytes,
    ulong MinimumAvailablePhysicalBytes,
    ulong MinimumAvailableCommitBytes)
{
    // These native-owned values are advisory thresholds. Standard remains the preferred
    // profile and LowMemory reduces the footprint. Falling below LowMemory requires an
    // explicit user confirmation, but is no longer a hard RAM admission floor.
    public static readonly VietsubTranslationResourceRequirements StandardCpu = new(
        MinimumTotalPhysicalBytes: 8UL * 1024 * 1024 * 1024,
        MinimumAvailablePhysicalBytes: 4UL * 1024 * 1024 * 1024,
        MinimumAvailableCommitBytes: 6UL * 1024 * 1024 * 1024);

    public static readonly VietsubTranslationResourceRequirements LowMemoryCpu = new(
        MinimumTotalPhysicalBytes: 6UL * 1024 * 1024 * 1024,
        MinimumAvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024,
        MinimumAvailableCommitBytes: 4UL * 1024 * 1024 * 1024);

    // Kept as a source-compatible alias for existing callers and tests.
    public static VietsubTranslationResourceRequirements SafeCpuBaseline => StandardCpu;
}

internal enum VietsubTranslationResourceFailure
{
    None,
    UnsupportedPlatform,
    SnapshotUnavailable,
    TotalPhysicalBelowMinimum,
    AvailablePhysicalBelowMinimum,
    AvailableCommitBelowMinimum
}

internal sealed record VietsubTranslationResourceEvaluation(
    bool CanLoad,
    VietsubTranslationResourceFailure Failure,
    string Message,
    VietsubTranslationMemorySnapshot? Snapshot,
    ulong BytesToRelease = 0,
    bool CanProceedWithConfirmation = false)
{
    public bool RequiresConfirmation => !CanLoad && CanProceedWithConfirmation;

    public bool IsBlocking => !CanLoad && !CanProceedWithConfirmation;
}

internal static class VietsubTranslationResourceGate
{
    public static VietsubTranslationResourceEvaluation Evaluate(
        IVietsubTranslationMemoryProbe memoryProbe,
        VietsubTranslationResourceRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(memoryProbe);
        ArgumentNullException.ThrowIfNull(requirements);

        if (!Environment.Is64BitProcess || !OperatingSystem.IsWindows())
        {
            return new VietsubTranslationResourceEvaluation(
                false,
                VietsubTranslationResourceFailure.UnsupportedPlatform,
                "Engine dịch local hiện chỉ hỗ trợ Windows 64-bit.",
                null);
        }

        if (!memoryProbe.TryCapture(out var snapshot, out _))
        {
            return new VietsubTranslationResourceEvaluation(
                false,
                VietsubTranslationResourceFailure.SnapshotUnavailable,
                "Không thể xác minh RAM và commit hiện còn trống. Bạn vẫn có thể tiếp tục, nhưng engine có thể chạy chậm, treo hoặc hết bộ nhớ.",
                null,
                CanProceedWithConfirmation: true);
        }

        if (snapshot.TotalPhysicalBytes < requirements.MinimumTotalPhysicalBytes)
        {
            return new VietsubTranslationResourceEvaluation(
                false,
                VietsubTranslationResourceFailure.TotalPhysicalBelowMinimum,
                $"Máy có {FormatGiB(snapshot.TotalPhysicalBytes)} GB RAM vật lý, thấp hơn mức khuyến nghị {FormatGiB(requirements.MinimumTotalPhysicalBytes)} GB. Bạn vẫn có thể tiếp tục nhưng engine có thể chạy chậm, treo hoặc hết bộ nhớ.",
                snapshot,
                requirements.MinimumTotalPhysicalBytes - snapshot.TotalPhysicalBytes,
                CanProceedWithConfirmation: true);
        }

        if (snapshot.AvailablePhysicalBytes < requirements.MinimumAvailablePhysicalBytes)
        {
            var missing = requirements.MinimumAvailablePhysicalBytes - snapshot.AvailablePhysicalBytes;
            return new VietsubTranslationResourceEvaluation(
                false,
                VietsubTranslationResourceFailure.AvailablePhysicalBelowMinimum,
                $"RAM trống hiện chỉ còn {FormatGiB(snapshot.AvailablePhysicalBytes)} GB, thấp hơn mức khuyến nghị {FormatGiB(requirements.MinimumAvailablePhysicalBytes)} GB. Bạn vẫn có thể tiếp tục nhưng engine có thể chạy chậm, treo hoặc hết bộ nhớ.",
                snapshot,
                missing,
                CanProceedWithConfirmation: true);
        }

        if (snapshot.AvailableCommitBytes < requirements.MinimumAvailableCommitBytes)
        {
            var missing = requirements.MinimumAvailableCommitBytes - snapshot.AvailableCommitBytes;
            return new VietsubTranslationResourceEvaluation(
                false,
                VietsubTranslationResourceFailure.AvailableCommitBelowMinimum,
                $"Commit khả dụng hiện chỉ còn {FormatGiB(snapshot.AvailableCommitBytes)} GB, thấp hơn mức khuyến nghị {FormatGiB(requirements.MinimumAvailableCommitBytes)} GB. Bạn vẫn có thể tiếp tục nhưng engine có thể chạy chậm, treo hoặc hết bộ nhớ.",
                snapshot,
                missing,
                CanProceedWithConfirmation: true);
        }

        return new VietsubTranslationResourceEvaluation(
            true,
            VietsubTranslationResourceFailure.None,
            $"Tài nguyên hiện tại đủ để nạp worker: RAM trống {FormatGiB(snapshot.AvailablePhysicalBytes)} GB, commit trống {FormatGiB(snapshot.AvailableCommitBytes)} GB.",
            snapshot);
    }

    internal static string FormatGiB(ulong bytes) =>
        (bytes / 1024d / 1024d / 1024d).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class WindowsVietsubTranslationMemoryProbe : IVietsubTranslationMemoryProbe
{
    public bool TryCapture(out VietsubTranslationMemorySnapshot snapshot, out string? error)
    {
        snapshot = new VietsubTranslationMemorySnapshot(0, 0, 0, 0, 0, DateTime.UtcNow);
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "unsupported_platform";
            return false;
        }

        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (!GlobalMemoryStatusEx(ref status))
        {
            error = $"win32_error_{Marshal.GetLastWin32Error()}";
            return false;
        }

        snapshot = new VietsubTranslationMemorySnapshot(
            status.TotalPhysical,
            status.AvailablePhysical,
            status.TotalPageFile,
            status.AvailablePageFile,
            status.MemoryLoad,
            DateTime.UtcNow);
        return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
