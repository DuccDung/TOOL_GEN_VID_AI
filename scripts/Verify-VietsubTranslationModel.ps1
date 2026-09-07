param(
    [Parameter(Mandatory = $true)]
    [string]$ComponentRoot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$resolvedRoot = [System.IO.Path]::GetFullPath($ComponentRoot)
$engineVersion = 'bc64014-llamasharp-0.27.0-adapter-1'
$modelPath = Join-Path $resolvedRoot "qwen3-4b-q4-k-m-cpu\$engineVersion\Qwen3-4B-Q4_K_M.gguf"
$expectedSize = 2497280256
$expectedHash = '7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5'

if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) {
    throw "Real-model verification requested but the approved model is missing: $modelPath"
}

$model = Get-Item -LiteralPath $modelPath
if ($model.Length -ne $expectedSize) {
    throw "Real-model verification requested but model size is invalid."
}

$actualHash = (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "Real-model verification requested but model SHA-256 is invalid."
}

dotnet build (Join-Path $solutionRoot 'TOOL-TESTS\TOOL-TESTS.csproj') `
    -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Real-model verification build failed.' }

$previousRunFlag = $env:VIDEOMAKER_RUN_LOCAL_MODEL_TESTS
$previousComponentRoot = $env:VIDEOMAKER_TRANSLATION_COMPONENT_ROOT
try {
    $env:VIDEOMAKER_RUN_LOCAL_MODEL_TESTS = '1'
    $env:VIDEOMAKER_TRANSLATION_COMPONENT_ROOT = $resolvedRoot
    dotnet test (Join-Path $solutionRoot 'TOOL-TESTS\TOOL-TESTS.csproj') `
        -c $Configuration `
        --no-build `
        --filter 'Category=LocalModelIntegration' `
        --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw 'Real-model verification failed; READY must not be claimed.'
    }
}
finally {
    $env:VIDEOMAKER_RUN_LOCAL_MODEL_TESTS = $previousRunFlag
    $env:VIDEOMAKER_TRANSLATION_COMPONENT_ROOT = $previousComponentRoot
}
