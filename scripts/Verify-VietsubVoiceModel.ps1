param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$resolvedWorkspaceRoot = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$componentRoot = Join-Path $resolvedWorkspaceRoot 'vietsub\components\voice\piper'
$modelPath = Join-Path $componentRoot 'model\vi_VN-vais1000-medium.onnx'
$configPath = Join-Path $componentRoot 'model\vi_VN-vais1000-medium.onnx.json'
$expectedModelSize = 63201294
$expectedModelHash = 'ec7c89e2c85f4d1edc24b6120c18aaf1bda614f06b511567eb9c7c0de15e2dab'
$expectedConfigSize = 4860
$expectedConfigHash = 'fafb9da1354ed4b77c31af228ed41fb41cd825c14cffa105454b25e6ae751ee0'

foreach ($item in @(
    @{ Path = $modelPath; Size = $expectedModelSize; Hash = $expectedModelHash },
    @{ Path = $configPath; Size = $expectedConfigSize; Hash = $expectedConfigHash }
)) {
    if (-not (Test-Path -LiteralPath $item.Path -PathType Leaf)) {
        throw "Real Piper verification requested but an approved component is missing: $($item.Path)"
    }
    $file = Get-Item -LiteralPath $item.Path
    if ($file.Length -ne $item.Size) {
        throw "Real Piper verification requested but component size is invalid: $($item.Path)"
    }
    $actualHash = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $item.Hash) {
        throw "Real Piper verification requested but component SHA-256 is invalid: $($item.Path)"
    }
}

dotnet build (Join-Path $solutionRoot 'TOOL-TESTS\TOOL-TESTS.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Real Piper verification build failed.' }

$previousRunFlag = $env:VIDEOMAKER_RUN_LOCAL_VOICE_TESTS
$previousWorkspaceRoot = $env:VIDEOMAKER_VOICE_WORKSPACE_ROOT
try {
    $env:VIDEOMAKER_RUN_LOCAL_VOICE_TESTS = '1'
    $env:VIDEOMAKER_VOICE_WORKSPACE_ROOT = $resolvedWorkspaceRoot
    dotnet test (Join-Path $solutionRoot 'TOOL-TESTS\TOOL-TESTS.csproj') `
        -c $Configuration `
        --no-build `
        --filter 'Category=LocalVoiceIntegration' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw 'Real Piper verification failed; READY must not be claimed.'
    }
}
finally {
    $env:VIDEOMAKER_RUN_LOCAL_VOICE_TESTS = $previousRunFlag
    $env:VIDEOMAKER_VOICE_WORKSPACE_ROOT = $previousWorkspaceRoot
}
