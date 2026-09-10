param(
    [ValidateSet('Prepare', 'Verify', 'Status')][string]$Mode = 'Status',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$desktop = Join-Path $repo "TOOL-LOCAL/bin/$Configuration/net10.0-windows/win-x64/TOOL-LOCAL.dll"
if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Build TOOL_GEN_POST_VIDEO.slnx first. The desktop binary is missing.'
}
$command = switch ($Mode) {
    'Prepare' { '--prepare-local-voice' }
    'Verify' { '--verify-local-voice' }
    'Status' { '--check-local-voice' }
}
# Use the same compiled host, configuration, installer and checksum/probe gates as the UI.
# This command never logs in, changes projects or calls a media provider.
& dotnet $desktop $command
exit $LASTEXITCODE
