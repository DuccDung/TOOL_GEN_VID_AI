param(
    [Parameter(Mandatory = $true)][string]$PublishRoot
)

$ErrorActionPreference = 'Stop'
$bundleRoot = [IO.Path]::GetFullPath($PublishRoot)
& (Join-Path $PSScriptRoot 'Test-OcrNativeRuntime.ps1') -BundleDirectory $bundleRoot | Out-Null
$probeStart = New-Object Diagnostics.ProcessStartInfo
$probeStart.FileName = Join-Path $bundleRoot 'TOOL-LOCAL.exe'
$probeStart.Arguments = '--check-bundled-components'
$probeStart.WorkingDirectory = [IO.Path]::GetTempPath()
$probeStart.UseShellExecute = $false
$probeStart.CreateNoWindow = $true
$probeStart.RedirectStandardOutput = $true
$probeStart.RedirectStandardError = $true
$probeStart.EnvironmentVariables['PATH'] = [Environment]::SystemDirectory + ';' + $env:SystemRoot
$probeProcess = New-Object Diagnostics.Process
$probeProcess.StartInfo = $probeStart
try {
    if (-not $probeProcess.Start()) { throw 'Cannot start the bundled component probe.' }
    $probeOutput = $probeProcess.StandardOutput.ReadToEndAsync()
    $probeError = $probeProcess.StandardError.ReadToEndAsync()
    if (-not $probeProcess.WaitForExit(210000)) {
        # This process was started by this script, not a user's desktop or IDE.
        $probeProcess.Kill()
        $probeProcess.WaitForExit()
        throw 'Bundled component probe timed out. Do not distribute this ZIP.'
    }
    $null = $probeError.GetAwaiter().GetResult()
    try { $probeReport = $probeOutput.GetAwaiter().GetResult() | ConvertFrom-Json }
    catch { throw 'Bundled component probe did not return a valid report. Do not distribute this ZIP.' }
    $components = @($probeReport.Components)
    $media = @($components | Where-Object { $_.Id -eq 'media' -and $_.State -eq 'READY' -and -not $_.ErrorCode })
    $ocr = @($components | Where-Object { $_.Id -eq 'ocr' -and $_.State -eq 'READY' -and -not $_.ErrorCode })
    $definition = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../third_party/ocr/MSVC_RUNTIME.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $nativeModules = @($probeReport.OcrNativeRuntimeModules)
    $nativeReady = $nativeModules.Count -eq $definition.files.Count -and
        @($nativeModules | Where-Object { $_.AppLocal -ne $true }).Count -eq 0 -and
        @(Compare-Object @($definition.files.name | Sort-Object) @($nativeModules.Name | Sort-Object)).Count -eq 0
    if ($probeProcess.ExitCode -ne 0 -or $probeReport.BundledComponentsReady -ne $true -or
        $probeReport.WebView2.State -ne 'READY' -or $media.Count -ne 1 -or $ocr.Count -ne 1 -or
        $probeReport.ServerAccessChecked -ne $false -or $probeReport.InstalledVoiceOrTranslationChecked -ne $false -or -not $nativeReady) {
        # Return only allowlisted metadata, never native stderr or exception/path text.
        $codes = @($components | Where-Object { $_.Id -in @('media', 'ocr') -and $_.ErrorCode -match '^[A-Za-z0-9_]{1,80}$' } |
            ForEach-Object { $_.Id + ':' + $_.ErrorCode }) -join ', '
        throw "Bundled component probe failed ($codes). Do not distribute this ZIP."
    }
    [pscustomobject]@{ AppVersion = $probeReport.AppVersion; BuildNumber = $probeReport.BuildNumber;
        WebView2 = 'READY'; Media = 'READY'; OcrEnglishChinese = 'READY'; OcrNativeRuntimeAppLocal = $nativeReady; ServerAccessChecked = $false }
}
finally { $probeProcess.Dispose() }
