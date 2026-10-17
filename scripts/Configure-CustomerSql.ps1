param(
    [string]$PackageRoot = $PSScriptRoot,
    [string]$SqlServer = '',
    [int]$Port = 0,
    [string]$Database = '',
    [string]$UserName = '',
    [Security.SecureString]$Password,
    [switch]$WindowsAuthentication,
    [switch]$TrustServerCertificate
)

# Run on the recipient's own machine. No credential is part of the ZIP.
# This only provisions the existing desktop SQL override; it never opens SQL.
$ErrorActionPreference = 'Stop'
$part = $null
$secretPointer = [IntPtr]::Zero
try {
    $root = (Resolve-Path -LiteralPath $PackageRoot).ProviderPath
    if (-not (Test-Path -LiteralPath (Join-Path $root 'TOOL-LOCAL.exe')) -or
        -not (Test-Path -LiteralPath (Join-Path $root 'appsettings.json'))) { throw 'Invalid package folder.' }
    for ($folder = New-Object IO.DirectoryInfo $root; $null -ne $folder; $folder = $folder.Parent) {
        if ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe package folder.' }
    }
    $destination = Join-Path $root 'appsettings.user.json'
    if (Test-Path -LiteralPath $destination) { throw 'Existing private configuration must be preserved.' }
    if (-not $SqlServer) { $SqlServer = Read-Host 'SQL host do quan tri vien cung cap' }
    if ($Port -eq 0) { $Port = [int](Read-Host 'TCP port SQL do quan tri vien cung cap') }
    if (-not $Database) { $Database = Read-Host 'Ten database workflow' }
    if ([string]::IsNullOrWhiteSpace($SqlServer) -or $SqlServer -match '(?i)DUNGDEV|[;,/\\]' -or
        $Port -lt 1 -or $Port -gt 65535 -or [string]::IsNullOrWhiteSpace($Database)) { throw 'Invalid SQL deployment input.' }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder.set_DataSource(('tcp:{0},{1}' -f $SqlServer, $Port))
    $builder.set_InitialCatalog($Database)
    $builder.set_PersistSecurityInfo($false)
    $builder.set_Encrypt($true)
    $builder.set_TrustServerCertificate([bool]$TrustServerCertificate)
    $builder.set_ConnectTimeout(15)
    $builder.set_IntegratedSecurity([bool]$WindowsAuthentication)
    if (-not $WindowsAuthentication) {
        if (-not $UserName) { $UserName = Read-Host 'SQL user chi co quyen workflow vf' }
        if ([string]::IsNullOrWhiteSpace($UserName)) { throw 'SQL user is required.' }
        if ($null -eq $Password) { $Password = Read-Host 'SQL password rieng cho may khach (khong gui trong chat)' -AsSecureString }
        $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
        $builder.set_UserID($UserName)
        $builder.set_Password([Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer))
    }
    $configuration = @{ Database = @{ ConnectionString = $builder.get_ConnectionString() } }
    $part = Join-Path $root ('.customer-sql-' + [Guid]::NewGuid().ToString('N') + '.part')
    $acl = New-Object Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetOwner($identity)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'Allow')))
    $stream = [IO.File]::Create($part)
    $stream.Dispose()
    Set-Acl -LiteralPath $part -AclObject $acl
    [IO.File]::WriteAllText($part, ($configuration | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding($false)))
    [IO.File]::Move($part, $destination)
    Write-Output 'Da luu cau hinh SQL rieng cho nguoi dung hien tai. Chua kiem tra ket noi/quyen/schema. Mo TOOL-LOCAL.exe de tiep tuc.'
} catch {
    # Never echo exception text, settings, SQL user/password or connection strings.
    Write-Output 'Khong tao duoc cau hinh SQL. Kiem tra thu muc giai nen co quyen ghi, khong co appsettings.user.json cu, va thong tin host/port/database. Giu nguyen file cau hinh cu.'
    exit 2
} finally {
    if ($secretPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer) }
    # Only remove the single temporary file created in the resolved package folder.
    if ($part -and (Test-Path -LiteralPath $part)) { Remove-Item -LiteralPath $part -Force }
}
