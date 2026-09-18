[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ComponentRoot,
    [ValidateRange(1, 10)][int]$Repeats = 3,
    [ValidateSet(6, 12)][int]$MaximumTargetCues = 6,
    [string[]]$Cases = @('legacy:24', 'reuse:24', 'reuse:28', 'reuse:30', 'reuse:32'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '../artifacts/translation-optimization/benchmark.json'),
    [switch]$Soak,
    [ValidateRange(1, 200)][int]$SoakScenes = 100,
    [string]$SoakCase = 'reuse:32',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
foreach ($case in @($Cases) + @($SoakCase)) {
    if ($case -notmatch '^(legacy|reuse|prefix):(0|12|24|28|30|32|36)$') { throw "Unsupported benchmark case: $case" }
}
$repo = Split-Path $PSScriptRoot -Parent
$outputFile = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($outputFile)) | Out-Null
$variables = @{
    MSBUILDDISABLENODEREUSE = '1'
    VIDEOMAKER_RUN_TRANSLATION_PERFORMANCE_TESTS = '1'
    VIDEOMAKER_TRANSLATION_COMPONENT_ROOT = [IO.Path]::GetFullPath($ComponentRoot)
    VIDEOMAKER_TRANSLATION_PERFORMANCE_REPEATS = "$Repeats"
    VIDEOMAKER_TRANSLATION_PERFORMANCE_MAX_TARGETS = "$MaximumTargetCues"
    VIDEOMAKER_TRANSLATION_PERFORMANCE_CASES = ($Cases -join ',')
    VIDEOMAKER_TRANSLATION_PERFORMANCE_OUTPUT = $outputFile
    VIDEOMAKER_TRANSLATION_SOAK_SCENES = "$SoakScenes"
    VIDEOMAKER_TRANSLATION_SOAK_CASE = $SoakCase
}
$previous = @{}
try {
    foreach ($key in $variables.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $variables[$key], 'Process')
    }
    if (!$NoBuild) {
        & dotnet build (Join-Path $repo 'TOOL-TESTS/TOOL-TESTS.csproj') -c Release -m:1 -nr:false -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw 'Optimization benchmark build failed.' }
    }
    $filter = if ($Soak) { 'Category=TranslationOptimizationSoak' } else { 'Category=TranslationOptimizationModel' }
    $trx = if ($Soak) { 'optimization-soak.trx' } else { 'optimization-benchmark.trx' }
    # Runs alone, after build; verifies installed bytes without installing components or editing subtitle workspaces.
    & dotnet test (Join-Path $repo 'TOOL-TESTS/TOOL-TESTS.csproj') -c Release --no-build --filter $filter `
        --logger 'console;verbosity=normal' --logger "trx;LogFileName=$trx" `
        --results-directory ([IO.Path]::GetDirectoryName($outputFile)) -- xUnit.ParallelizeTestCollections=false
    if ($LASTEXITCODE -ne 0) { throw 'Optimization benchmark failed; inspect the JSON/TRX report.' }
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
}
