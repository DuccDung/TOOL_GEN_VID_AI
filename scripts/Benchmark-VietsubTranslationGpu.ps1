[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ComponentRoot,
    [ValidateRange(1, 30)][int]$Repeats = 3,
    [string]$OutputPath = (Join-Path $PSScriptRoot '../artifacts/translation-gpu/benchmark.json'),
    [switch]$AcceptResourceWarning,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$outputFile = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null
$variables = @{
    MSBUILDDISABLENODEREUSE = '1'
    VIDEOMAKER_RUN_GPU_MODEL_TESTS = '1'
    VIDEOMAKER_TRANSLATION_COMPONENT_ROOT = [IO.Path]::GetFullPath($ComponentRoot)
    VIDEOMAKER_GPU_BENCHMARK_REPEATS = "$Repeats"
    VIDEOMAKER_GPU_BENCHMARK_OUTPUT = $outputFile
    VIDEOMAKER_GPU_ACCEPT_RESOURCE_WARNING = $(if ($AcceptResourceWarning) { '1' } else { '0' })
}
$previous = @{}
try {
    foreach ($key in $variables.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $variables[$key], 'Process')
    }
    if (!$NoBuild) {
        & dotnet build (Join-Path $repo 'TOOL-TESTS/TOOL-TESTS.csproj') -c Release -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'GPU benchmark build failed.' }
    }
    # Model test runs alone, after build. No subtitle workspace or paid provider is used.
    & dotnet test (Join-Path $repo 'TOOL-TESTS/TOOL-TESTS.csproj') -c Release --no-build `
        --filter 'Category=LocalGpuModel' --logger 'console;verbosity=normal' `
        --logger 'trx;LogFileName=gpu-benchmark.trx' --results-directory ([IO.Path]::GetDirectoryName($outputFile)) `
        -- xUnit.ParallelizeTestCollections=false
    if ($LASTEXITCODE -ne 0) { throw 'GPU benchmark failed; inspect results before enabling acceleration.' }
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
