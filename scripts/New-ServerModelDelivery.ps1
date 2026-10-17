#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ModelHandoverPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-f]{64}$')][string]$HandoverInventorySha256,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot 'ModelDistributionHandover.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force -DisableNameChecking

function Get-DeliveryFiles([string]$Root) {
    $pending = New-Object 'Collections.Generic.Stack[string]'
    $pending.Push((Assert-ModelSafePath $Root))
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            $null = Assert-ModelSafePath $entry.FullName
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName) } else { $entry }
        }
    }
}

function Get-DeliverySourceSnapshot([string]$Repository) {
    $head = @(& git -C $Repository rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1 -or $head[0] -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot identify source commit.' }
    $branch = @(& git -C $Repository branch --show-current)
    $status = @(& git -C $Repository status --porcelain=1 --untracked-files=all)
    $paths = @(& git -C $Repository ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source identities.' }
    $files = New-Object 'Collections.Generic.List[object]'
    foreach ($relative in @($paths | Where-Object { Test-ModelSnapshotPath $_ } | Sort-Object -Unique)) {
        $source = Join-Path $Repository $relative
        if (-not [IO.File]::Exists($source)) { continue }
        $stamp = Get-ModelFileStamp $source
        $files.Add([pscustomobject]@{ path = $relative; sizeBytes = $stamp.sizeBytes; sha256 = $stamp.sha256 })
    }
    [pscustomobject]@{ schemaVersion = 1; baseCommit = $head[0]; branch = ($branch -join ''); uncommittedChanges = ($status.Count -gt 0)
        scope = 'Source identities only; deployment settings, secrets, caches and old builds excluded'; files = @($files.ToArray()) }
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($path in @($ModelHandoverPath, $OutputDirectory)) {
    if ($path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+[\\/])') {
        throw 'Delivery paths must be fully qualified Windows paths.'
    }
}
$handover = Assert-ModelSafePath $ModelHandoverPath
$output = Assert-ModelSafePath $OutputDirectory
if (Test-Path -LiteralPath $output) { throw 'Use a new delivery output directory; existing files are never overwritten.' }
foreach ($scope in @($repo, $handover)) {
    $prefix = $scope.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $outputPrefix = $output.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($output.Equals($scope, [StringComparison]::OrdinalIgnoreCase) -or
        $output.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        $scope.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Delivery output must be outside the checkout and model handover.'
    }
}
$inventoryPath = Join-Path $handover 'inventory.json'
if ((Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $HandoverInventorySha256) {
    throw 'The handover inventory identity does not match the independent pin.'
}
$verifiedHandover = Test-ModelHandoverInventory -Root $handover
$inventory = Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8 | ConvertFrom-Json
$candidateId = [string]$inventory.candidateId
if ($candidateId -cnotmatch '^[a-z0-9][a-z0-9.-]{1,100}$') { throw 'Invalid handover candidate ID.' }
$checksumPath = Assert-ModelSafePath (Join-Path $handover 'checksums.sha256')
if ((Get-Item -LiteralPath $checksumPath).Length -gt 4MB) { throw 'Handover checksum metadata is too large.' }
$checksumBytes = [IO.File]::ReadAllBytes($checksumPath)
$checksumAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $checksumStamp = [pscustomobject]@{ sizeBytes = $checksumBytes.LongLength; sha256 = ([BitConverter]::ToString($checksumAlgorithm.ComputeHash($checksumBytes))).Replace('-', '').ToLowerInvariant() } }
finally { $checksumAlgorithm.Dispose() }
$expectedChecksums = @{}
foreach ($entry in $inventory.files) { $expectedChecksums[$entry.path] = $entry.sha256 }
$expectedChecksums['inventory.json'] = $HandoverInventorySha256
$seenChecksums = @{}
foreach ($line in ([Text.Encoding]::UTF8.GetString($checksumBytes) -split '\r?\n' | Where-Object { $_.Length -gt 0 })) {
    if ($line -cnotmatch '^([0-9a-f]{64})  (.+)$') { throw 'Invalid handover checksum metadata.' }
    $hash = $Matches[1]; $relative = $Matches[2]
    if (-not $expectedChecksums.ContainsKey($relative) -or $seenChecksums.ContainsKey($relative) -or $expectedChecksums[$relative] -cne $hash) {
        throw 'Handover checksum metadata does not match the independently pinned inventory.'
    }
    $seenChecksums[$relative] = $true
}
if ($seenChecksums.Count -ne $expectedChecksums.Count) { throw 'Handover checksum metadata is incomplete.' }
$sourceFiles = @(Get-DeliveryFiles -Root $handover)
$modelBytes = [long]($sourceFiles | Measure-Object Length -Sum).Sum
# Stage, archive, full extraction and the separate server publish must coexist.
$outputDrive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($output))
if ($outputDrive.AvailableFreeSpace -lt 3L * $modelBytes + 2L * 1024 * 1024 * 1024) {
    throw 'Insufficient space for server/model staging, archive and full verification.'
}
$packageRoot = Join-Path $output 'package'
$extractedRoot = Join-Path $output 'verified-extraction'
$modelDestination = Join-Path $packageRoot ('models/' + $candidateId)
$maximumModelRelative = ($sourceFiles | ForEach-Object { $_.FullName.Length - $handover.Length - 1 } | Measure-Object -Maximum).Maximum
if ($modelDestination.Length + 1 + $maximumModelRelative -gt 245 -or
    (Join-Path $extractedRoot ('models/' + $candidateId)).Length + 1 + $maximumModelRelative -gt 245) {
    throw 'Choose a shorter delivery output path for Windows PowerShell staging and verification.'
}
[IO.Directory]::CreateDirectory($output) | Out-Null
$incomplete = Join-Path $output '.incomplete'
[IO.File]::WriteAllText($incomplete, 'Local packaging in progress. Not deployed.')
[IO.Directory]::CreateDirectory($packageRoot) | Out-Null
$sourceBefore = Get-DeliverySourceSnapshot $repo

$publishResults = @(& (Join-Path $PSScriptRoot 'Publish-ToolServerIis.ps1') `
    -OutputRoot (Join-Path $output 'server-build') -SkipArchive)
$publishObjects = @($publishResults | Where-Object { $null -ne $_.PSObject.Properties['PublishDirectory'] })
if ($publishObjects.Count -ne 1) { throw 'Server publish did not return one verified output directory.' }
$serverPublish = $publishObjects[0]
$serverDestination = Join-Path $packageRoot 'server'
foreach ($file in @(Get-DeliveryFiles -Root $serverPublish.PublishDirectory)) {
    $source = Assert-ModelSafePath $file.FullName
    $relative = $source.Substring($serverPublish.PublishDirectory.TrimEnd('\', '/').Length + 1)
    $stamp = Get-ModelFileStamp $source
    Copy-ModelVerifiedFile -Source $source -Destination (Join-Path $serverDestination $relative) `
        -ExpectedSize $stamp.sizeBytes -ExpectedSha256 $stamp.sha256 | Out-Null
}

# Preserve the entire immutable handover, including its OCR inputs and attribution.
# Do not unpack Piper, copy a venv/READY marker, or alter any handover metadata.
foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($handover.Length + 1).Replace('\', '/')
    Assert-ModelRelativePath $relative
    $entry = @($inventory.files | Where-Object { $_.path -ceq $relative })
    $stamp = if ($relative -ceq 'inventory.json') {
        [pscustomobject]@{ sizeBytes = (Get-Item -LiteralPath $inventoryPath).Length; sha256 = $HandoverInventorySha256 }
    } elseif ($relative -ceq 'checksums.sha256') { $checksumStamp
    } elseif ($entry.Count -eq 1) { $entry[0] } else { throw 'An unexpected model handover file appeared during packaging.' }
    Copy-ModelVerifiedFile -Source $file.FullName -Destination (Join-Path $modelDestination $relative) `
        -ExpectedSize $stamp.sizeBytes -ExpectedSha256 $stamp.sha256 | Out-Null
}
Test-ModelHandoverInventory -Root $modelDestination | Out-Null

$publisherRoot = Join-Path $packageRoot 'tools/model-publisher'
& dotnet publish (Join-Path $repo 'TOOL-MODEL-PUBLISHER/TOOL-MODEL-PUBLISHER.csproj') `
    -c Release -r win-x64 --self-contained false -p:UseAppHost=false -o $publisherRoot | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Offline model publisher build failed.' }

$deploymentRoot = Join-Path $packageRoot 'deployment'
[IO.Directory]::CreateDirectory($deploymentRoot) | Out-Null
$readmeTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ServerModelDelivery.README.vi.md'), [Text.Encoding]::UTF8)
$readme = $readmeTemplate.Replace('{{HANDOVER_CANDIDATE}}', $candidateId).Replace('{{INVENTORY_SHA256}}', $HandoverInventorySha256)
[IO.File]::WriteAllText((Join-Path $deploymentRoot 'README-DEPLOY.vi.md'), $readme, (New-Object Text.UTF8Encoding($false)))
Write-ModelDistributionJson -Path (Join-Path $deploymentRoot 'model-distribution.disabled.example.json') -Value ([ordered]@{
    ModelDistribution = [ordered]@{ Enabled = $false; StorageRoot = 'App_ModelDistribution'; RepositoryId = 'videomaker-models';
        Platform = 'win-x64'; AllowedChannels = @('Development'); PublicKeysPem = @{};
        TrustedCatalogSha256ByChannel = @{}; MinimumSequence = 1 }
})
$sourceAfter = Get-DeliverySourceSnapshot $repo
if (($sourceBefore | ConvertTo-Json -Depth 10 -Compress) -cne ($sourceAfter | ConvertTo-Json -Depth 10 -Compress)) {
    throw 'Source identities changed during publishing; incomplete output retained. Use a new output directory after freezing the checkout.'
}
Write-ModelDistributionJson -Path (Join-Path $deploymentRoot 'source-snapshot.json') -Value $sourceAfter
$deliveryStatus = [ordered]@{
    schemaVersion = 1; preparedAtUtc = [DateTime]::UtcNow.ToString('o'); baseCommit = $sourceAfter.baseCommit; sourceWorkingTreeUncommitted = $sourceAfter.uncommittedChanges
    target = 'IIS-win-x64'; serverDllSha256 = $serverPublish.ServerDllSha256; privateRuntimeVersion = $serverPublish.RuntimeVersion
    candidateId = $candidateId; handoverInventorySha256 = $HandoverInventorySha256; handoverFilesVerified = $verifiedHandover.filesVerified
    modelBytes = $modelBytes; modelDistributionEnabled = $false; modelRepositoryState = 'UnsignedHandover'
    modelCatalogSigned = $false; privateSigningKeyIncluded = $false; runtimeModelProbePerformed = $false
    serverStarted = $false; databaseContacted = $false; serverVerified = $false; deployed = $false; productionQualified = $false
}
Write-ModelDistributionJson -Path (Join-Path $deploymentRoot 'package-status.json') -Value $deliveryStatus
New-DesktopPackageInventory -PackageRoot $packageRoot
$archiveName = 'VideoMaker-Server-Models-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.zip'
$archivePath = Join-Path $output $archiveName
$partialArchive = $archivePath + '.part'
New-DesktopPackageZip -PackageRoot $packageRoot -DestinationPath $partialArchive
$archiveStamp = Get-ModelFileStamp $partialArchive
$extraction = Expand-DesktopPackageZip -ZipPath $partialArchive -DestinationRoot $extractedRoot
$packageCheck = Test-DesktopPackageInventory -PackageRoot $extractedRoot
$extractedHandover = Test-ModelHandoverInventory -Root (Join-Path $extractedRoot ('models/' + $candidateId))
Import-Module (Join-Path $PSScriptRoot 'ServerPackageSafety.psm1') -Force -DisableNameChecking
Assert-ServerPackageSafe -PublishDirectory (Join-Path $extractedRoot 'server') | Out-Null
if ((Get-FileHash -LiteralPath (Join-Path $extractedRoot 'server/TOOL-SERVER.dll') -Algorithm SHA256).Hash.ToLowerInvariant() -cne $serverPublish.ServerDllSha256) {
    throw 'Extracted server binary identity changed.'
}
[IO.File]::Move($partialArchive, $archivePath)
[IO.File]::WriteAllText((Join-Path $output 'checksums.sha256'), ($archiveStamp.sha256 + '  ' + $archiveName + [Environment]::NewLine), [Text.Encoding]::ASCII)
Write-ModelDistributionJson -Path (Join-Path $output 'verification-result.json') -Value ([ordered]@{
    schemaVersion = 1; archive = $archiveName; sizeBytes = $archiveStamp.sizeBytes; sha256 = $archiveStamp.sha256
    packageFilesVerified = $packageCheck.FilesVerified; modelFilesVerified = $extractedHandover.filesVerified
    extractedBytes = $extraction.ExtractedBytes; maximumModelRelativePath = $maximumModelRelative
    settingsSanitized = $true; modelCatalogSigned = $false; modelDistributionEnabled = $false
    serverStarted = $false; databaseContacted = $false; deployed = $false; productionQualified = $false
})
[IO.File]::Delete($incomplete)
[pscustomobject]@{ ArchivePath = $archivePath; PackageDirectory = $packageRoot; VerifiedExtraction = $extractedRoot;
    SizeBytes = $archiveStamp.sizeBytes; Sha256 = $archiveStamp.sha256; ModelCatalogSigned = $false; Deployed = $false }
