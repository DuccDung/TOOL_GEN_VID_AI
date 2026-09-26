#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BuilderPython,
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [switch]$Download
)
$ErrorActionPreference = 'Stop'
$arguments = @('-I', '-B', (Join-Path $PSScriptRoot 'prepare_piper_bundle.py'),
    '--input-dir', [IO.Path]::GetFullPath($InputDirectory), '--output-dir', [IO.Path]::GetFullPath($OutputDirectory))
if ($Download) { $arguments += '--download' }
& $BuilderPython @arguments
if ($LASTEXITCODE -ne 0) { throw 'Piper offline bundle preparation failed. Previous evidence was preserved.' }
