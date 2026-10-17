param(
    [string]$SourceDirectory = '',
    [string]$DestinationDirectory = ''
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
Import-Module (Join-Path $scriptRoot 'ApprovedFfmpegBundle.psm1') -Force

# Approval is a reviewed source input. This script cannot manufacture approval,
# rewrite the Development profile or infer complete GPL source availability.
$definitionPath = Join-Path $solutionRoot 'third_party\ffmpeg\FFMPEG_RELEASE_APPROVED.json'
$definition = Get-ApprovedFfmpegDefinition -DefinitionPath $definitionPath
if ($definition.bundleId -ne 'gyan-9.0.1-essentials-win-x64' -or $definition.version -ne '9.0.1-essentials_build-www.gyan.dev') {
    throw 'The default Publish profile requires the pinned Gyan FFmpeg 9.0.1 bundle. Review a new definition before changing this identity.'
}
$resolvedSource = if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    Join-Path $solutionRoot 'third_party\ffmpeg\win-x64'
} else { [System.IO.Path]::GetFullPath($SourceDirectory) }
$resolvedDestination = if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    Join-Path $solutionRoot ('artifacts\ffmpeg-release\' + $definition.bundleId)
} else { [System.IO.Path]::GetFullPath($DestinationDirectory) }
if ($resolvedSource.TrimEnd('\', '/') -eq $resolvedDestination.TrimEnd('\', '/')) {
    throw 'The FFmpeg Release directory must differ from the original Development directory.'
}
Assert-ApprovedFfmpegPath $resolvedDestination
$reused = $false
$stagingRoot = $null
try {
    if (Test-Path -LiteralPath $resolvedDestination) {
        Test-ApprovedFfmpegFiles -Directory $resolvedDestination -Definition $definition
        $reused = $true
    } else {
        # Hash all sources before copying or executing a native program.
        $sources = @(Get-ApprovedFfmpegSourceFiles -BinarySourceDirectory $resolvedSource `
            -DefinitionDirectory (Split-Path -Parent $definitionPath) -Definition $definition)
        $destinationParent = [System.IO.Path]::GetDirectoryName($resolvedDestination.TrimEnd('\', '/'))
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        $stagingRoot = Join-Path $destinationParent ('.ffmpeg-release-' + [Guid]::NewGuid().ToString('N') + '.part')
        New-Item -ItemType Directory -Path $stagingRoot | Out-Null
        foreach ($source in $sources) {
            Copy-Item -LiteralPath $source.SourcePath -Destination (Join-Path $stagingRoot $source.Name)
        }
        Test-ApprovedFfmpegFiles -Directory $stagingRoot -Definition $definition
        $null = & (Join-Path $scriptRoot 'Test-FfmpegBundle.ps1') -BundlePath $stagingRoot `
            -ExpectedVersion $definition.version -RequireReleaseApproval
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                [System.IO.Directory]::Move($stagingRoot, $resolvedDestination)
                break
            }
            catch {
                if (Test-Path -LiteralPath $resolvedDestination -PathType Container) {
                    # A simultaneous Publish may have prepared exactly the same bundle.
                    Test-ApprovedFfmpegFiles -Directory $resolvedDestination -Definition $definition
                    $reused = $true
                    break
                }
                if ($attempt -eq 20) { throw }
                # Windows scanners may briefly retain a just-probed executable.
                # Retry only the atomic directory promotion; never replace a bundle.
                Start-Sleep -Milliseconds 250
            }
        }
    }
    $profile = & (Join-Path $scriptRoot 'Test-FfmpegBundle.ps1') -BundlePath $resolvedDestination `
        -ExpectedVersion $definition.version -RequireReleaseApproval
    [pscustomobject]@{
        BundlePath = $resolvedDestination
        BundleId = $definition.bundleId
        Version = $profile.Version
        ApprovalScope = $definition.approvalScope
        ApprovalKind = $definition.approvalKind
        Reused = $reused
        CorrespondingSourceComplete = $definition.correspondingSourceComplete
        RedistributionQualified = $definition.redistributionQualified
        ProductionQualified = $definition.productionQualified
    }
}
finally {
    if ($null -ne $stagingRoot -and [System.IO.Directory]::Exists($stagingRoot)) {
        $fullStage = [System.IO.Path]::GetFullPath($stagingRoot)
        $fullParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetDirectoryName($resolvedDestination.TrimEnd('\', '/'))).TrimEnd('\', '/')
        if ([System.IO.Path]::GetDirectoryName($fullStage) -ne $fullParent -or
            [System.IO.Path]::GetFileName($fullStage) -notmatch '^\.ffmpeg-release-[a-f0-9]{32}\.part$') {
            throw 'Refusing to remove an unexpected FFmpeg staging path.'
        }
        Assert-ApprovedFfmpegPath $fullStage
        [System.IO.Directory]::Delete($fullStage, $true)
    }
}
