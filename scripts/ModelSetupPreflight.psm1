#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot 'ModelDistributionHandover.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'DesktopOfflinePackage.psm1') -Force -DisableNameChecking

function Read-PreflightJson([string]$Path) {
    $safe = Assert-ModelSafePath $Path
    if (-not [IO.File]::Exists($safe) -or (Get-Item -LiteralPath $safe).Length -gt 1048576) {
        throw 'Preflight JSON is missing or exceeds the size limit.'
    }
    try { Get-Content -LiteralPath $safe -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop }
    catch { throw 'Preflight JSON cannot be parsed. Its contents are not included in diagnostics.' }
}

function Get-PreflightSetting($Settings, [string]$Group, [string]$Name, $Default) {
    if ($null -eq $Settings) { return $Default }
    $groupProperty = $Settings.PSObject.Properties[$Group]
    if ($null -eq $groupProperty -or $null -eq $groupProperty.Value) { return $Default }
    $property = $groupProperty.Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    $property.Value
}

function Get-StaticModelPreflightDefinition {
    # Source inventory only: never load an application assembly or accept pins from settings.
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../TOOL-DISTRIBUTION/StaticModels/ApprovedStaticModelBundle.cs') -Raw -Encoding UTF8
    $pattern = 'Current\s*\{\s*get;\s*\}\s*=\s*new\(\s*"(?<id>[A-Za-z0-9.-]+)"\s*,\s*new Uri\("(?<url>https://[^"\s]+)"\)\s*,\s*(?<size>[0-9_]+)\s*,\s*"(?<sha>[0-9a-f]{64})"\s*,\s*"(?<inventory>[0-9a-f]{64})"\s*,\s*(?<expanded>[0-9_]+)\s*,\s*ApprovedModelArtifactPins\.All\s*\)'
    $matches = [regex]::Matches($source, $pattern)
    if ($matches.Count -ne 1) { throw 'Cannot read the pinned StaticArchive source definition.' }
    $match = $matches[0]
    $url = [Uri]$match.Groups['url'].Value
    if ($url.Scheme -cne 'https' -or $url.UserInfo -or $url.Query -or $url.Fragment) {
        throw 'Invalid pinned StaticArchive source definition.'
    }
    [pscustomobject]@{ bundleId = $match.Groups['id'].Value; archiveUrl = $url.AbsoluteUri
        archiveSizeBytes = [long]$match.Groups['size'].Value.Replace('_', ''); archiveSha256 = $match.Groups['sha'].Value
        inventorySha256 = $match.Groups['inventory'].Value; maximumExpandedBytes = [long]$match.Groups['expanded'].Value.Replace('_', '') }
}

function Get-ModelSetupPreflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ApplicationRoot,
        [string]$CurrentDirectory = (Get-Location).ProviderPath,
        [string]$LocalApplicationData = [Environment]::GetFolderPath('LocalApplicationData'),
        [string[]]$FixedDriveRoots
    )
    # This is an inventory only. Never starts the desktop, loads a model, writes a marker,
    # follows a reparse point, or returns a destructive command/authorization.
    $app = Assert-ModelSafePath ([IO.Path]::GetFullPath($ApplicationRoot))
    $cwd = Assert-ModelSafePath ([IO.Path]::GetFullPath($CurrentDirectory))
    $local = Assert-ModelSafePath ([IO.Path]::GetFullPath($LocalApplicationData))
    $baseSettings = Read-PreflightJson (Join-Path $app 'appsettings.json')
    $userSettings = $null
    $userSettingsPath = Join-Path $app 'appsettings.user.json'
    if ([IO.File]::Exists($userSettingsPath)) { $userSettings = Read-PreflightJson $userSettingsPath }
    foreach ($settings in @($baseSettings, $userSettings)) {
        if ($null -eq $settings) { continue }
        foreach ($group in @('Storage', 'Update', 'ModelDistribution', 'Server', 'Features')) {
            $property = $settings.PSObject.Properties[$group]
            if ($null -ne $property -and ($null -eq $property.Value -or $property.Value.GetType() -ne [System.Management.Automation.PSCustomObject])) {
                throw 'A deployment settings section has an invalid type.'
            }
        }
    }
    $effective = @{}
    foreach ($entry in @(
        @('Storage', 'WorkspaceRoot', ''), @('Update', 'Channel', 'Stable'),
        @('ModelDistribution', 'Mode', 'Server'), @('ModelDistribution', 'Enabled', $false), @('ModelDistribution', 'BaseUrl', $null),
        @('ModelDistribution', 'CacheRoot', $null), @('Server', 'BaseUrl', ''),
        @('Features', 'VietsubEnabled', $false), @('Features', 'VietsubOcrEnabled', $false),
        @('Features', 'VietsubLocalTranslationEnabled', $false), @('Features', 'VietsubLocalVoiceEnabled', $true)
    )) {
        $value = Get-PreflightSetting $baseSettings $entry[0] $entry[1] $entry[2]
        $value = Get-PreflightSetting $userSettings $entry[0] $entry[1] $value
        if (($entry[2] -is [bool] -and $value -isnot [bool]) -or
            ($entry[2] -isnot [bool] -and $null -ne $value -and $value -isnot [string])) {
            throw 'A deployment settings value has an invalid type.'
        }
        $effective[$entry[0] + '.' + $entry[1]] = $value
    }
    $blockers = New-Object 'Collections.Generic.List[string]'
    $mode = [string]$effective['ModelDistribution.Mode']
    if ($mode -cnotin @('Server', 'StaticArchive')) { throw 'Unsupported model delivery mode.' }
    $staticMode = $mode -ceq 'StaticArchive'
    $staticDefinition = $null
    if ($staticMode) {
        $staticDefinition = Get-StaticModelPreflightDefinition
        if ($effective['ModelDistribution.Enabled'] -or
            -not [string]::IsNullOrWhiteSpace([string]$effective['ModelDistribution.BaseUrl']) -or
            -not [string]::IsNullOrWhiteSpace([string]$effective['ModelDistribution.CacheRoot'])) {
            $blockers.Add('STATIC_ARCHIVE_SETTINGS_OVERRIDE_NOT_ALLOWED')
        }
    }
    $components = Join-Path $local 'ToolGenPostVideo/components'
    $metadataRoot = Join-Path $local 'ToolGenPostVideo/system-setup'
    $cache = if ($staticMode) { Join-Path $metadataRoot 'static-model-cache' } else { $effective['ModelDistribution.CacheRoot'] }
    # Match DesktopModelDistributionClient: an override is resolved against the desktop CWD,
    # not expanded as a workspace environment variable.
    if ($null -eq $cache) { $cache = Join-Path $components 'model-downloads' }
    if ([string]::IsNullOrWhiteSpace([string]$cache)) { throw 'CacheRoot cannot be empty when explicitly configured.' }
    if (-not [IO.Path]::IsPathRooted([string]$cache)) { $cache = Join-Path $cwd ([string]$cache) }
    $cache = Assert-ModelSafePath ([IO.Path]::GetFullPath([string]$cache))
    $workspace = [Environment]::ExpandEnvironmentVariables(([string]$effective['Storage.WorkspaceRoot']).Trim())
    if ([string]::IsNullOrWhiteSpace($workspace)) { throw 'WorkspaceRoot is required for a complete inventory.' }
    if (-not [IO.Path]::IsPathRooted($workspace)) { $workspace = Join-Path $app $workspace }
    $workspace = Assert-ModelSafePath ([IO.Path]::GetFullPath($workspace))
    $qwen = Get-DesktopQwenDefinition
    $qwenMetadata = Read-PreflightJson (Join-Path $PSScriptRoot '../third_party/translation/COMPONENT_MANIFEST.json')
    $piper = Read-PreflightJson (Join-Path $PSScriptRoot '../third_party/voice/PIPER_OFFLINE_APPROVED.json')
    $publicRoot = Read-PreflightJson (Join-Path $PSScriptRoot '../TOOL-LOCAL/ModelDistribution/model-trust.json')
    $artifacts = @(
        [pscustomobject]@{ name = $qwenMetadata.model.fileName; sha256 = $qwen.Sha256; sizeBytes = $qwen.SizeBytes },
        [pscustomobject]@{ name = 'piper-offline.zip'; sha256 = $piper.archiveSha256; sizeBytes = $piper.archiveSize },
        [pscustomobject]@{ name = 'manifest.json'; sha256 = $piper.manifestSha256; sizeBytes = $piper.manifestSize }
    )
    $candidates = New-Object 'Collections.Generic.List[object]'
    $seen = @{}
    function Add-Candidate([string]$Kind, [string]$Path) {
        $full = Assert-ModelSafePath ([IO.Path]::GetFullPath($Path))
        if ($seen.ContainsKey($full)) { return }
        $seen[$full] = $true
        $exists = Test-Path -LiteralPath $full
        $size = $null
        if ($exists -and [IO.File]::Exists($full)) { $size = (Get-Item -LiteralPath $full).Length }
        $candidates.Add([pscustomobject]@{ kind = $Kind; path = $full; exists = [bool]$exists; sizeBytes = $size })
    }
    $qwenRelative = Join-Path $qwenMetadata.componentId $qwen.EngineVersion
    $qwenRoots = @((Join-Path $components 'vietsub-translation'))
    if ($null -eq $FixedDriveRoots) {
        $FixedDriveRoots = @([IO.DriveInfo]::GetDrives() | Where-Object { $_.IsReady -and $_.DriveType -eq 'Fixed' } | ForEach-Object { $_.RootDirectory.FullName })
    }
    foreach ($drive in $FixedDriveRoots) { $qwenRoots += Join-Path $drive 'VideoMakerData/components/vietsub-translation' }
    foreach ($root in $qwenRoots) { Add-Candidate 'qwen-component' (Join-Path $root $qwenRelative) }
    foreach ($anchor in $(if ($staticMode) { @() } else { @($app, $cwd) })) {
        $directory = New-Object IO.DirectoryInfo $anchor
        for ($depth = 0; $null -ne $directory -and $depth -lt 10; $depth++) {
            Add-Candidate 'qwen-reuse' (Join-Path $directory.FullName $qwen.RelativePath)
            $directory = $directory.Parent
        }
    }
    if ($staticMode) {
        foreach ($staticCache in @($cache, (Join-Path $components 'static-model-downloads'))) {
            $bundleRoot = Join-Path $staticCache $staticDefinition.archiveSha256
            foreach ($relative in @('archive.zip', 'archive.zip.part', 'archive.zip.part.receipt.json', 'payload/inventory.json', 'payload/checksums.sha256')) {
                Add-Candidate 'static-archive-cache' (Join-Path $bundleRoot $relative)
            }
        }
    } else {
        foreach ($artifact in $artifacts) {
            $file = Join-Path (Join-Path (Join-Path $cache 'artifacts') $artifact.sha256) $artifact.name
            foreach ($suffix in @('', '.part', '.part.receipt.json', '.part.receipt.json.tmp', '.download.lock')) { Add-Candidate 'artifact-cache' ($file + $suffix) }
        }
    }
    $legacyPiper = Join-Path $workspace 'vietsub/components/voice/piper'
    $globalPiper = Join-Path $components 'voice/piper'
    Add-Candidate 'piper-legacy' $legacyPiper
    Add-Candidate 'piper-global' $globalPiper
    Add-Candidate 'piper-packaged' (Join-Path $app ('components/piper/' + $piper.bundleVersion))
    Add-Candidate 'piper-prepared' (Join-Path $cache ('bundles/piper/' + $piper.bundleVersion))
    $selectedPiper = if (-not $staticMode -and [IO.Directory]::Exists($legacyPiper)) { $legacyPiper } else { $globalPiper }
    $trustCache = if ($staticMode) { Join-Path $components 'model-downloads' } else { $cache }
    $trustRoot = Assert-ModelSafePath (Join-Path $trustCache 'trust')
    $states = New-Object 'Collections.Generic.List[object]'
    if ([IO.Directory]::Exists($trustRoot)) {
        $stateFiles = @(Get-ChildItem -LiteralPath $trustRoot -Filter '*.json' -File -Force)
        if ($stateFiles.Count -gt 128) { throw 'Too many trust state files for a bounded preflight.' }
        foreach ($file in $stateFiles) {
            try {
                $state = Read-PreflightJson $file.FullName
                $sequence = 0L
                if ($state.repositoryId -isnot [string] -or $state.repositoryId -cnotmatch '^[A-Za-z0-9_.-]{1,100}$' -or
                    $state.channel -cnotmatch '^(Stable|Beta|Development|Staging)$' -or
                    $state.platform -cne 'win-x64' -or -not [long]::TryParse([string]$state.sequence, [ref]$sequence) -or $sequence -le 0 -or
                    $state.catalogSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid trust metadata.' }
                $revocations = @()
                $revocationProperty = $state.PSObject.Properties['revokedArtifactSha256']
                if ($null -ne $revocationProperty -and $null -ne $revocationProperty.Value) { $revocations = @($revocationProperty.Value) }
                if ($revocations.Count -gt $artifacts.Count -or
                    @($revocations | Sort-Object -Unique).Count -ne $revocations.Count -or
                    @($revocations | Where-Object { $_ -isnot [string] -or $_ -cnotmatch '^[0-9a-f]{64}$' -or
                        @($artifacts.sha256) -cnotcontains $_ }).Count) { throw 'Invalid revocations.' }
                $states.Add([pscustomobject]@{ path = $file.FullName; repositoryId = $state.repositoryId; channel = $state.channel;
                    platform = $state.platform; sequence = $sequence; catalogSha256 = $state.catalogSha256;
                    revokedArtifactSha256 = $revocations })
                if ($state.repositoryId -ceq $publicRoot.repositoryId -and @($artifacts | Where-Object { $revocations -ccontains $_.sha256 }).Count) {
                    if (-not $blockers.Contains('APPROVED_ARTIFACT_KNOWN_REVOKED')) { $blockers.Add('APPROVED_ARTIFACT_KNOWN_REVOKED') }
                }
            } catch { $blockers.Add('TRUST_STATE_UNREADABLE_OR_INVALID') }
        }
    }
    $requiredDownloadFree = if ($staticMode) {
        # Inventory does not hash payloads; never credit an existing file as verified free space.
        [long]$staticDefinition.archiveSizeBytes + [long]$staticDefinition.maximumExpandedBytes + 64L * 1024 * 1024
    } else { [long]$qwen.SizeBytes + [long]$qwenMetadata.minimumHardware.freeDiskBytesForInstall + 64L * 1024 * 1024 }
    $cacheDrive = New-Object IO.DriveInfo ([IO.Path]::GetPathRoot($cache))
    $cacheFree = $cacheDrive.AvailableFreeSpace
    if ($cacheFree -lt $requiredDownloadFree) { $blockers.Add($(if ($staticMode) { 'INSUFFICIENT_STATIC_ARCHIVE_CACHE_DISK' } else { 'INSUFFICIENT_QWEN_DOWNLOAD_DISK' })) }
    if (-not $staticMode -and $effective['ModelDistribution.Enabled'] -ne $true) { $blockers.Add('MODEL_SERVER_DISABLED') }
    foreach ($name in @('VietsubEnabled', 'VietsubOcrEnabled', 'VietsubLocalTranslationEnabled', 'VietsubLocalVoiceEnabled')) {
        if ($effective['Features.' + $name] -ne $true) { $blockers.Add('FEATURE_DISABLED_' + $name.ToUpperInvariant()) }
    }
    if (@($candidates | Where-Object { $_.exists }).Count) { $blockers.Add('LOCAL_REUSE_CANDIDATES_PRESENT') }
    $originText = $effective['ModelDistribution.BaseUrl']
    if ($null -eq $originText) { $originText = $effective['Server.BaseUrl'] }
    $origin = $null
    $originValid = [Uri]::TryCreate([string]$originText, [UriKind]::Absolute, [ref]$origin) -and
        $origin.Scheme -eq 'https' -and $origin.Port -eq 443 -and -not $origin.UserInfo -and -not $origin.Query -and -not $origin.Fragment
    if ($staticMode) { $origin = [Uri]$staticDefinition.archiveUrl; $originValid = $true }
    if (-not $originValid) { $blockers.Add('INVALID_MODEL_ORIGIN') }
    [pscustomobject]@{
        schemaVersion = 1; capturedAtUtc = [DateTime]::UtcNow.ToString('o'); applicationRoot = $app; currentDirectory = $cwd
        assessment = 'InventoryOnly'; definitionScope = 'SourceCheckout'; sourceRepositoryId = $publicRoot.repositoryId
        modelDeliveryMode = $mode; staticModelBundle = $staticDefinition; actualCompilePolicyVerified = $false
        actualEmbeddedTrustVerified = $false; candidateFilesRecursivelyInventoried = $false
        cleanupAuthorized = $false; serverVerified = $false; setupVerified = $false; runtimeProbed = $false
        settingsUserOverridePresent = [IO.File]::Exists($userSettingsPath); channel = [string]$effective['Update.Channel']
        modelOrigin = $(if ($originValid) { $origin.AbsoluteUri } else { $null }); cacheRoot = $cache
        qwenFreshInstallRoot = Join-Path (Join-Path $components 'vietsub-translation') $qwenRelative
        piperSelectedRootBeforeCleanup = $selectedPiper; piperRootMustBeResolvedAgainAfterCleanup = $true
        artifactPins = $artifacts; candidates = @($candidates.ToArray()); trustStates = @($states.ToArray())
        protectedPaths = @($trustRoot, (Join-Path $metadataRoot 'static-model-install.json'), (Join-Path $metadataRoot 'static-model-install.pending.json'),
            (Join-Path $metadataRoot 'installer-transactions'), (Join-Path $app 'static-setup-receipt.json'),
            (Join-Path $workspace 'vietsub/projects'), (Join-Path $local 'ToolGenPostVideo/auth-token.bin'),
            (Join-Path $local 'ToolGenPostVideo/device-id.bin'), (Join-Path $local 'ToolGenPostVideo/settings/preferences.json'))
        disk = [pscustomobject]@{ cacheVolume = $cacheDrive.Name; availableBytes = $cacheFree; downloadAndExtractionRequiredBytes = $requiredDownloadFree;
            qwenDownloadRequiredBytes = $(if ($staticMode) { $null } else { $requiredDownloadFree });
            includesSetupRuntimeAndBackupBudget = $false }
        blockers = @($blockers.ToArray()); remainingChecks = @('SERVER_TARGET_AND_SCOPE', $(if ($staticMode) { 'ACTUAL_SETUP_AND_COMPILED_POLICY' } else { 'ACTUAL_SETUP_AND_EMBEDDED_TRUST' }),
            'FFMPEG_RELEASE_APPROVAL', 'PER_VOLUME_TOTAL_DISK_BUDGET', 'PIPER_TARGET_PATH_RUNTIME', 'HARDLINK_IDENTITIES_AND_QUARANTINE_MAP')
    }
}

Export-ModuleMember -Function Get-ModelSetupPreflight
