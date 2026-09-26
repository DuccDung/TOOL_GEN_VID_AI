using System.Diagnostics;
using System.Text;
using TOOL_LOCAL.SystemSetup;

namespace TOOL_TESTS.SystemSetup;

public sealed class RuntimeUseGateProcessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Lease_BlocksOtherProcessesAndIsReleasedOnExit(bool exclusive, bool abruptExit)
    {
        var root = Path.Combine(Path.GetTempPath(), $"vm-lease-{Guid.NewGuid():N}");
        var gate = new RuntimeUseGate(root);
        using (gate.Acquire(true)) { }
        const string script = """
            $ErrorActionPreference = 'Stop'
            $share = if ($env:VM_TEST_LEASE_EXCLUSIVE -eq '1') { [IO.FileShare]::None } else { [IO.FileShare]::Read }
            $lease = [IO.File]::Open($env:VM_TEST_LEASE_PATH, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
            try {
                [Console]::WriteLine('LOCKED')
                [Console]::Out.Flush()
                [void][Console]::ReadLine()
            } finally { $lease.Dispose() }
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        start.Environment["VM_TEST_LEASE_PATH"] = Path.Combine(root, "runtime.lock");
        start.Environment["VM_TEST_LEASE_EXCLUSIVE"] = exclusive ? "1" : "0";
        using var child = Process.Start(start)!;
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.Equal("LOCKED", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("system_setup_busy", Assert.Throws<SetupException>(() => gate.Acquire(true)).Code);
            if (exclusive)
                Assert.Equal("system_setup_busy", Assert.Throws<SetupException>(() => gate.Acquire(false)).Code);
            else
                using (gate.Acquire(false)) { }
            if (abruptExit) child.Kill(entireProcessTree: true);
            else await child.StandardInput.WriteLineAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (!abruptExit) Assert.Equal(0, child.ExitCode);
            using var released = gate.Acquire(true);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await stderr;
            Directory.Delete(root, recursive: true);
        }
    }
}
