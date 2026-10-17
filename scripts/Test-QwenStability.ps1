param(
    [Parameter(Mandatory = $true)][string]$PublishRoot,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateRange(1, 10)][int]$ColdStarts = 3,
    [ValidateRange(30, 100)][int]$Iterations = 30,
    [switch]$AcceptResourceWarning
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PublishRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Stability output must be a new directory.' }
for ($folder = [IO.DirectoryInfo]::new($output); $null -ne $folder; $folder = $folder.Parent) {
    if ($folder.Exists -and ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Stability output cannot use a reparse point.'
    }
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$runs = @()
$passed = $false
try {
    for ($index = 1; $index -le $ColdStarts; $index++) {
        $resultPath = Join-Path $output ('cold-start-{0:D2}.json' -f $index)
        $report = & (Join-Path $PSScriptRoot 'Test-QwenOfflineBundle.ps1') -PublishRoot $root `
            -Iterations $Iterations -ResultPath $resultPath -AcceptResourceWarning:$AcceptResourceWarning
        $runs += [ordered]@{ ColdStart = $index; RuntimeProfileId = $report.RuntimeProfileId
            WorkerBinaryFingerprint = $report.WorkerBinaryFingerprint; RuntimeReady = $report.QwenOfflineReady
            CompletedIterations = $report.CompletedIterations; MemoryGrowthBytes = $report.PrivateMemoryGrowthBytes
            LastMetrics = $report.LastMetrics }
    }
    $passed = $runs.Count -eq $ColdStarts
} finally {
    [ordered]@{ CheckedAtUtc = [DateTime]::UtcNow.ToString('o'); Passed = $passed
        RequestedColdStarts = $ColdStarts; RequestedIterationsPerStart = $Iterations
        ResourceWarningAccepted = [bool]$AcceptResourceWarning; Runs = $runs
        NetworkDownloadsAllowed = $false; ServerAccessChecked = $false
        CustomerReleaseQualified = $false; OtherMachinesVerified = $false
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'qualification.json') -Encoding UTF8
}
if (-not $passed) { throw 'Qwen stability qualification did not complete.' }
Get-Content -LiteralPath (Join-Path $output 'qualification.json') -Raw | ConvertFrom-Json
