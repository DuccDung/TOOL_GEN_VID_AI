param(
    [Parameter(Mandatory=$true)][string]$ComponentRoot,
    [string]$TemporaryRoot
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$componentPath = [IO.Path]::GetFullPath($ComponentRoot)
if (-not [IO.Path]::IsPathRooted($ComponentRoot) -or $componentPath.StartsWith('\\') -or
    $componentPath.TrimEnd('\') -eq [IO.Path]::GetPathRoot($componentPath).TrimEnd('\')) {
    throw 'A dedicated local component directory is required.'
}
if ([string]::IsNullOrWhiteSpace($TemporaryRoot)) { $TemporaryRoot = Join-Path ([IO.Path]::GetTempPath()) 'vm-veo-voice' }
$temporaryPath = [IO.Path]::GetFullPath($TemporaryRoot)
if (-not [IO.Path]::IsPathRooted($TemporaryRoot) -or $temporaryPath.StartsWith('\\') -or
    $temporaryPath.TrimEnd('\') -eq [IO.Path]::GetPathRoot($temporaryPath).TrimEnd('\') -or
    $temporaryPath.TrimEnd('\').Equals($componentPath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -or
    $temporaryPath.StartsWith($componentPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'A separate local temporary directory outside the component is required.'
}
foreach ($directoryPath in @($componentPath, $temporaryPath)) {
    $ancestor = [IO.DirectoryInfo]::new($directoryPath)
    while ($null -ne $ancestor) {
        if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked component/temp directories are not supported.' }
        $ancestor = $ancestor.Parent
    }
}
New-Item -ItemType Directory -Path $temporaryPath -Force | Out-Null
$env:TEMP = $temporaryPath
$env:TMP = $temporaryPath
$env:PYTHONDONTWRITEBYTECODE = '1'
New-Item -ItemType Directory -Path $componentPath -Force | Out-Null
$allowedHosts = @('github.com','codeload.github.com','objects.githubusercontent.com','release-assets.githubusercontent.com','huggingface.co','cdn-lfs.hf.co','cas-bridge.xethub.hf.co','us.aws.cdn.hf.co','dl.fbaipublicfiles.com')
function Download-Checked([string]$Url,[string]$Name,[long]$Size,[string]$Sha256) {
    $destination = Join-Path $componentPath $Name
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    if ((Test-Path -LiteralPath $destination) -and (Get-Item -LiteralPath $destination).Length -eq $Size -and (Get-FileHash -LiteralPath $destination).Hash -eq $Sha256) { return }
    Add-Type -AssemblyName System.Net.Http
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(30)
    $uri = [Uri]$Url
    try {
        for ($redirect = 0; $redirect -le 5; $redirect++) {
            if ($uri.Scheme -ne 'https' -or $uri.Port -ne 443 -or $allowedHosts -notcontains $uri.DnsSafeHost -or $uri.UserInfo) { throw 'Download host is not approved.' }
            $response = $client.GetAsync($uri,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ge 300 -and [int]$response.StatusCode -lt 400) {
                $next = [Uri]::new($uri,$response.Headers.Location)
                $response.Dispose()
                $uri = $next
                continue
            }
            $response.EnsureSuccessStatusCode() | Out-Null
            $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $target = [IO.File]::Create($destination + '.part')
            try {
                $buffer = New-Object byte[] 131072
                [long]$total = 0
                while (($read = $source.Read($buffer,0,$buffer.Length)) -gt 0) {
                    $total += $read
                    if ($total -gt $Size) { throw 'Downloaded component exceeds the pinned size.' }
                    $target.Write($buffer,0,$read)
                }
            } finally { $source.Dispose(); $target.Dispose(); $response.Dispose() }
            if ($total -ne $Size -or (Get-FileHash -LiteralPath ($destination + '.part')).Hash -ne $Sha256) { throw 'Component checksum mismatch.' }
            Move-Item -LiteralPath ($destination + '.part') -Destination $destination -Force
            return
        }
        throw 'Too many component redirects.'
    } finally { $client.Dispose() }
}

Download-Checked 'https://github.com/astral-sh/uv/releases/download/0.12.3/uv-x86_64-pc-windows-msvc.zip' 'uv.zip' 19013455 'b23350c79e8ad0192b8124af13a0f17e8d4e4549524785e1aef389ae5a06990e'
Expand-Archive -LiteralPath (Join-Path $componentPath 'uv.zip') -DestinationPath (Join-Path $componentPath 'uv') -Force
$uv = Join-Path $componentPath 'uv/uv.exe'
if ((Get-FileHash -LiteralPath $uv).Hash -ne '68a22cbab1674647bcda32120b214e6480f875414e3333f49f87ae99b4b0e0fa') { throw 'UV executable mismatch.' }
$env:UV_PYTHON_INSTALL_DIR = Join-Path $componentPath 'python'
$env:UV_CACHE_DIR = Join-Path ([IO.Path]::GetTempPath()) 'vm-vc-uv'
$env:UV_NO_PROGRESS = '1'
$python = Join-Path $componentPath 'venv/Scripts/python.exe'
if (!(Test-Path -LiteralPath $python)) {
    & $uv venv --python 3.11.11 --managed-python (Join-Path $componentPath 'venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python installation failed.' }
}
$lockfile = Join-Path $PSScriptRoot 'requirements.lock'
if (!(Test-Path -LiteralPath $lockfile)) { throw 'Missing pinned dependency lockfile.' }
& $uv pip sync --python $python --index-url https://pypi.org/simple --extra-index-url https://download.pytorch.org/whl/cpu --index-strategy unsafe-best-match --require-hashes $lockfile
if ($LASTEXITCODE -ne 0) { throw 'Voice runtime installation failed.' }
Download-Checked 'https://codeload.github.com/myshell-ai/OpenVoice/zip/74a1d147b17a8c3092dd5430504bd83ef6c7eb23' 'openvoice.zip' 3208705 'd08cbc84f4ec7abc76f9dddb5bbb221e906e49cfe5febd37133152bdeacb8be4'
Expand-Archive -LiteralPath (Join-Path $componentPath 'openvoice.zip') -DestinationPath (Join-Path $componentPath 'upstream') -Force
$upstream = Join-Path $componentPath 'upstream/OpenVoice-74a1d147b17a8c3092dd5430504bd83ef6c7eb23'
New-Item -ItemType Directory -Path (Join-Path $componentPath 'openvoice') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $upstream 'openvoice') -Destination (Join-Path $componentPath 'openvoice') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $upstream 'LICENSE') -Destination (Join-Path $componentPath 'openvoice/LICENSE') -Force
Download-Checked 'https://huggingface.co/myshell-ai/OpenVoiceV2/resolve/f36e7edfe1684461a8343844af60babc2efbb727/converter/checkpoint.pth' 'checkpoint.pth' 131320490 '9652c27e92b6b2a91632590ac9962ef7ae2b712e5c5b7f4c34ec55ee2b37ab9e'
Download-Checked 'https://huggingface.co/myshell-ai/OpenVoiceV2/resolve/f36e7edfe1684461a8343844af60babc2efbb727/converter/config.json' 'config.json' 838 '9dfff60350b8c63f2c664efd92a61b2516efb22671466960f0e5dfebd881fa47'
Download-Checked 'https://dl.fbaipublicfiles.com/demucs/hybrid_transformer/955717e8-8726e21a.th' 'demucs/955717e8-8726e21a.th' 84141911 '8726e21a993978c7ba086d3872e7608d7d5bfca646ca4aca459ffda844faa8b4'
# Manifest covers the installed code/model/runtime bytes, not merely a READY file.
& $python -I -c @'
import hashlib, json, os, pathlib, sys
root = pathlib.Path('\\\\?\\' + os.path.abspath(sys.argv[1]))
files = []
for directory, children, names in os.walk(root):
    # uv creates a minor-version junction; the venv uses the exact-version home.
    # Never traverse/manifest junction aliases or a link escaping the component.
    children[:] = sorted(n for n in children if n not in ('uv-cache','upstream','__pycache__','plot-cache')
                         and not os.lstat(pathlib.Path(directory) / n).st_file_attributes & 0x400)
    for name in sorted(names):
        path = pathlib.Path(directory) / name
        if os.lstat(path).st_file_attributes & 0x400:
            raise RuntimeError('Linked runtime file is not supported')
        if name in ('manifest.json','ready.json','runtime.lock') or path.suffix in ('.pyc','.part') or name.endswith('.request.json'):
            continue
        with path.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        files.append(dict(path=path.relative_to(root).as_posix(), size=path.stat().st_size, sha256=digest))
partial = root / 'manifest.json.part'
partial.write_text(json.dumps(dict(version='openvoice-v2-demucs-silero-cpu-v1', files=files), sort_keys=True), encoding='utf-8')
partial.replace(root / 'manifest.json')
'@ $componentPath
if ($LASTEXITCODE -ne 0) { throw 'Runtime manifest failed.' }
Write-Output 'Component files installed. Desktop probe is required before READY.'
