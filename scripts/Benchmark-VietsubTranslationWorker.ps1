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
$modelPath = Join-Path $resolvedRoot 'qwen3-4b-q4-k-m-cpu\bc64014-llamasharp-0.27.0-adapter-1\Qwen3-4B-Q4_K_M.gguf'
$expectedSize = 2497280256
$expectedHash = '7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5'

if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) {
    throw "Benchmark requested but the approved model is missing: $modelPath"
}
if ((Get-Item -LiteralPath $modelPath).Length -ne $expectedSize) {
    throw 'Benchmark requested but model size is invalid.'
}
if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
    throw 'Benchmark requested but model SHA-256 is invalid.'
}

$profiles = @(
    @{ Name = 'production-low-memory'; ResourceProfile = 'low-memory'; Context = 4096; MaxTokens = 768;  Batch = 128; UBatch = 64;  Threads = 4 },
    @{ Name = 'candidate-small-context'; ResourceProfile = 'low-memory'; Context = 2048; MaxTokens = 512;  Batch = 128; UBatch = 64;  Threads = 4 },
    @{ Name = 'candidate-balanced'; ResourceProfile = 'low-memory'; Context = 4096; MaxTokens = 512;  Batch = 128; UBatch = 64;  Threads = 4 },
    @{ Name = 'production-standard'; ResourceProfile = 'standard'; Context = 4096; MaxTokens = 1024; Batch = 256; UBatch = 64;  Threads = 8 },
    @{ Name = 'candidate-standard-large-output'; ResourceProfile = 'standard'; Context = 4096; MaxTokens = 1536; Batch = 256; UBatch = 128; Threads = 8 },
    @{ Name = 'candidate-large-context'; ResourceProfile = 'standard'; Context = 8192; MaxTokens = 1024; Batch = 256; UBatch = 128; Threads = 8 },
    @{ Name = 'candidate-maximum'; ResourceProfile = 'standard'; Context = 8192; MaxTokens = 1536; Batch = 512; UBatch = 128; Threads = 12 }
)

dotnet build (Join-Path $solutionRoot 'TOOL-TESTS\TOOL-TESTS.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Translation benchmark build failed.' }

$names = @(
    'VIDEOMAKER_RUN_TRANSLATION_BENCHMARKS',
    'VIDEOMAKER_TRANSLATION_COMPONENT_ROOT',
    'VIDEOMAKER_TRANSLATION_CONTEXT',
    'VIDEOMAKER_TRANSLATION_MAX_TOKENS',
    'VIDEOMAKER_TRANSLATION_BATCH',
    'VIDEOMAKER_TRANSLATION_UBATCH',
    'VIDEOMAKER_TRANSLATION_THREADS',
    'VIDEOMAKER_TRANSLATION_RESOURCE_PROFILE'
)
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

try {
    $env:VIDEOMAKER_RUN_TRANSLATION_BENCHMARKS = '1'
    $env:VIDEOMAKER_TRANSLATION_COMPONENT_ROOT = $resolvedRoot
    foreach ($profile in $profiles) {
        $env:VIDEOMAKER_TRANSLATION_CONTEXT = [string]$profile.Context
        $env:VIDEOMAKER_TRANSLATION_MAX_TOKENS = [string]$profile.MaxTokens
        $env:VIDEOMAKER_TRANSLATION_BATCH = [string]$profile.Batch
        $env:VIDEOMAKER_TRANSLATION_UBATCH = [string]$profile.UBatch
        $env:VIDEOMAKER_TRANSLATION_THREADS = [string]$profile.Threads
        $env:VIDEOMAKER_TRANSLATION_RESOURCE_PROFILE = [string]$profile.ResourceProfile
        Write-Host ("Benchmark name={0}, resource={1}, context={2}, maxTokens={3}, batch={4}, ubatch={5}, threads={6}" -f `
            $profile.Name, $profile.ResourceProfile, $profile.Context, $profile.MaxTokens, $profile.Batch, $profile.UBatch, $profile.Threads)
        dotnet test (Join-Path $solutionRoot 'TOOL-TESTS\TOOL-TESTS.csproj') `
            -c $Configuration `
            --no-build `
            --filter 'Category=LocalModelBenchmark' `
            --logger 'console;verbosity=normal'
        if ($LASTEXITCODE -ne 0) {
            throw 'Translation benchmark failed; do not enable the feature or claim a production profile.'
        }
    }
}
finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
