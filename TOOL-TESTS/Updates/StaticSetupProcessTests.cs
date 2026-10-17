extern alias SetupAssembly;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using Xunit;
using Xunit.Abstractions;

namespace TOOL_TESTS.Updates;

/// <summary>Real Windows job semantics, with tiny owned cmd/ping fixtures and loopback traffic only.</summary>
public sealed class StaticSetupProcessTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-job-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MaintenanceParentWaitReturnsWhenTheAcknowledgedProcessNoLongerExists()
    {
        var prepared = PrepareWait(int.MaxValue, DateTime.UtcNow.Ticks);
        await prepared.WaitForParentExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaintenanceParentWaitDoesNotKillAReusedPidOrTheLiveParentOnCancellation(bool mismatchedStartTime)
    {
        Directory.CreateDirectory(_root);
        using var parent = StartWaitingFixture("maintenance-parent");
        Process? child = null;
        try
        {
            File.WriteAllText(Path.Combine(_root, "maintenance-parent.ready"), "ready");
            child = await WaitForPingChildAsync(parent);
            var ticks = parent.StartTime.ToUniversalTime().Ticks;
            var prepared = PrepareWait(parent.Id, mismatchedStartTime ? ticks + 1 : ticks);
            using var cancellation = new CancellationTokenSource();
            var waiting = prepared.WaitForParentExitAsync(cancellation.Token);
            if (mismatchedStartTime) await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            else
            {
                Assert.False(waiting.IsCompleted, "The exact live parent must be awaited before installation.");
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            }
            Assert.False(parent.HasExited); Assert.False(child.HasExited);
        }
        finally { await StopFixtureAsync(parent); child?.Dispose(); }
    }

    private PreparedMaintenance PrepareWait(int parentId, long startTicks)
    {
        var hash = new string('a', 64);
        var release = new DesktopReleaseResponse(Guid.NewGuid(), "VideoMaker", "1.0.0", 7, "Stable", "win-x64", null, null,
            DateTime.UtcNow, "app.zip", "https://103.179.190.182:8443/apps/videomaker/app.zip", 100, hash,
            SourceKind: DesktopArtifactSourceKinds.ExternalVps, InstallMode: DesktopInstallModes.StaticArchive,
            DistributionProtocol: 2, ModelBundleId: ApprovedStaticModelBundle.Current.BundleId);
        var request = new DesktopMaintenanceRequest(1, Guid.NewGuid().ToString("N"), new string('c', 64), "repair", release,
            _root, "1.0.0", 7, hash, parentId, startTicks, "S-1-5-21-1000-1000-1000-1001", CreatedAtUtc: DateTime.UtcNow);
        return new(request, release);
    }

    [Fact]
    public async Task AbruptDeathOfTheJobOwnerParentKillsOnlyItsOwnedDescendants()
    {
        Directory.CreateDirectory(_root);
        var helper = Path.Combine(AppContext.BaseDirectory, "SetupJobOwnerHost", "VideoMaker.Setup.JobOwnerFixture.exe");
        Assert.True(File.Exists(helper), "Build must include the isolated Job owner fixture.");
        var script = WriteWaitingScript("parent-owned");
        var ready = Path.Combine(_root, "parent-owned.ready"); var receipt = Path.Combine(_root, "parent-owner.json");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _root };
        foreach (var argument in new[] { typeof(StaticBootstrapRunner).Assembly.Location, script, ready, receipt }) start.ArgumentList.Add(argument);
        using var parent = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated Job owner.");
        Process? owned = null; Process? ownedChild = null; Process? independent = null; Process? independentChild = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(receipt))
            {
                Assert.False(parent.HasExited, "Job owner exited before publishing its fixture receipt.");
                await Task.Delay(20, timeout.Token);
            }
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(receipt, timeout.Token));
            Assert.Equal(parent.Id, metadata.RootElement.GetProperty("ownerPid").GetInt32());
            owned = Process.GetProcessById(metadata.RootElement.GetProperty("childPid").GetInt32());
            ownedChild = await WaitForPingChildAsync(owned);
            independent = StartWaitingFixture("independent-parent-death");
            File.WriteAllText(Path.Combine(_root, "independent-parent-death.ready"), "independent");
            independentChild = await WaitForPingChildAsync(independent);
            output.WriteLine($"Abrupt-parent fixture PIDs: owner={parent.Id}, owned cmd={owned.Id}, ping={ownedChild.Id}; independent cmd={independent.Id}, ping={independentChild.Id}.");
            parent.Kill(entireProcessTree: false); // The production Job, not the test, must terminate descendants.
            await Task.WhenAll(parent.WaitForExitAsync(), owned.WaitForExitAsync(), ownedChild.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(owned.HasExited); Assert.True(ownedChild.HasExited);
            Assert.False(independent.HasExited); Assert.False(independentChild.HasExited);
        }
        finally
        {
            await StopFixtureAsync(parent);
            if (owned is not null) await StopFixtureAsync(owned);
            if (independent is not null) await StopFixtureAsync(independent);
            owned?.Dispose(); ownedChild?.Dispose(); independent?.Dispose(); independentChild?.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JobDisposalOrTerminationKillsItsTreeAndPreservesIndependentProcesses(bool terminateExplicitly)
    {
        Directory.CreateDirectory(_root);
        var stopwatch = Stopwatch.StartNew();
        var job = StaticBootstrapRunner.OwnedProcessJob.Create();
        using var owned = StartWaitingFixture("owned");
        Process? ownedChild = null;
        Process? independent = null;
        Process? independentChild = null;
        try
        {
            job.Assign(owned);
            File.WriteAllText(Path.Combine(_root, "owned.ready"), "owned");
            ownedChild = await WaitForPingChildAsync(owned);
            independent = StartWaitingFixture("independent");
            File.WriteAllText(Path.Combine(_root, "independent.ready"), "independent");
            independentChild = await WaitForPingChildAsync(independent);
            Assert.False(owned.HasExited); Assert.False(ownedChild.HasExited);
            Assert.False(independent.HasExited); Assert.False(independentChild.HasExited);
            output.WriteLine($"Fixture-owned PIDs: cmd={owned.Id}, ping={ownedChild.Id}; independent cmd={independent.Id}, ping={independentChild.Id}.");
            if (terminateExplicitly) job.Terminate(); else job.Dispose();
            await Task.WhenAll(owned.WaitForExitAsync(), ownedChild.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(owned.HasExited); Assert.True(ownedChild.HasExited);
            Assert.False(independent.HasExited); Assert.False(independentChild.HasExited);
            output.WriteLine($"Production Job Object {(terminateExplicitly ? "Terminate" : "Dispose")} verified in {stopwatch.ElapsedMilliseconds} ms; independent tree survived.");
        }
        finally
        {
            job.Dispose();
            await StopFixtureAsync(owned); if (independent is not null) await StopFixtureAsync(independent);
            ownedChild?.Dispose(); independentChild?.Dispose(); independent?.Dispose();
        }
    }

    private Process StartWaitingFixture(string name)
    {
        var script = WriteWaitingScript(name);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _root,
            // /s strips the outer pair. The remaining pair must still quote a script path
            // containing spaces; standard argv quoting alone is insufficient for cmd /c.
            Arguments = "/d /s /c \"\"" + script + "\"\""
        };
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated cmd fixture.");
    }

    private string WriteWaitingScript(string name)
    {
        var script = Path.Combine(_root, name + ".cmd");
        // Keep the batch source ASCII. cmd decodes a .cmd using its current code page;
        // %~dp0 expands the actual Unicode directory without embedding it in batch bytes.
        File.WriteAllText(script, "@echo off\r\n:wait_for_owner\r\nif not exist \"%~dp0" + name + ".ready\" goto wait_for_owner\r\n\"%SystemRoot%\\System32\\ping.exe\" -t 127.0.0.1 >nul\r\n", System.Text.Encoding.ASCII);
        return script;
    }

    private static async Task<Process> WaitForPingChildAsync(Process parent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (parent.HasExited) throw new InvalidOperationException("Isolated cmd fixture exited before its child started.");
            var child = FindPingChild(parent.Id); if (child is not null) return child;
            await Task.Delay(20, timeout.Token);
        }
    }

    private static Process? FindPingChild(int parentId)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) return null;
        do
        {
            if (entry.ParentProcessId == parentId && string.Equals(entry.Executable, "ping.exe", StringComparison.OrdinalIgnoreCase))
            {
                try { return Process.GetProcessById((int)entry.ProcessId); } catch (ArgumentException) { }
            }
        } while (Process32Next(snapshot, ref entry));
        return null;
    }

    private static async Task StopFixtureAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId; public UIntPtr DefaultHeapId; public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
