using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TOOL_LOCAL.Vietsub.Voice;

// Closing the desktop (including a crash) closes this handle and terminates only
// the worker tree it owns. A new app cannot inherit an orphaned uv/Python writer.
internal sealed class PiperProcessTree : IDisposable
{
    private IntPtr _handle;
    internal PiperProcessTree(Process process)
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        try
        {
            if (_handle == IntPtr.Zero) throw new Win32Exception();
            var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(_handle, 9, ref limits, Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception();
            if (!AssignProcessToJobObject(_handle, process.Handle) && !process.HasExited) throw new Win32Exception();
        }
        catch
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } }
            finally { Dispose(); }
            throw;
        }
    }
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
