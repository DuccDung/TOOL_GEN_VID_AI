#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-z0-9][a-z0-9.-]{1,100}$')][string]$CandidateId,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$DesktopVersion,
    [Parameter(Mandatory = $true)][ValidateRange(1, [long]::MaxValue)][long]$DesktopBuildNumber,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]{0,39}$')][string]$Channel = 'Staging',
    [string]$QwenModelPath = '',
    [string]$PiperBundlePath = '',
    [string]$NugetPackagesRoot = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot 'ModelDistributionHandover.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force
$repo = Assert-ModelSafePath ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$output = Assert-ModelSafePath $OutputRoot
Assert-ModelRelativePath $CandidateId
if ($output.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($repo.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The model distribution handover must be created outside the source checkout.'
}
$candidate = Assert-ModelSafePath (Join-Path $output $CandidateId)
if (Test-Path -LiteralPath $candidate) { throw 'Model distribution candidate already exists; choose a new ID.' }
$qwen = Get-DesktopQwenDefinition
$qwenMetadata = Get-Content -LiteralPath (Join-Path $repo 'third_party/translation/COMPONENT_MANIFEST.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$piper = Get-Content -LiteralPath (Join-Path $repo 'third_party/voice/PIPER_OFFLINE_APPROVED.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$piperMetadata = Get-Content -LiteralPath (Join-Path $repo 'third_party/voice/COMPONENT_MANIFEST.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $QwenModelPath) { $QwenModelPath = Join-Path $repo $qwen.RelativePath }
if (-not $PiperBundlePath) { $PiperBundlePath = Join-Path $repo ('artifacts/piper-offline/' + $piper.bundleVersion) }
if (-not $NugetPackagesRoot) {
    $NugetPackagesRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
}
$QwenModelPath = Assert-ModelSafePath $QwenModelPath
$PiperBundlePath = Assert-ModelSafePath $PiperBundlePath
$NugetPackagesRoot = Assert-ModelSafePath $NugetPackagesRoot
$null = Test-DesktopQwenModel $QwenModelPath
& (Join-Path $PSScriptRoot 'Test-PiperOfflineBundle.ps1') -BundleDirectory $PiperBundlePath -ArtifactOnly | Out-Null
if ($piper.bundleVersion -cne $piperMetadata.runtime.bundleVersion -or $piper.schemaVersion -ne 1 -or
    $piper.platform -cne 'win-x64' -or $piperMetadata.componentId -cne 'piper-vi-vais1000-medium') {
    throw 'Piper metadata does not match the approved runtime bundle.'
}

function Get-SafeSourceSnapshot {
    $head = @(& git -C $repo rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1 -or $head[0] -cnotmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the source commit.' }
    $branch = @(& git -C $repo branch --show-current)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source branch.' }
    $status = @(& git -C $repo status --porcelain=1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the working tree state.' }
    $paths = @(& git -C $repo -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source identities.' }
    $files = @($paths | Sort-Object -Unique | Where-Object { Test-ModelSnapshotPath $_ } | ForEach-Object {
        Assert-ModelRelativePath $_
        $path = Assert-ModelSafePath (Join-Path $repo $_)
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $stamp = Get-ModelFileStamp $path
            [ordered]@{ path = $_; sizeBytes = $stamp.sizeBytes; sha256 = $stamp.sha256 }
        }
    })
    [ordered]@{
        schemaVersion = 1; branch = ($branch -join ''); commit = $head[0]; workingTreeDirty = ($status.Count -gt 0)
        scope = 'Allowlisted source identities only; no source bodies, user configuration, secrets, cache or build outputs.'
        files = $files
    }
}

$snapshot = Get-SafeSourceSnapshot
[xml]$workerProject = Get-Content -LiteralPath (Join-Path $repo 'TOOL-VIETSUB-TRANSLATION-WORKER/TOOL-VIETSUB-TRANSLATION-WORKER.csproj') -Raw -Encoding UTF8
$protocolSource = Get-Content -LiteralPath (Join-Path $repo 'TOOL-LOCAL/Vietsub/Translation/VietsubTranslationWorkerProtocol.cs') -Raw -Encoding UTF8
$protocolMatch = [regex]::Match($protocolSource, 'public const int Version = (\d+);')
$versionMatch = [regex]::Match($protocolSource, 'public const string WorkerVersion = "([0-9.]+)";')
$workerVersion = $workerProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $protocolMatch.Success -or -not $versionMatch.Success -or $workerVersion -cne $versionMatch.Groups[1].Value) {
    throw 'Translation worker project and protocol versions differ.'
}
$cpu = @($workerProject.SelectNodes('/Project/ItemGroup/PackageReference') | Where-Object { $_.GetAttribute('Include') -eq 'LLamaSharp.Backend.Cpu' })
if ($cpu.Count -ne 1 -or $cpu[0].Version -cne $qwenMetadata.runtime.version) { throw 'Translation backend version differs from model metadata.' }

$ocrPins = @()
[xml]$desktopProject = Get-Content -LiteralPath (Join-Path $repo 'TOOL-LOCAL/TOOL-LOCAL.csproj') -Raw -Encoding UTF8
foreach ($line in Get-Content -LiteralPath (Join-Path $repo 'third_party/ocr/NUGET_HASHES.sha512') -Encoding UTF8) {
    if (-not $line.Trim() -or $line.StartsWith('#')) { continue }
    if ($line -cnotmatch '^([A-Za-z0-9.]+)/([0-9.]+) ([A-Za-z0-9+/]{86}==)$') { throw 'Invalid OCR package pin.' }
    $packageId = $Matches[1]; $packageVersion = $Matches[2]; $packageHash = $Matches[3]
    $reference = @($desktopProject.SelectNodes('/Project/ItemGroup/PackageReference') | Where-Object { $_.GetAttribute('Include') -ceq $packageId })
    if ($reference.Count -ne 1 -or $reference[0].Version -cne $packageVersion) { throw 'OCR package reference differs from the pinned source.' }
    $fileName = $packageId.ToLowerInvariant() + '.' + $packageVersion + '.nupkg'
    $packagePath = Assert-ModelSafePath (Join-Path $NugetPackagesRoot ($packageId.ToLowerInvariant() + '/' + $packageVersion + '/' + $fileName))
    $stamp = Get-ModelFileStamp $packagePath -IncludeSha512
    if ($stamp.sha512Base64 -cne $packageHash) { throw 'OCR package archive SHA-512 mismatch.' }
    $ocrPins += [pscustomobject]@{ id = $packageId; version = $packageVersion; sha512Base64 = $packageHash; path = $packagePath; fileName = $fileName; stamp = $stamp }
}
if ($ocrPins.Count -ne 5 -or (@($ocrPins.id | Sort-Object -Unique)).Count -ne 5) { throw 'The main OCR handover requires exactly five pinned package archives.' }

[IO.Directory]::CreateDirectory($output) | Out-Null
$staging = Assert-ModelSafePath (Join-Path $output ($CandidateId + '.staging-' + [Guid]::NewGuid().ToString('N')))
[IO.Directory]::CreateDirectory($staging) | Out-Null
$inventoryFiles = New-Object 'Collections.Generic.List[object]'

function Add-HandoverFile {
    param([string]$SourcePath, [string]$RelativePath, [string]$ComponentId, [string]$ComponentVersion,
        [string]$ArtifactId, [string]$Role, $Origin, [string]$License, [long]$Size, [string]$Sha256, [string]$Sha512 = '')
    Assert-ModelRelativePath $RelativePath
    $stamp = Copy-ModelVerifiedFile -Source $SourcePath -Destination (Join-Path $staging $RelativePath) `
        -ExpectedSize $Size -ExpectedSha256 $Sha256 -ExpectedSha512Base64 $Sha512
    $entry = [ordered]@{
        path = $RelativePath; sizeBytes = $stamp.sizeBytes; sha256 = $stamp.sha256
        componentId = $ComponentId; componentVersion = $ComponentVersion; artifactId = $ArtifactId
        role = $Role; source = $Origin; license = $License
    }
    if ($Sha512) { $entry.sha512Base64 = $Sha512 }
    $inventoryFiles.Add([pscustomobject]$entry)
}

function Add-RepositoryEvidence([string]$SourceRelative, [string]$DestinationRelative, [string]$ComponentId, [string]$Role, [string]$License) {
    $path = Join-Path $repo $SourceRelative
    $stamp = Get-ModelFileStamp $path
    Add-HandoverFile $path $DestinationRelative $ComponentId '' '' $Role `
        ([ordered]@{ kind = 'repository'; path = $SourceRelative; commit = $snapshot.commit }) $License $stamp.sizeBytes $stamp.sha256
}

try {
    $qwenPath = 'artifacts/' + $qwenMetadata.componentId + '/' + $qwen.EngineVersion + '/' + $qwen.Sha256 + '/' + $qwenMetadata.model.fileName
    Add-HandoverFile $QwenModelPath $qwenPath $qwenMetadata.componentId $qwen.EngineVersion $qwenMetadata.componentId 'model-weights' `
        ([ordered]@{ repository = 'https://huggingface.co/' + $qwenMetadata.model.sourceRepository; revision = $qwenMetadata.model.sourceRevision }) `
        'Apache-2.0' $qwen.SizeBytes $qwen.Sha256
    $piperBase = 'artifacts/' + $piperMetadata.componentId + '/' + $piper.bundleVersion + '/'
    $piperArchiveRelative = $piperBase + $piper.archiveSha256 + '/piper-offline.zip'
    $piperManifestRelative = $piperBase + $piper.manifestSha256 + '/manifest.json'
    $piperOrigin = [ordered]@{ repository = 'https://huggingface.co/' + $piperMetadata.model.sourceRepository; revision = $piperMetadata.model.sourceRevision; runtime = 'https://github.com/OHF-Voice/piper1-gpl'; provenance = 'provenance/voice/PROVENANCE.md' }
    Add-HandoverFile (Join-Path $PiperBundlePath 'piper-offline.zip') $piperArchiveRelative $piperMetadata.componentId $piper.bundleVersion 'piper-offline-archive' 'offline-runtime-and-model' $piperOrigin `
        'GPL-3.0; CC-BY-4.0 model; Python/uv/wheel/MSVC notices inside payload' $piper.archiveSize $piper.archiveSha256
    Add-HandoverFile (Join-Path $PiperBundlePath 'manifest.json') $piperManifestRelative $piperMetadata.componentId $piper.bundleVersion 'piper-offline-manifest' 'offline-payload-manifest' $piperOrigin `
        'Distribution metadata; retain bundled upstream notices' $piper.manifestSize $piper.manifestSha256
    $manifest = Get-Content -LiteralPath (Join-Path $staging $piperManifestRelative) -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.bundleVersion -cne $piper.bundleVersion) { throw 'Piper manifest bundle version mismatch.' }
    $piperEntries = Test-ModelPiperArchive -ArchivePath (Join-Path $staging $piperArchiveRelative) -Manifest $manifest -ExpectedExpandedBytes $piper.expandedBytes
    foreach ($package in $ocrPins) {
        $relative = 'artifacts/paddleocr-en-zh-win-x64/3.3.1/' + $package.stamp.sha256 + '/' + $package.fileName
        Add-HandoverFile $package.path $relative 'paddleocr-en-zh-win-x64' '3.3.1' '' 'ocr-build-input' `
            ([ordered]@{ repository = 'https://api.nuget.org/v3-flatcontainer/' + $package.id.ToLowerInvariant(); packageId = $package.id; version = $package.version }) `
            'Apache-2.0; transitive dependency review required before public distribution' $package.stamp.sizeBytes $package.stamp.sha256 $package.sha512Base64
    }
    foreach ($spec in @(
        @('third_party/translation/COMPONENT_MANIFEST.json', 'provenance/translation/COMPONENT_MANIFEST.json', 'qwen3-4b-q4-k-m-cpu', 'component-definition', 'Distribution metadata'),
        @('third_party/translation/PROVENANCE.md', 'provenance/translation/PROVENANCE.md', 'qwen3-4b-q4-k-m-cpu', 'provenance', 'Retain upstream attribution'),
        @('third_party/translation/checksums.sha256', 'provenance/translation/checksums.sha256', 'qwen3-4b-q4-k-m-cpu', 'source-checksums', 'Distribution metadata'),
        @('third_party/translation/NUGET_HASHES.sha512', 'provenance/translation/NUGET_HASHES.sha512', 'qwen3-4b-q4-k-m-cpu', 'source-checksums', 'Distribution metadata'),
        @('third_party/translation/LICENSE-LLAMASHARP-AND-LLAMA.CPP-MIT.txt', 'licenses/translation/LICENSE-LLAMASHARP-AND-LLAMA.CPP-MIT.txt', 'qwen3-4b-q4-k-m-cpu', 'license', 'MIT'),
        @('third_party/ocr/LICENSE-APACHE-2.0.txt', 'licenses/Apache-2.0.txt', 'qwen-and-ocr', 'license', 'Apache-2.0'),
        @('third_party/ocr/PROVENANCE.md', 'provenance/ocr/PROVENANCE.md', 'paddleocr-en-zh-win-x64', 'provenance', 'Retain upstream attribution'),
        @('third_party/ocr/NUGET_HASHES.sha512', 'provenance/ocr/NUGET_HASHES.sha512', 'paddleocr-en-zh-win-x64', 'source-checksums', 'Distribution metadata'),
        @('third_party/ocr/MSVC_RUNTIME.json', 'provenance/ocr/MSVC_RUNTIME.json', 'paddleocr-en-zh-win-x64', 'native-runtime-definition', 'Microsoft redistribution terms'),
        @('third_party/ocr/MSVC-NOTICE.md', 'licenses/ocr/MSVC-NOTICE.md', 'paddleocr-en-zh-win-x64', 'license', 'Microsoft redistribution terms'),
        @('third_party/voice/COMPONENT_MANIFEST.json', 'provenance/voice/COMPONENT_MANIFEST.json', 'piper-vi-vais1000-medium', 'component-definition', 'Distribution metadata'),
        @('third_party/voice/PIPER_OFFLINE_APPROVED.json', 'provenance/voice/PIPER_OFFLINE_APPROVED.json', 'piper-vi-vais1000-medium', 'approved-definition', 'Distribution metadata'),
        @('third_party/voice/PROVENANCE.md', 'provenance/voice/PROVENANCE.md', 'piper-vi-vais1000-medium', 'provenance', 'Retain upstream attribution'),
        @('third_party/voice/checksums.sha256', 'provenance/voice/checksums.sha256', 'piper-vi-vais1000-medium', 'source-checksums', 'Distribution metadata'),
        @('third_party/voice/LICENSES.md', 'licenses/voice/LICENSES.md', 'piper-vi-vais1000-medium', 'license', 'Multiple; complete texts retained in original offline payload'),
        @('third_party/voice/MSVC-NOTICE.md', 'licenses/voice/MSVC-NOTICE.md', 'piper-vi-vais1000-medium', 'license', 'Microsoft redistribution terms'),
        @('TOOL-LOCAL/Vietsub/Voice/Workers/piper_worker.py', 'compatibility-inputs/piper_worker.py', 'piper-vi-vais1000-medium', 'worker-source', 'Project source; dependency licenses remain separate'),
        @('TOOL-LOCAL/SystemSetup/piper-requirements.lock', 'compatibility-inputs/piper-requirements.lock', 'piper-vi-vais1000-medium', 'requirements-lock', 'Retain wheel license notices'),
        @('third_party/ffmpeg/win-x64/PROVENANCE.md', 'provenance/media/PROVENANCE.md', 'desktop-media', 'provenance', 'Release approval not inferred'),
        @('third_party/ffmpeg/win-x64/LICENSE.txt', 'licenses/media/LICENSE.txt', 'desktop-media', 'license', 'See original FFmpeg license')
    )) { Add-RepositoryEvidence $spec[0] $spec[1] $spec[2] $spec[3] $spec[4] }

    $compatibility = [ordered]@{
        schemaVersion = 1; candidateId = $CandidateId
        desktop = [ordered]@{ version = $DesktopVersion; buildNumber = $DesktopBuildNumber; channel = $Channel; platform = 'win-x64'; runtimeQualified = $false }
        components = @(
            [ordered]@{ componentId = $qwenMetadata.componentId; componentVersion = $qwen.EngineVersion; artifactIds = @($qwenMetadata.componentId); modelSha256 = $qwen.Sha256; workerVersion = $workerVersion; protocolVersion = [int]$protocolMatch.Groups[1].Value; cpuBackend = 'LLamaSharp.Backend.Cpu'; cpuBackendVersion = $qwenMetadata.runtime.version; runtimeDelivery = 'matching-desktop-package'; gpuRequired = $false },
            [ordered]@{ componentId = $piperMetadata.componentId; componentVersion = $piper.bundleVersion; artifactIds = @('piper-offline-archive', 'piper-offline-manifest'); workerSha256 = $piper.workerSha256; requirementsSha256 = $piper.requirementsSha256; protocolVersion = $piper.protocolVersion; expandedBytes = $piper.expandedBytes; minimumOfflineInstallFreeBytes = $piper.minimumFreeDiskBytes; createFreshVenvOnTarget = $true },
            [ordered]@{ componentId = 'paddleocr-en-zh-win-x64'; componentVersion = '3.3.1'; runtimeDelivery = 'matching-desktop-package'; independentRuntimeDownloadSupported = $false; packages = @($ocrPins | ForEach-Object { [ordered]@{ id = $_.id; version = $_.version; sha512Base64 = $_.sha512Base64 } }) }
        )
        excluded = @('Qwen 1.7B', 'Kokoro optional model and voicepacks', 'CUDA/GPU acceleration', 'installed venv', 'cache', 'READY/probe markers')
        redistributionStatus = 'Staging only; public license/source-provision and Release approvals remain required.'
    }
    $verification = [ordered]@{
        schemaVersion = 1; candidateId = $CandidateId; state = 'ArtifactVerified'; createdAtUtc = [DateTime]::UtcNow.ToString('o')
        qwen = [ordered]@{ sizeAndHashVerified = $true; runtimeProbed = $false }
        piper = $piperEntries
        ocr = [ordered]@{ packageArchivesVerified = $ocrPins.Count; runtimeProbed = $false }
        serverVerified = $false; runtimeQualified = $false; published = $false
    }
    foreach ($document in @(
        @('source-snapshot.json', $snapshot, 'source-identities'),
        @('compatibility.json', $compatibility, 'compatibility'),
        @('qualification/artifact-verification.json', $verification, 'artifact-verification')
    )) {
        Write-ModelDistributionJson (Join-Path $staging $document[0]) $document[1]
        $stamp = Get-ModelFileStamp (Join-Path $staging $document[0])
        $inventoryFiles.Add([pscustomobject][ordered]@{ path = $document[0]; sizeBytes = $stamp.sizeBytes; sha256 = $stamp.sha256; componentId = 'distribution-evidence'; componentVersion = ''; artifactId = ''; role = $document[2]; source = [ordered]@{ kind = 'generated'; commit = $snapshot.commit }; license = 'Project distribution evidence' })
    }
    $afterSnapshot = Get-SafeSourceSnapshot
    if (($snapshot | ConvertTo-Json -Depth 8 -Compress) -cne ($afterSnapshot | ConvertTo-Json -Depth 8 -Compress)) {
        throw 'Source identities changed during handover; staging retained, create a new candidate.'
    }
    $inventory = [ordered]@{ schemaVersion = 1; candidateId = $CandidateId; state = 'ArtifactVerified'; createdAtUtc = [DateTime]::UtcNow.ToString('o'); excludes = @('inventory.json', 'checksums.sha256'); files = @($inventoryFiles | Sort-Object path) }
    Write-ModelDistributionJson (Join-Path $staging 'inventory.json') $inventory
    $inventoryStamp = Get-ModelFileStamp (Join-Path $staging 'inventory.json')
    $checksumLines = @($inventory.files | ForEach-Object { $_.sha256 + '  ' + $_.path }) + @($inventoryStamp.sha256 + '  inventory.json')
    [IO.File]::WriteAllLines((Join-Path $staging 'checksums.sha256'), [string[]]$checksumLines, (New-Object Text.UTF8Encoding($false)))
    $verified = Test-ModelHandoverInventory -Root $staging
    # Directory.Move rejects an existing destination. No customer candidate is overwritten.
    $null = Assert-ModelSafePath $staging
    $null = Assert-ModelSafePath $candidate
    [IO.Directory]::Move($staging, $candidate)
    [pscustomobject]@{ CandidateId = $CandidateId; CandidateDirectory = $candidate; InventorySha256 = $inventoryStamp.sha256; FilesVerified = $verified.filesVerified; PiperEntriesVerified = $piperEntries.filesVerified; RuntimeQualified = $false; ServerVerified = $false; Published = $false }
} catch {
    # Retain only this newly owned staging directory for diagnosis. Never remove other data.
    throw
}
