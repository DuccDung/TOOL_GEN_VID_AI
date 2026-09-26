using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Own only the subprocess tree started by Test-Stability.ps1. Closing this job
// releases that tree even if a test host crashes or leaves a browser running.
public sealed class StabilityProcessJob : IDisposable
{
    private IntPtr handle;
    public StabilityProcessJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception();
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (!SetInformationJobObject(handle, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits))))
        {
            var error = new Win32Exception();
            Dispose();
            throw error;
        }
    }
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.Handle)) throw new Win32Exception();
    }
    public uint ActiveProcesses
    {
        get
        {
            Accounting info;
            if (!QueryInformationJobObject(handle, 1, out info, Marshal.SizeOf(typeof(Accounting)), IntPtr.Zero))
                throw new Win32Exception();
            return info.ActiveProcesses;
        }
    }
    public void Dispose()
    {
        if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
    }
    public ulong PeakCommittedBytes
    {
        get
        {
            ExtendedLimits info;
            if (!QueryInformationJobObject(handle, 9, out info, Marshal.SizeOf(typeof(ExtendedLimits)), IntPtr.Zero))
                throw new Win32Exception();
            return info.PeakJobMemory.ToUInt64();
        }
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
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    {
        public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out Accounting info, int length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out ExtendedLimits info, int length, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
