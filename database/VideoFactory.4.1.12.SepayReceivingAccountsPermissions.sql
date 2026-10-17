/* Replacement entry point for SePay receiver setup on restricted migration accounts.
   Keeps 4.1.10 immutable. A visible auth-schema CRUD DENY already protects new tables;
   otherwise object DENY must already exist or be applied by a permitted principal.
   Never ignores permission errors, grants desktop access, or changes receiver settings. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[auth].[LicensePayments]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    THROW 51120, 'License payment schema is required before SePay receiver management.', 1;

DECLARE @DesktopRoleId int = DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole');
IF @DesktopRoleId IS NULL OR NOT EXISTS
    (SELECT 1 FROM sys.database_principals WHERE principal_id = @DesktopRoleId AND type = 'R')
    THROW 51124, 'VideoMakerDesktopRole must exist and be visible. Ask the DBA for VIEW DEFINITION on this role; do not skip desktop protection.', 1;

DECLARE @SchemaDenyCount int =
    (SELECT COUNT(*) FROM sys.database_permissions
     WHERE class = 3 AND major_id = SCHEMA_ID(N'auth') AND minor_id = 0
       AND grantee_principal_id = @DesktopRoleId AND state = 'D'
       AND permission_name IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE'));
DECLARE @ObjectDenyCount int =
    (SELECT COUNT(*) FROM sys.database_permissions
     WHERE class = 1 AND minor_id = 0 AND grantee_principal_id = @DesktopRoleId AND state = 'D'
       AND major_id IN (OBJECT_ID(N'auth.SepayReceivingAccounts'), OBJECT_ID(N'auth.SepayPaymentSettings'))
       AND permission_name IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE'));
DECLARE @CanManagePermissions bit = CASE
    WHEN IS_SRVROLEMEMBER(N'sysadmin') = 1 OR IS_MEMBER(N'db_owner') = 1
      OR IS_MEMBER(N'db_securityadmin') = 1 OR HAS_PERMS_BY_NAME(N'auth', N'SCHEMA', N'CONTROL') = 1
      OR (HAS_PERMS_BY_NAME(N'auth.SepayReceivingAccounts', N'OBJECT', N'CONTROL') = 1
          AND HAS_PERMS_BY_NAME(N'auth.SepayPaymentSettings', N'OBJECT', N'CONTROL') = 1)
    THEN 1 ELSE 0 END;

IF @SchemaDenyCount <> 4 AND @ObjectDenyCount <> 8 AND @CanManagePermissions = 0
    THROW 51122, 'Desktop protection is not verifiable with this account. Ask the DBA to verify auth schema CRUD DENY and grant metadata visibility, or run this migration with permission to DENY. No changes were made.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[auth].[SepayReceivingAccounts]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[SepayReceivingAccounts]
        (
            [AccountId] uniqueidentifier NOT NULL CONSTRAINT [PK_SepayReceivingAccounts] PRIMARY KEY,
            [DisplayName] nvarchar(100) NOT NULL,
            [BankCode] varchar(50) NOT NULL,
            [BankName] nvarchar(100) NOT NULL,
            [AccountNumber] varchar(50) NOT NULL,
            [AccountName] nvarchar(200) NOT NULL,
            [Revision] uniqueidentifier NOT NULL CONSTRAINT [DF_SepayReceivingAccounts_Revision] DEFAULT NEWID(),
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            CONSTRAINT [CK_SepayReceivingAccounts_Required] CHECK
                (LEN(LTRIM(RTRIM([DisplayName]))) > 0 AND LEN(LTRIM(RTRIM([BankName]))) > 0
                 AND LEN(LTRIM(RTRIM([AccountName]))) > 0),
            CONSTRAINT [CK_SepayReceivingAccounts_BankCode] CHECK
                (LEN([BankCode]) BETWEEN 2 AND 50 AND [BankCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9_-]%'),
            CONSTRAINT [CK_SepayReceivingAccounts_AccountNumber] CHECK
                (LEN([AccountNumber]) BETWEEN 4 AND 50 AND [AccountNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Za-z0-9]%'),
            CONSTRAINT [UQ_SepayReceivingAccounts_BankAccount] UNIQUE ([BankCode], [AccountNumber])
        );
    END;

    IF OBJECT_ID(N'[auth].[SepayPaymentSettings]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[SepayPaymentSettings]
        (
            [SettingsId] int NOT NULL CONSTRAINT [PK_SepayPaymentSettings] PRIMARY KEY,
            [Enabled] bit NOT NULL CONSTRAINT [DF_SepayPaymentSettings_Enabled] DEFAULT (0),
            [SelectedAccountId] uniqueidentifier NULL,
            [Revision] uniqueidentifier NOT NULL CONSTRAINT [DF_SepayPaymentSettings_Revision] DEFAULT NEWID(),
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            CONSTRAINT [CK_SepayPaymentSettings_Singleton] CHECK ([SettingsId] = 1),
            CONSTRAINT [CK_SepayPaymentSettings_EnabledReceiver] CHECK ([Enabled] = 0 OR [SelectedAccountId] IS NOT NULL),
            CONSTRAINT [FK_SepayPaymentSettings_ReceivingAccount] FOREIGN KEY ([SelectedAccountId])
                REFERENCES [auth].[SepayReceivingAccounts] ([AccountId])
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM [auth].[SepayPaymentSettings] WHERE [SettingsId] = 1)
        INSERT INTO [auth].[SepayPaymentSettings] ([SettingsId], [Enabled], [SelectedAccountId], [Revision], [UpdatedAtUtc])
            VALUES (1, 0, NULL, NEWID(), SYSUTCDATETIME());

    IF @SchemaDenyCount <> 4 AND @ObjectDenyCount <> 8
    BEGIN
        DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[SepayReceivingAccounts] TO [VideoMakerDesktopRole];
        DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[SepayPaymentSettings] TO [VideoMakerDesktopRole];
    END;

    /* Check the covering DENY again inside the transaction, before recording versions.
       Every permission on both objects must be covered by schema or object DENY. */
    IF EXISTS
    (
        SELECT 1
        FROM (VALUES (OBJECT_ID(N'auth.SepayReceivingAccounts')), (OBJECT_ID(N'auth.SepayPaymentSettings'))) AS t(ObjectId)
        CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE')) AS p(PermissionName)
        WHERE NOT EXISTS
        (
            SELECT 1 FROM sys.database_permissions AS d
            WHERE d.grantee_principal_id = @DesktopRoleId AND d.state = 'D' AND d.minor_id = 0
              AND d.permission_name = p.PermissionName
              AND ((d.class = 3 AND d.major_id = SCHEMA_ID(N'auth')) OR (d.class = 1 AND d.major_id = t.ObjectId))
        )
    )
        THROW 51123, 'SePay desktop protection verification failed; rolling back.', 1;

    IF OBJECT_ID(N'[auth].[CK_SepayPaymentSettings_EnabledReceiver]', N'C') IS NULL OR
       OBJECT_ID(N'[auth].[FK_SepayPaymentSettings_ReceivingAccount]', N'F') IS NULL OR
       NOT EXISTS (SELECT 1 FROM [auth].[SepayPaymentSettings] WHERE [SettingsId] = 1)
        THROW 51121, 'SePay receiver management migration verification failed.', 1;

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.10-sepay-receiving-accounts')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
            VALUES ('4.1.10-sepay-receiving-accounts', N'Admin quản lý tài khoản nhận tiền và trạng thái SePay trong SQL; giữ snapshot đơn cũ.');
    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.12-sepay-receiver-permissions')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
            VALUES ('4.1.12-sepay-receiver-permissions', N'Khởi tạo SePay với kiểm bảo vệ desktop bằng DENY schema hoặc object; hỗ trợ tài khoản migration hạn chế.');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
