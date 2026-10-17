#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)][string]$ApplicationRoot,
    [string]$CurrentDirectory = (Get-Location).ProviderPath,
    [Parameter(Mandatory = $true)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ModelSetupPreflight.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'ModelDistributionHandover.psm1') -Force -DisableNameChecking
# Resolve/collect before writing. Existing reports are never overwritten.
$report = Get-ModelSetupPreflight -ApplicationRoot $ApplicationRoot -CurrentDirectory $CurrentDirectory
Write-ModelDistributionJson -Path ([IO.Path]::GetFullPath($ReportPath)) -Value $report
[pscustomobject]@{ reportPath = [IO.Path]::GetFullPath($ReportPath); assessment = $report.assessment;
    cleanupAuthorized = $false; blockers = $report.blockers }
