param(
    [Parameter(Mandatory = $true)][string]$PublishRoot,
    [string]$SourceWebRoot = (Join-Path $PSScriptRoot '..\TOOL-LOCAL\Web\dist'),
    [switch]$ProbeWebView2
)

$ErrorActionPreference = 'Stop'
$candidateRoot = [System.IO.Path]::GetFullPath($PublishRoot)
$sourceRoot = [System.IO.Path]::GetFullPath($SourceWebRoot)
$required = @(
    'TOOL-LOCAL.exe', 'appsettings.json', 'workers\piper_worker.py', 'workers\piper-requirements.lock',
    'workers\kokoro_worker.py', 'workers\kokoro-requirements.lock',
    'workers\voice_consistency_worker.py', 'workers\install_voice_consistency.ps1', 'workers\requirements.lock',
    'setup-fixtures\en.png', 'setup-fixtures\zh.png',
    'runtimes\win-x64\native\WebView2Loader.dll',
    'Sdcb.PaddleOCR.Models.LocalV5.dll', 'paddle_inference_c.dll', 'OpenCvSharpExtern.dll',
    'tools\ffmpeg\ffmpeg.exe', 'tools\ffmpeg\ffprobe.exe', 'tools\ffmpeg\checksums.sha256',
    'tools\ffmpeg\LICENSE.txt', 'tools\ffmpeg\PROVENANCE.md',
    '_updater\VideoMaker.Updater.exe',
    '_translation_worker\VideoMaker.Vietsub.TranslationWorker.exe',
    '_translation_worker\LLamaSharp.dll',
    '_translation_worker\runtimes\win-x64\native\avx2\ggml-cpu.dll',
    'third_party\translation\LICENSE-NVIDIA-CUDA-12.4.txt',
    '_translation_worker\VideoMaker.Vietsub.TranslationWorker.dll',
    '_translation_worker\VideoMaker.Vietsub.TranslationWorker.runtimeconfig.json',
    '_translation_worker\VideoMaker.Vietsub.TranslationWorker.deps.json',
    '_translation_worker\coreclr.dll', '_translation_worker\hostfxr.dll',
    'wwwroot\index.html'
)
foreach ($profile in @('avx2', 'avx', 'noavx')) {
    foreach ($library in @('llama.dll', 'ggml.dll', 'ggml-base.dll', 'ggml-cpu.dll')) {
        $required += "_translation_worker\runtimes\win-x64\native\$profile\$library"
    }
}
# Managed OCR assemblies may be inside a single-file desktop publish.
if (-not (Test-Path -LiteralPath (Join-Path $candidateRoot 'TOOL-LOCAL.dll'))) {
    $required = @($required | Where-Object { $_ -ne 'Sdcb.PaddleOCR.Models.LocalV5.dll' })
} else {
    $required += @('coreclr.dll', 'hostfxr.dll', 'TOOL-LOCAL.runtimeconfig.json', 'TOOL-LOCAL.deps.json')
}
foreach ($relative in $required) {
    $file = Join-Path $candidateRoot $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Publish candidate is missing a required Setup component: $relative"
    }
}
$loaderStream = [IO.File]::OpenRead((Join-Path $candidateRoot 'runtimes\win-x64\native\WebView2Loader.dll'))
$loaderReader = New-Object IO.BinaryReader($loaderStream)
try {
    if ($loaderStream.Length -lt 64 -or $loaderReader.ReadUInt16() -ne 0x5A4D) {
        throw 'Published WebView2Loader.dll is not a valid Windows x64 DLL.'
    }
    $loaderStream.Position = 0x3c
    $peOffset = $loaderReader.ReadInt32()
    if ($peOffset -lt 64 -or $peOffset -gt $loaderStream.Length - 24) {
        throw 'Published WebView2Loader.dll has an invalid PE header.'
    }
    $loaderStream.Position = $peOffset
    if ($loaderReader.ReadUInt32() -ne 0x4550 -or $loaderReader.ReadUInt16() -ne 0x8664) {
        throw 'Published WebView2Loader.dll must target Windows x64.'
    }
    $loaderStream.Position = $peOffset + 22
    if (($loaderReader.ReadUInt16() -band 0x2000) -eq 0) {
        throw 'Published WebView2Loader.dll is not a DLL.'
    }
} finally {
    $loaderReader.Dispose()
    $loaderStream.Dispose()
}
foreach ($privateFile in @('appsettings.user.json', 'preferences.json', 'device-id.bin', 'auth-token.bin', 'session.bin', 'tokens.bin')) {
    if (Test-Path -LiteralPath (Join-Path $candidateRoot $privateFile)) {
        throw 'Publish candidate includes private machine configuration or account data.'
    }
}
$webRoot = Join-Path $candidateRoot 'wwwroot'
$index = Get-Content -LiteralPath (Join-Path $webRoot 'index.html') -Raw -Encoding UTF8
$references = [regex]::Matches($index, '(?:src|href)="(?<path>/assets/[^"]+)"')
if ($references.Count -lt 2) { throw 'Publish index must reference the built JavaScript and CSS assets.' }
foreach ($reference in $references) {
    $relative = [Uri]::UnescapeDataString($reference.Groups['path'].Value).TrimStart('/')
    $target = [System.IO.Path]::GetFullPath((Join-Path $webRoot $relative))
    if (-not $target.StartsWith($webRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish index references an asset outside wwwroot.'
    }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Publish index references a missing asset: $relative" }
}
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File)
foreach ($source in $sourceFiles) {
    $relative = $source.FullName.Substring($sourceRoot.Length).TrimStart([char[]]@('\', '/'))
    $target = Join-Path $webRoot $relative
    if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or
        (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "Published web asset does not match the current build: $relative"
    }
}
$webView2State = 'NOT_CHECKED'
if ($ProbeWebView2) {
    # The dedicated command does not read deployment settings, log in, access SQL
    # or start models. PATH belongs only to this child; never change the machine PATH.
    $probeStart = New-Object Diagnostics.ProcessStartInfo
    $probeStart.FileName = Join-Path $candidateRoot 'TOOL-LOCAL.exe'
    $probeStart.Arguments = '--check-webview2'
    $probeStart.WorkingDirectory = [IO.Path]::GetTempPath()
    $probeStart.UseShellExecute = $false
    $probeStart.CreateNoWindow = $true
    $probeStart.RedirectStandardOutput = $true
    $probeStart.RedirectStandardError = $true
    $probeStart.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory + ';' + $env:SystemRoot
    $probeProcess = New-Object Diagnostics.Process
    $probeProcess.StartInfo = $probeStart
    try {
        if (-not $probeProcess.Start()) { throw 'Cannot start the published WebView2 probe.' }
        $probeOutput = $probeProcess.StandardOutput.ReadToEndAsync()
        $probeError = $probeProcess.StandardError.ReadToEndAsync()
        if (-not $probeProcess.WaitForExit(30000)) {
            $probeProcess.Kill()
            $probeProcess.WaitForExit()
            throw 'Published WebView2 probe timed out.'
        }
        try { $probeReport = $probeOutput.GetAwaiter().GetResult() | ConvertFrom-Json }
        catch { throw 'Published WebView2 probe did not return a valid diagnostic report.' }
        $null = $probeError.GetAwaiter().GetResult()
        $webView2State = $probeReport.WebView2.State
        $validReady = $probeProcess.ExitCode -eq 0 -and $webView2State -eq 'READY' -and
            [string]::IsNullOrEmpty($probeReport.WebView2.ErrorCode)
        $validRuntimeMissing = $probeProcess.ExitCode -eq 2 -and $webView2State -eq 'NOT_INSTALLED' -and
            $probeReport.WebView2.ErrorCode -eq 'webview2_runtime_missing'
        if (-not ($validReady -or $validRuntimeMissing)) {
            throw 'Published WebView2 loader failed with a Windows-only PATH. Do not distribute this package.'
        }
    } finally {
        $probeProcess.Dispose()
    }
}
[pscustomobject]@{ RequiredComponents = $required.Count; WebAssets = $sourceFiles.Count; MissingOrMismatched = 0; WebView2State = $webView2State }
