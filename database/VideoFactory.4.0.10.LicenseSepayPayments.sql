/*
    License offer and SePay transfer payment support for VideoMaker 4.0.10.
    This migration is idempotent, does not publish plans, and does not seed prices or secrets.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[auth].[LicensePlans]', N'U') IS NULL OR
   OBJECT_ID(N'[auth].[UserLicenses]', N'U') IS NULL OR
   OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51100, 'Account/license schema and ai.SchemaVersions are required before the SePay license payment migration.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'auth.LicensePlans', N'SalePriceVnd') IS NULL
    BEGIN
        ALTER TABLE [auth].[LicensePlans]
            ADD [SalePriceVnd] decimal(19, 0) NULL;
    END;

    IF COL_LENGTH(N'auth.LicensePlans', N'IsPublic') IS NULL
    BEGIN
        ALTER TABLE [auth].[LicensePlans]
            ADD [IsPublic] bit NOT NULL
                CONSTRAINT [DF_LicensePlans_IsPublic] DEFAULT (0) WITH VALUES;
    END;

    IF COL_LENGTH(N'auth.LicensePlans', N'DisplayOrder') IS NULL
    BEGIN
        ALTER TABLE [auth].[LicensePlans]
            ADD [DisplayOrder] int NOT NULL
                CONSTRAINT [DF_LicensePlans_DisplayOrder] DEFAULT (0) WITH VALUES;
    END;

    IF COL_LENGTH(N'auth.LicensePlans', N'MarketingFeaturesJson') IS NULL
    BEGIN
        ALTER TABLE [auth].[LicensePlans]
            ADD [MarketingFeaturesJson] nvarchar(max) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[auth].[LicensePlans]')
          AND [name] = N'CK_LicensePlans_SalePriceVnd'
    )
    BEGIN
        -- Dynamic SQL defers name resolution until the columns added above exist.
        EXEC sys.sp_executesql N'
            ALTER TABLE [auth].[LicensePlans] WITH CHECK
                ADD CONSTRAINT [CK_LicensePlans_SalePriceVnd]
                    CHECK ([SalePriceVnd] IS NULL OR [SalePriceVnd] > 0);';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[auth].[LicensePlans]')
          AND [name] = N'CK_LicensePlans_PublicSale'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [auth].[LicensePlans] WITH CHECK
                ADD CONSTRAINT [CK_LicensePlans_PublicSale]
                    CHECK ([IsPublic] = 0 OR
                        ([SalePriceVnd] IS NOT NULL AND
                         [SalePriceVnd] > 0 AND
                         [DefaultDurationDays] > 0 AND
                         [DefaultDurationDays] <= 3650));';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[auth].[LicensePlans]')
          AND [name] = N'CK_LicensePlans_DisplayOrder'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [auth].[LicensePlans] WITH CHECK
                ADD CONSTRAINT [CK_LicensePlans_DisplayOrder]
                    CHECK ([DisplayOrder] >= 0);';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[auth].[LicensePlans]')
          AND [name] = N'CK_LicensePlans_MarketingFeaturesJson'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [auth].[LicensePlans] WITH CHECK
                ADD CONSTRAINT [CK_LicensePlans_MarketingFeaturesJson]
                    CHECK ([MarketingFeaturesJson] IS NULL OR ISJSON([MarketingFeaturesJson]) = 1);';
    END;

    IF OBJECT_ID(N'[auth].[LicensePayments]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[LicensePayments]
        (
            [LicensePaymentId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_LicensePayments_Id] DEFAULT NEWSEQUENTIALID(),
            [UserId] nvarchar(450) NOT NULL,
            [LicensePlanId] uniqueidentifier NOT NULL,
            [OrderCode] varchar(40) NOT NULL,
            [TransferCode] varchar(40) NOT NULL,
            [IdempotencyKey] varchar(100) NOT NULL,
            [PriceSnapshotVnd] decimal(19, 0) NOT NULL,
            [DurationSnapshotDays] int NOT NULL,
            [PlanCodeSnapshot] varchar(50) NOT NULL,
            [PlanNameSnapshot] nvarchar(200) NOT NULL,
            [EntitlementSnapshotJson] nvarchar(max) NULL,
            [Status] varchar(20) NOT NULL
                CONSTRAINT [DF_LicensePayments_Status] DEFAULT ('Pending'),
            [ReceiverBankCodeSnapshot] varchar(50) NOT NULL,
            [ReceiverAccountNumberSnapshot] varchar(50) NOT NULL,
            [ReceiverAccountNameSnapshot] nvarchar(200) NOT NULL,
            [ProviderTransactionId] bigint NULL,
            [ProviderReferenceCode] nvarchar(100) NULL,
            [FulfilledUserLicenseId] uniqueidentifier NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_LicensePayments_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [PaidAtUtc] datetime2(3) NULL,
            [FulfilledAtUtc] datetime2(3) NULL,
            [FailureCode] varchar(100) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_LicensePayments] PRIMARY KEY CLUSTERED ([LicensePaymentId]),
            CONSTRAINT [FK_LicensePayments_Users]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_LicensePayments_LicensePlans]
                FOREIGN KEY ([LicensePlanId]) REFERENCES [auth].[LicensePlans]([LicensePlanId]),
            CONSTRAINT [FK_LicensePayments_UserLicenses]
                FOREIGN KEY ([FulfilledUserLicenseId]) REFERENCES [auth].[UserLicenses]([UserLicenseId]),
            CONSTRAINT [CK_LicensePayments_Price]
                CHECK ([PriceSnapshotVnd] > 0),
            CONSTRAINT [CK_LicensePayments_Duration]
                CHECK ([DurationSnapshotDays] > 0 AND [DurationSnapshotDays] <= 3650),
            CONSTRAINT [CK_LicensePayments_Status]
                CHECK ([Status] IN ('Pending', 'Paid', 'Fulfilled', 'Expired', 'Failed')),
            CONSTRAINT [CK_LicensePayments_EntitlementJson]
                CHECK ([EntitlementSnapshotJson] IS NULL OR ISJSON([EntitlementSnapshotJson]) = 1),
            CONSTRAINT [CK_LicensePayments_Expiry]
                CHECK ([ExpiresAtUtc] > [CreatedAtUtc])
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[LicensePayments]')
          AND [name] = N'UQ_LicensePayments_OrderCode'
    )
    BEGIN
        CREATE UNIQUE INDEX [UQ_LicensePayments_OrderCode]
            ON [auth].[LicensePayments]([OrderCode]);
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[LicensePayments]')
          AND [name] = N'UQ_LicensePayments_TransferCode'
    )
    BEGIN
        CREATE UNIQUE INDEX [UQ_LicensePayments_TransferCode]
            ON [auth].[LicensePayments]([TransferCode]);
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[LicensePayments]')
          AND [name] = N'UQ_LicensePayments_UserIdempotency'
    )
    BEGIN
        CREATE UNIQUE INDEX [UQ_LicensePayments_UserIdempotency]
            ON [auth].[LicensePayments]([UserId], [IdempotencyKey]);
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[LicensePayments]')
          AND [name] = N'UQ_LicensePayments_ProviderTransactionId'
    )
    BEGIN
        CREATE UNIQUE INDEX [UQ_LicensePayments_ProviderTransactionId]
            ON [auth].[LicensePayments]([ProviderTransactionId])
            WHERE [ProviderTransactionId] IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[LicensePayments]')
          AND [name] = N'IX_LicensePayments_UserPlanStatusExpiry'
    )
    BEGIN
        CREATE INDEX [IX_LicensePayments_UserPlanStatusExpiry]
            ON [auth].[LicensePayments]([UserId], [LicensePlanId], [Status], [ExpiresAtUtc]);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.10-license-sepay-payments'
    )
    BEGIN
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.10-license-sepay-payments',
            N'Thêm catalog gói bán công khai và giao dịch chuyển khoản SePay để tự gia hạn license.'
        );
    END;

    IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
    BEGIN
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[auth].[LicensePayments]
            TO [VideoMakerDesktopRole];
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF COL_LENGTH(N'auth.LicensePlans', N'SalePriceVnd') IS NULL OR
   COL_LENGTH(N'auth.LicensePlans', N'IsPublic') IS NULL OR
   COL_LENGTH(N'auth.LicensePlans', N'DisplayOrder') IS NULL OR
   COL_LENGTH(N'auth.LicensePlans', N'MarketingFeaturesJson') IS NULL OR
   OBJECT_ID(N'[auth].[LicensePayments]', N'U') IS NULL OR
   NOT EXISTS
   (
       SELECT 1
       FROM [ai].[SchemaVersions]
       WHERE [Version] = '4.0.10-license-sepay-payments'
   )
BEGIN
    THROW 51101, 'SePay license payment migration verification failed.', 1;
END;
