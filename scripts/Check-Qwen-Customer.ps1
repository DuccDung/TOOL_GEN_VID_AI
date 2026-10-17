param(
    [string]$PackageRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

# Native preflight only: this command never loads GGUF weights or runs inference.
# The test mode is set only for the child worker, never for the desktop application.
function Read-ExactBytes([IO.Stream]$Stream, [int]$Count) {
    $buffer = New-Object byte[] $Count
    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.ReadAsync($buffer, $offset, $Count - $offset)
        if (-not $read.Wait(15000)) { throw 'Worker read timed out.' }
        if ($read.Result -eq 0) { throw 'Worker closed output.' }
        $offset += $read.Result
    }
    return ,$buffer
}

function Read-WorkerFrame([IO.Stream]$Stream) {
    $header = Read-ExactBytes $Stream 4
    $count = [BitConverter]::ToInt32($header, 0)
    if ($count -lt 1 -or $count -gt 1048576) { throw 'Invalid worker frame length.' }
    $body = Read-ExactBytes $Stream $count
    return ([Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json)
}

function Write-WorkerFrame([IO.Stream]$Stream, $Envelope) {
    $body = [Text.Encoding]::UTF8.GetBytes(($Envelope | ConvertTo-Json -Depth 10 -Compress))
    $header = [BitConverter]::GetBytes([int]$body.Length)
    $Stream.Write($header, 0, 4)
    $Stream.Write($body, 0, $body.Length)
    $Stream.Flush()
}

$report = [ordered]@{
    Diagnostic = 'Qwen customer native preflight'
    ModelWeightsLoaded = $false
    ModelInferenceChecked = $false
    PackageFound = $false
    Windows = $null
    Cpu = $null
    Memory = $null
    Model = $null
    MsvcFiles = @()
    WorkerHello = $null
    NativePreflight = $null
    LoadedMsvcModules = @()
    WorkerOperationErrorType = $null
    WorkerExitCode = $null
    DiagnosticErrorType = $null
}
$process = $null
$started = $false
$stderrTask = $null

try {
    $root = (Resolve-Path -LiteralPath $PackageRoot).ProviderPath
    if (-not (Test-Path -LiteralPath (Join-Path $root 'TOOL-LOCAL.exe'))) {
        $candidates = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {
            Test-Path -LiteralPath (Join-Path $_.FullName 'TOOL-LOCAL.exe')
        })
        if ($candidates.Count -ne 1) { throw 'Specify the extracted application folder with -PackageRoot.' }
        $root = $candidates[0].FullName
    }
    $report.PackageFound = $true
    $os = Get-CimInstance Win32_OperatingSystem
    $report.Windows = $os.Caption
    $report.Cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name

    if (-not ('QwenCustomerMemoryProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class QwenCustomerMemoryProbe {
    [StructLayout(LayoutKind.Sequential)]
    public struct Status {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool GlobalMemoryStatusEx(ref Status status);
    public static Status Capture() {
        var status = new Status();
        status.Length = (uint)Marshal.SizeOf(typeof(Status));
        if (!GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("Memory snapshot failed.");
        return status;
    }
}
'@
    }
    $memory = [QwenCustomerMemoryProbe]::Capture()
    $report.Memory = [ordered]@{
        TotalRamGiB = [Math]::Round($memory.TotalPhysical / 1GB, 2)
        FreeRamGiB = [Math]::Round($memory.AvailablePhysical / 1GB, 2)
        FreeCommitGiB = [Math]::Round($memory.AvailablePageFile / 1GB, 2)
        MeetsStandardThresholds = ($memory.TotalPhysical -ge 8GB -and
            $memory.AvailablePhysical -ge 4GB -and $memory.AvailablePageFile -ge 6GB)
        MeetsLowMemoryThresholds = ($memory.TotalPhysical -ge 6GB -and
            $memory.AvailablePhysical -ge 3GB -and $memory.AvailablePageFile -ge 4GB)
    }

    $workerRoot = Join-Path $root '_translation_worker'
    $report.MsvcFiles = @(foreach ($name in @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll')) {
        $systemDll = Join-Path ([Environment]::SystemDirectory) $name
        [ordered]@{
            Name = $name
            DesktopRootPresent = (Test-Path -LiteralPath (Join-Path $root $name))
            WorkerRootPresent = (Test-Path -LiteralPath (Join-Path $workerRoot $name))
            System32Present = (Test-Path -LiteralPath $systemDll)
            System32Version = $(if (Test-Path -LiteralPath $systemDll) { (Get-Item -LiteralPath $systemDll).VersionInfo.FileVersion })
        }
    })

    $modelPath = Join-Path $root 'third_party\translation\runtime-test\qwen3-4b-q4-k-m-cpu\bc64014-llamasharp-0.27.0-adapter-1\Qwen3-4B-Q4_K_M.gguf'
    $modelPresent = Test-Path -LiteralPath $modelPath
    $modelSize = if ($modelPresent) { (Get-Item -LiteralPath $modelPath).Length } else { 0 }
    $modelHash = if ($modelPresent) { (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
    $report.Model = [ordered]@{
        Present = $modelPresent
        SizeBytes = $modelSize
        SizeVerified = ($modelSize -eq 2497280256)
        Sha256 = $modelHash
        HashVerified = ($modelHash -eq '7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5')
    }

    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $workerRoot 'VideoMaker.Vietsub.TranslationWorker.exe'
    $start.WorkingDirectory = $workerRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE'] = 'backend-preflight'
    $start.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory
    $start.EnvironmentVariables.Remove('CUDA_VISIBLE_DEVICES')
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    $started = $process.Start()
    if (-not $started) { throw 'Worker did not start.' }
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $hello = Read-WorkerFrame $process.StandardOutput.BaseStream
    $report.WorkerHello = $hello.payload
    if ($hello.type -ne 'hello' -or $hello.protocolVersion -ne 3) { throw 'Unexpected worker protocol.' }

    $payload = @{
        componentRoot = ''; modelPath = ''; componentId = 'qwen3-4b-q4-k-m-cpu'
        engineId = 'qwen3-4b-q4-k-m-cpu'; engineVersion = 'bc64014-llamasharp-0.27.0-adapter-1'
        modelFileName = 'Qwen3-4B-Q4_K_M.gguf'; modelSizeBytes = 2497280256; modelSha256 = ('0' * 64)
        config = @{
            contextSize = 4096; maximumGeneratedTokens = 1024; batchSize = 256; uBatchSize = 64
            threads = 4; gpuLayerCount = 0; useMemoryMap = $true
            avxPolicy = 'best-supported-cpu-up-to-avx2'; profileId = 'qwen3-cpu-safe-v2'
            promptProfileId = 'qwen3-vietsub-context-v2-no-think'; samplingProfileId = 'deterministic-gbnf-v1'
            executorMode = 'reuse-stateless-v1'
        }
        resourceRequirements = @{
            minimumTotalPhysicalBytes = 8589934592
            minimumAvailablePhysicalBytes = 4294967296
            minimumAvailableCommitBytes = 6442450944
        }
        resourceWarningAccepted = $false
    }
    Write-WorkerFrame $process.StandardInput.BaseStream @{
        protocolVersion = 3; type = 'load'; requestId = 'native-preflight'; payload = $payload; retryable = $false
    }
    $response = Read-WorkerFrame $process.StandardOutput.BaseStream
    $report.NativePreflight = [ordered]@{
        Passed = ($response.type -eq 'result')
        ErrorCode = $response.errorCode
        Backend = $response.payload.backendIdentity
        AvxLevel = $response.payload.avxLevel
        NativeLibraryHash = $response.payload.nativeLibraryHash
    }
    $report.LoadedMsvcModules = @($process.Modules | Where-Object {
        $_.ModuleName -match '^(msvcp140|vcruntime140|vcruntime140_1|vcomp140)\.dll$'
    } | ForEach-Object { [ordered]@{ Name = $_.ModuleName; FileName = $_.FileName } })

    Write-WorkerFrame $process.StandardInput.BaseStream @{
        protocolVersion = 3; type = 'shutdown'; requestId = 'shutdown'; retryable = $false
    }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) { throw 'Worker shutdown timed out.' }
} catch {
    # Exception messages and raw worker stderr are intentionally excluded.
    $report.DiagnosticErrorType = $_.Exception.GetType().Name
} finally {
    if ($started) {
        if (-not $process.HasExited) {
            $process.Kill()
            [void]$process.WaitForExit(5000)
        }
        if ($process.HasExited) { $report.WorkerExitCode = $process.ExitCode }
        if ($stderrTask -and $stderrTask.Wait(2000)) {
            $errorMatch = [regex]::Match($stderrTask.Result, 'operation_error:([A-Za-z0-9_]{1,80})')
            if ($errorMatch.Success) { $report.WorkerOperationErrorType = $errorMatch.Groups[1].Value }
        }
    }
    if ($process) { $process.Dispose() }
}

$report | ConvertTo-Json -Depth 8
if ($report.DiagnosticErrorType -or -not $report.NativePreflight.Passed -or -not $report.Model.HashVerified) { exit 2 }
exit 0
