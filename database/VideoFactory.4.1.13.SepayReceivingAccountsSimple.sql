/* SePay receiver tables and configuration only.
   Run in the application database after the account/payment baseline.
   Re-running preserves receiving accounts and existing admin configuration. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[auth].[LicensePayments]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    THROW 51120, 'License payment schema is required before SePay receiver management.', 1;

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

    IF OBJECT_ID(N'[auth].[CK_SepayPaymentSettings_Singleton]', N'C') IS NULL OR
       OBJECT_ID(N'[auth].[CK_SepayPaymentSettings_EnabledReceiver]', N'C') IS NULL OR
       OBJECT_ID(N'[auth].[FK_SepayPaymentSettings_ReceivingAccount]', N'F') IS NULL OR
       OBJECT_ID(N'[auth].[UQ_SepayReceivingAccounts_BankAccount]', N'UQ') IS NULL
        THROW 51121, 'SePay receiver management migration verification failed.', 1;

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.10-sepay-receiving-accounts')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
            VALUES ('4.1.10-sepay-receiving-accounts', N'Admin quản lý tài khoản nhận tiền và trạng thái SePay trong SQL; giữ snapshot đơn cũ.');

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.13-sepay-receivers-simple')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
            VALUES ('4.1.13-sepay-receivers-simple', N'Khởi tạo bảng và cấu hình SePay theo luồng đơn giản, giữ nguyên quyền database hiện có.');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

PRINT N'SePay: bảng và cấu hình đã sẵn sàng. Cấu hình mới mặc định tắt thanh toán; cấu hình đã có được giữ nguyên.';
