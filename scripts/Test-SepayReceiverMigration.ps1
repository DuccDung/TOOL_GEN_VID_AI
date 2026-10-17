# Uses a new private LocalDB instance and synthetic data only. No external SQL target accepted.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$repoRoot = Split-Path -Parent $PSScriptRoot
$localDbExe = Join-Path $env:ProgramFiles 'Microsoft SQL Server/150/Tools/Binn/SqlLocalDB.exe'
if (-not (Test-Path -LiteralPath $localDbExe)) { throw 'SQL Server 2019 LocalDB is required.' }
$instance = 'VideoMakerSepayTest_' + [Guid]::NewGuid().ToString('N')
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$scratchRoot = Join-Path $tempBase $instance
$null = New-Item -ItemType Directory -Path $scratchRoot
$original = Get-Content -LiteralPath (Join-Path $repoRoot 'database/VideoFactory.4.1.10.SepayReceivingAccounts.sql') -Encoding UTF8 -Raw
$replacement = Get-Content -LiteralPath (Join-Path $repoRoot 'database/VideoFactory.4.1.12.SepayReceivingAccountsPermissions.sql') -Encoding UTF8 -Raw
$simple = Get-Content -LiteralPath (Join-Path $repoRoot 'database/VideoFactory.4.1.13.SepayReceivingAccountsSimple.sql') -Encoding UTF8 -Raw
$prepare = (Get-Content -LiteralPath (Join-Path $repoRoot 'database/Prepare.SepayReceivingAccountsPermissions.sql') -Encoding UTF8 -Raw).
    Replace('<approved-database>', 'VideoFactory').Replace('<migration-database-user>', 'VmMigrator')
$script:passedCount = 0

function Literal([string]$value) { return $value.Replace("'", "''") }
function Open-TestConnection([string]$database = 'VideoFactory') {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = '(localdb)\' + $instance
    $builder['Initial Catalog'] = $database
    $builder['Integrated Security'] = $true
    $builder['TrustServerCertificate'] = $true
    $builder['Pooling'] = $false
    $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
    $connection.Open()
    return $connection
}
function Exec($connection, [string]$sql) {
    foreach ($batch in [regex]::Split($sql, '(?im)^\s*GO\s*$')) {
        if ([string]::IsNullOrWhiteSpace($batch)) { continue }
        $command = $connection.CreateCommand()
        try { $command.CommandTimeout = 60; $command.CommandText = $batch; $null = $command.ExecuteNonQuery() }
        finally { $command.Dispose() }
    }
}
function Scalar($connection, [string]$sql) {
    $command = $connection.CreateCommand()
    try { $command.CommandTimeout = 30; $command.CommandText = $sql; return $command.ExecuteScalar() }
    finally { $command.Dispose() }
}
function As-Migrator([string]$sql) {
    $connection = Open-TestConnection
    try { Exec $connection "EXECUTE AS USER=N'VmMigrator';"; Exec $connection $sql }
    finally { $connection.Dispose() }
}
function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) { throw ($message + ': expected ' + $expected + ', actual ' + $actual) }
}
function Expect-SqlError([scriptblock]$action, [int]$number) {
    try { & $action }
    catch {
        $problem = $_.Exception.GetBaseException()
        if ($problem -is [System.Data.SqlClient.SqlException] -and
            @($problem.Errors | Where-Object Number -eq $number).Count -gt 0) { return }
        throw
    }
    throw ('Expected SQL error ' + $number + ', but command succeeded.')
}
function Passed([string]$name) { $script:passedCount++; Write-Output ('PASS: ' + $name) }
function Reset-Fixture {
    Exec $admin @'
DROP TABLE IF EXISTS auth.SepayPaymentSettings;
DROP TABLE IF EXISTS auth.SepayReceivingAccounts;
DELETE ai.SchemaVersions;
REVOKE SELECT, INSERT, UPDATE, DELETE ON SCHEMA::auth FROM VideoMakerDesktopRole;
REVOKE VIEW DEFINITION ON ROLE::VideoMakerDesktopRole FROM VmMigrator;
REVOKE VIEW DEFINITION ON SCHEMA::auth FROM VmMigrator;
'@
}
function Assert-NoChanges {
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM sys.tables WHERE object_id IN (OBJECT_ID(N'auth.SepayReceivingAccounts'),OBJECT_ID(N'auth.SepayPaymentSettings'));") 'No receiver tables'
    Assert-Equal 0 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'No version writes'
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.LicensePayments;') 'Existing payments preserved'
}

$created = $false
$master = $null
$admin = $null
try {
    $null = & $localDbExe create $instance -s
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create private LocalDB instance.' }
    $created = $true
    $master = Open-TestConnection 'master'
    $dataPath = Literal (Join-Path $scratchRoot 'data.mdf')
    $logPath = Literal (Join-Path $scratchRoot 'log.ldf')
    Exec $master "CREATE DATABASE [VideoFactory] ON (NAME=N'VmSepayData', FILENAME=N'$dataPath') LOG ON (NAME=N'VmSepayLog', FILENAME=N'$logPath');"
    $admin = Open-TestConnection
    Exec $admin @'
CREATE SCHEMA auth AUTHORIZATION dbo;
GO
CREATE SCHEMA ai AUTHORIZATION dbo;
GO
CREATE TABLE auth.LicensePayments (Id int NOT NULL PRIMARY KEY);
INSERT auth.LicensePayments VALUES (1);
CREATE TABLE ai.SchemaVersions (SchemaVersionId int IDENTITY PRIMARY KEY, Version varchar(50) NOT NULL UNIQUE, Description nvarchar(1000), AppliedAtUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE ROLE VideoMakerDesktopRole AUTHORIZATION dbo;
CREATE USER VmMigrator WITHOUT LOGIN;
CREATE USER VmDesktop WITHOUT LOGIN;
ALTER ROLE db_ddladmin ADD MEMBER VmMigrator;
ALTER ROLE VideoMakerDesktopRole ADD MEMBER VmDesktop;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::auth TO VmMigrator;
GRANT SELECT, INSERT ON OBJECT::ai.SchemaVersions TO VmMigrator;
'@
    $backup = Literal (Join-Path $scratchRoot 'baseline.bak')
    $restoreData = Literal (Join-Path $scratchRoot 'restored.mdf')
    $restoreLog = Literal (Join-Path $scratchRoot 'restored.ldf')
    Exec $master "BACKUP DATABASE [VideoFactory] TO DISK=N'$backup' WITH INIT,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM; RESTORE DATABASE [SePayRestore] FROM DISK=N'$backup' WITH MOVE N'VmSepayData' TO N'$restoreData', MOVE N'VmSepayLog' TO N'$restoreLog', CHECKSUM; DBCC CHECKDB([SePayRestore]) WITH NO_INFOMSGS;"
    Passed 'Synthetic baseline backup, checksum, actual restore and CHECKDB'

    if ($simple -match '(?i)\b(GRANT|DENY|REVOKE|CONTROL)\b|VideoMakerDesktopRole|sys\.database_permissions|EXECUTE\s+AS') {
        throw 'Simple migration must not contain database security operations or role metadata checks.'
    }
    $limited = Open-TestConnection
    try {
        Exec $limited "EXECUTE AS USER=N'VmMigrator';"
        Assert-Equal 0 (Scalar $limited "SELECT HAS_PERMS_BY_NAME(N'auth',N'SCHEMA',N'CONTROL');") 'Migrator has no schema CONTROL'
        Assert-Equal 0 (Scalar $limited "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'VideoMakerDesktopRole';") 'Role metadata is hidden'
        Exec $limited $simple
    }
    finally { $limited.Dispose() }
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.SepayPaymentSettings WHERE SettingsId=1 AND Enabled=0 AND SelectedAccountId IS NULL;') 'Simple disabled singleton'
    Assert-Equal 2 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'Simple and base versions'
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM ai.SchemaVersions WHERE Version='4.1.12-sepay-receiver-permissions';") 'Security migration is not marked applied'
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole');") 'Desktop permissions unchanged'
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VmMigrator') AND permission_name='VIEW DEFINITION';") 'Migrator metadata permissions unchanged'
    Passed 'Simple migration works without CONTROL or visible role metadata and leaves permissions unchanged'

    Exec $admin @'
INSERT auth.SepayReceivingAccounts(AccountId,DisplayName,BankCode,BankName,AccountNumber,AccountName,Revision,CreatedAtUtc,UpdatedAtUtc)
VALUES ('11111111-1111-1111-1111-111111111111',N'Fixture bank','VCB',N'Fixture bank','12345678',N'FIXTURE USER','22222222-2222-2222-2222-222222222222',SYSUTCDATETIME(),SYSUTCDATETIME());
UPDATE auth.SepayPaymentSettings SET Enabled=1,SelectedAccountId='11111111-1111-1111-1111-111111111111',Revision='33333333-3333-3333-3333-333333333333';
'@
    $simpleSettingsBefore = Scalar $admin 'SELECT CONVERT(nvarchar(36),Revision)+CONVERT(nvarchar(30),UpdatedAtUtc,126) FROM auth.SepayPaymentSettings;'
    $simpleReceiverBefore = Scalar $admin 'SELECT CONVERT(nvarchar(36),Revision)+CONVERT(nvarchar(30),CreatedAtUtc,126)+CONVERT(nvarchar(30),UpdatedAtUtc,126) FROM auth.SepayReceivingAccounts;'
    As-Migrator $simple
    Assert-Equal $simpleSettingsBefore (Scalar $admin 'SELECT CONVERT(nvarchar(36),Revision)+CONVERT(nvarchar(30),UpdatedAtUtc,126) FROM auth.SepayPaymentSettings;') 'Simple rerun preserves settings revision and timestamp'
    Assert-Equal $simpleReceiverBefore (Scalar $admin 'SELECT CONVERT(nvarchar(36),Revision)+CONVERT(nvarchar(30),CreatedAtUtc,126)+CONVERT(nvarchar(30),UpdatedAtUtc,126) FROM auth.SepayReceivingAccounts;') 'Simple rerun preserves receiving account'
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.SepayPaymentSettings WHERE Enabled=1 AND SelectedAccountId IS NOT NULL;') 'Simple rerun preserves enabled selection'
    Assert-Equal 2 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'Simple rerun does not duplicate versions'
    Passed 'Simple rerun preserves receiving account, enabled selection, revisions and versions'

    Expect-SqlError { Exec $admin 'UPDATE auth.SepayPaymentSettings SET Enabled=1,SelectedAccountId=NULL;' } 547
    Expect-SqlError { Exec $admin "UPDATE auth.SepayPaymentSettings SET SelectedAccountId='99999999-9999-9999-9999-999999999999';" } 547
    Expect-SqlError { Exec $admin 'INSERT auth.SepayPaymentSettings(SettingsId,Enabled,UpdatedAtUtc) VALUES(2,0,SYSUTCDATETIME());' } 547
    Expect-SqlError { Exec $admin "INSERT auth.SepayReceivingAccounts(AccountId,DisplayName,BankCode,BankName,AccountNumber,AccountName,CreatedAtUtc,UpdatedAtUtc) VALUES(NEWID(),N'Another bank','VCB',N'Fixture bank','12345678',N'FIXTURE USER',SYSUTCDATETIME(),SYSUTCDATETIME());" } 2627
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.SepayReceivingAccounts;') 'Only original receiver remains'
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.SepayPaymentSettings WHERE Enabled=1 AND SelectedAccountId IS NOT NULL;') 'Constraints preserve configured settings'
    Passed 'Simple schema enforces receiver selection, foreign key, singleton and unique bank account'

    Reset-Fixture
    Exec $admin "CREATE TRIGGER ai.RejectSepayVersion ON ai.SchemaVersions AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Version='4.1.13-sepay-receivers-simple') THROW 51129, 'Fixture version insert failure.', 1; END;"
    Expect-SqlError { As-Migrator $simple } 51129
    Assert-NoChanges
    Exec $admin 'DROP TRIGGER ai.RejectSepayVersion;'
    Passed 'Simple migration rolls back all tables, settings and versions on late failure'

    Exec $admin $prepare
    Expect-SqlError { As-Migrator $original } 15151
    Assert-NoChanges
    Passed 'Original migration reproduces restricted-account error and rolls back'

    As-Migrator $replacement
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.SepayPaymentSettings WHERE SettingsId=1 AND Enabled=0 AND SelectedAccountId IS NULL;') 'Disabled singleton'
    Assert-Equal 2 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'Base and correction versions'
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE class=1 AND major_id IN (OBJECT_ID(N'auth.SepayReceivingAccounts'),OBJECT_ID(N'auth.SepayPaymentSettings')) AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole');") 'Schema protection reused without new object grants'
    Passed 'Limited DDL account succeeds using existing visible schema DENY'

    Exec $admin @'
INSERT auth.SepayReceivingAccounts(AccountId,DisplayName,BankCode,BankName,AccountNumber,AccountName,Revision,CreatedAtUtc,UpdatedAtUtc)
VALUES ('11111111-1111-1111-1111-111111111111',N'Fixture bank','VCB',N'Fixture bank','12345678',N'FIXTURE USER','22222222-2222-2222-2222-222222222222',SYSUTCDATETIME(),SYSUTCDATETIME());
UPDATE auth.SepayPaymentSettings SET Enabled=1,SelectedAccountId='11111111-1111-1111-1111-111111111111',Revision='33333333-3333-3333-3333-333333333333';
'@
    $before = Scalar $admin 'SELECT CONVERT(nvarchar(36),Revision)+CONVERT(nvarchar(30),UpdatedAtUtc,126) FROM auth.SepayPaymentSettings;'
    As-Migrator $replacement
    Assert-Equal $before (Scalar $admin 'SELECT CONVERT(nvarchar(36),Revision)+CONVERT(nvarchar(30),UpdatedAtUtc,126) FROM auth.SepayPaymentSettings;') 'Revision and timestamp preserved'
    Assert-Equal 1 (Scalar $admin 'SELECT COUNT(*) FROM auth.SepayPaymentSettings WHERE Enabled=1 AND SelectedAccountId IS NOT NULL;') 'Enabled selection preserved'
    Assert-Equal 2 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'Versions remain unique'
    Passed 'Rerun preserves enabled receiver, revision, timestamp and versions'

    foreach ($table in @('SepayReceivingAccounts','SepayPaymentSettings')) {
        foreach ($statement in @("SELECT * FROM auth.$table;", "DELETE FROM auth.$table WHERE 1=0;", "UPDATE auth.$table SET Revision=NEWID() WHERE 1=0;", "INSERT auth.$table DEFAULT VALUES;")) {
            $desktop = Open-TestConnection
            try { Exec $desktop "EXECUTE AS USER=N'VmDesktop';"; Expect-SqlError { Exec $desktop $statement } 229 }
            finally { $desktop.Dispose() }
        }
    }
    Passed 'Desktop SQL is denied SELECT/INSERT/UPDATE/DELETE on both tables'

    Reset-Fixture
    Exec $admin 'DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::auth TO VideoMakerDesktopRole;'
    Expect-SqlError { As-Migrator $replacement } 51124
    Assert-NoChanges
    Passed 'Hidden security metadata stops before DDL'

    Exec $admin 'GRANT VIEW DEFINITION ON ROLE::VideoMakerDesktopRole TO VmMigrator; GRANT VIEW DEFINITION ON SCHEMA::auth TO VmMigrator; REVOKE SELECT, INSERT, UPDATE, DELETE ON SCHEMA::auth FROM VideoMakerDesktopRole;'
    Expect-SqlError { As-Migrator $replacement } 51122
    Assert-NoChanges
    Passed 'Missing desktop protection stops before DDL'

    Exec $admin 'DENY SELECT ON SCHEMA::auth TO VideoMakerDesktopRole;'
    Expect-SqlError { As-Migrator $replacement } 51122
    Assert-NoChanges
    Passed 'Partial schema DENY cannot bypass remaining CRUD protection'

    Exec $admin $replacement
    Assert-Equal 8 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE class=1 AND major_id IN (OBJECT_ID(N'auth.SepayReceivingAccounts'),OBJECT_ID(N'auth.SepayPaymentSettings')) AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') AND state='D' AND permission_name IN ('SELECT','INSERT','UPDATE','DELETE');") 'All object DENY permissions'
    As-Migrator $replacement
    Passed 'DBA applies object DENY; restricted rerun uses existing protection'

    As-Migrator $simple
    Assert-Equal 8 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE class=1 AND major_id IN (OBJECT_ID(N'auth.SepayReceivingAccounts'),OBJECT_ID(N'auth.SepayPaymentSettings')) AND grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') AND state='D' AND permission_name IN ('SELECT','INSERT','UPDATE','DELETE');") 'Existing object protection remains unchanged'
    Assert-Equal 3 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'Simple version added alongside existing versions'
    Passed 'Simple migration preserves existing desktop protection and prior migration versions'

    Reset-Fixture
    Exec $admin $prepare
    Exec $admin "CREATE TRIGGER ai.RejectSepayVersion ON ai.SchemaVersions AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Version='4.1.12-sepay-receiver-permissions') THROW 51129, 'Fixture version insert failure.', 1; END;"
    Expect-SqlError { As-Migrator $replacement } 51129
    Assert-NoChanges
    Exec $admin 'DROP TRIGGER ai.RejectSepayVersion;'
    Passed 'Late version failure rolls back tables, settings and both versions'

    Reset-Fixture
    $dbaPackage = "BEGIN TRY`nBEGIN TRANSACTION;`n" + $prepare + "`n" + $replacement +
        "`nCOMMIT TRANSACTION;`nEND TRY`nBEGIN CATCH`nIF XACT_STATE() <> 0 ROLLBACK TRANSACTION;`nTHROW;`nEND CATCH;"
    Exec $admin $dbaPackage
    Assert-Equal 0 (Scalar $admin 'SELECT @@TRANCOUNT;') 'DBA package commits outer transaction'
    Assert-Equal 2 (Scalar $admin 'SELECT COUNT(*) FROM ai.SchemaVersions;') 'DBA package records both versions'
    As-Migrator $replacement
    Passed 'Combined DBA preparation and migration commits atomically and permits restricted rerun'

    Reset-Fixture
    Exec $admin "CREATE TRIGGER ai.RejectSepayVersion ON ai.SchemaVersions AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Version='4.1.12-sepay-receiver-permissions') THROW 51129, 'Fixture version insert failure.', 1; END;"
    Expect-SqlError { Exec $admin $dbaPackage } 51129
    Assert-NoChanges
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') AND class=3 AND major_id=SCHEMA_ID(N'auth');") 'DBA package rolls back schema permission changes'
    Assert-Equal 0 (Scalar $admin "SELECT COUNT(*) FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID(N'VmMigrator') AND permission_name='VIEW DEFINITION';") 'DBA package rolls back metadata grants'
    Exec $admin 'DROP TRIGGER ai.RejectSepayVersion;'
    Passed 'Combined DBA package rolls back security grants as well as schema on failure'

    $productionPackage = Join-Path $repoRoot 'artifacts/sepay-admin-20261011/production/Apply-SePay-Permissions-Fix.sql'
    if (Test-Path -LiteralPath $productionPackage) {
        $productionSql = Get-Content -LiteralPath $productionPackage -Encoding UTF8 -Raw
        Expect-SqlError { Exec $admin $productionSql } 51130
        Assert-NoChanges
        Passed 'Generated production DBA package rejects an unrelated instance/database'
    }

    Write-Output ('Local SQL regression: ' + $script:passedCount + ' Passed / 0 Failed / 0 Skipped')
}
finally {
    if ($admin) { $admin.Dispose() }
    if ($master) { $master.Dispose() }
    if ($created) {
        $null = & $localDbExe stop $instance -k
        if ($LASTEXITCODE -ne 0) { throw ('Unable to stop owned LocalDB instance: ' + $instance) }
        $null = & $localDbExe delete $instance
        if ($LASTEXITCODE -ne 0) { throw ('Unable to remove owned LocalDB instance: ' + $instance) }
    }
    $resolvedScratch = [IO.Path]::GetFullPath($scratchRoot)
    if ($resolvedScratch -ne (Join-Path $tempBase $instance) -or -not $instance.StartsWith('VideoMakerSepayTest_')) { throw 'Unsafe test cleanup target.' }
    Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
}
