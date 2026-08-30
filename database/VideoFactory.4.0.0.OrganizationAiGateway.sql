/*
    VideoFactory 4.0.0 - Organization AI Gateway
    Run after database/VideoFactory.Initial.sql.

    This migration is idempotent and does not delete user data. It introduces:
      - organizations, members and organization roles;
      - organization-scoped encrypted provider credentials;
      - atomic monthly budget periods, reservations and usage ledger;
      - tenant/user/cost attribution on Projects and ProviderRequests.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'ai') IS NULL
    EXEC(N'CREATE SCHEMA [ai] AUTHORIZATION [dbo];');
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[SchemaVersions]
        (
            [SchemaVersionId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AiSchemaVersions] PRIMARY KEY,
            [Version] varchar(50) NOT NULL CONSTRAINT [UQ_AiSchemaVersions_Version] UNIQUE,
            [Description] nvarchar(500) NULL,
            [AppliedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_AiSchemaVersions_AppliedAtUtc] DEFAULT SYSUTCDATETIME()
        );
    END;

    IF OBJECT_ID(N'[ai].[Organizations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[Organizations]
        (
            [OrganizationId] uniqueidentifier NOT NULL CONSTRAINT [DF_Organizations_Id] DEFAULT NEWSEQUENTIALID(),
            [Code] varchar(80) NOT NULL,
            [Name] nvarchar(200) NOT NULL,
            [Status] varchar(20) NOT NULL CONSTRAINT [DF_Organizations_Status] DEFAULT ('Active'),
            [MonthlyBudgetLimit] decimal(19,6) NOT NULL CONSTRAINT [DF_Organizations_Budget] DEFAULT (0),
            [CurrencyCode] char(3) NOT NULL CONSTRAINT [DF_Organizations_Currency] DEFAULT ('USD'),
            [CreatedByUserId] nvarchar(450) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Organizations_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_Organizations_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_Organizations] PRIMARY KEY ([OrganizationId]),
            CONSTRAINT [UQ_Organizations_Code] UNIQUE ([Code]),
            CONSTRAINT [FK_Organizations_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_Organizations_Status] CHECK ([Status] IN ('Active','Suspended','Archived')),
            CONSTRAINT [CK_Organizations_Budget] CHECK ([MonthlyBudgetLimit] >= 0),
            CONSTRAINT [CK_Organizations_Currency] CHECK ([CurrencyCode] = 'USD')
        );
    END;

    IF OBJECT_ID(N'[ai].[OrganizationMembers]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationMembers]
        (
            [OrganizationId] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [Role] varchar(30) NOT NULL CONSTRAINT [DF_OrganizationMembers_Role] DEFAULT ('Member'),
            [Status] varchar(20) NOT NULL CONSTRAINT [DF_OrganizationMembers_Status] DEFAULT ('Active'),
            [MonthlyBudgetLimit] decimal(19,6) NULL,
            [JoinedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationMembers_JoinedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationMembers_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationMembers] PRIMARY KEY ([OrganizationId], [UserId]),
            CONSTRAINT [FK_OrganizationMembers_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [FK_OrganizationMembers_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_OrganizationMembers_Role] CHECK ([Role] IN ('Owner','OrganizationAdmin','BillingManager','Member','Viewer')),
            CONSTRAINT [CK_OrganizationMembers_Status] CHECK ([Status] IN ('Active','Suspended','Removed')),
            CONSTRAINT [CK_OrganizationMembers_Budget] CHECK ([MonthlyBudgetLimit] IS NULL OR [MonthlyBudgetLimit] >= 0)
        );
        CREATE INDEX [IX_OrganizationMembers_User_Status]
            ON [ai].[OrganizationMembers] ([UserId], [Status]);
    END;

    IF OBJECT_ID(N'[ai].[OrganizationProviderCredentials]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationProviderCredentials]
        (
            [OrganizationProviderCredentialId] uniqueidentifier NOT NULL CONSTRAINT [DF_OrganizationProviderCredentials_Id] DEFAULT NEWSEQUENTIALID(),
            [OrganizationId] uniqueidentifier NOT NULL,
            [ProviderId] uniqueidentifier NOT NULL,
            [Version] int NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [EncryptedPayload] nvarchar(max) NOT NULL,
            [SecretHint] varchar(16) NOT NULL,
            [Status] varchar(20) NOT NULL CONSTRAINT [DF_OrganizationProviderCredentials_Status] DEFAULT ('Active'),
            [CreatedByUserId] nvarchar(450) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationProviderCredentials_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationProviderCredentials_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RetiredAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationProviderCredentials] PRIMARY KEY ([OrganizationProviderCredentialId]),
            CONSTRAINT [FK_OrganizationProviderCredentials_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [FK_OrganizationProviderCredentials_Providers] FOREIGN KEY ([ProviderId]) REFERENCES [vf].[Providers]([ProviderId]),
            CONSTRAINT [FK_OrganizationProviderCredentials_CreatedBy] FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [UQ_OrganizationProviderCredentials_Version] UNIQUE ([OrganizationId], [ProviderId], [Version]),
            CONSTRAINT [CK_OrganizationProviderCredentials_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_OrganizationProviderCredentials_Status] CHECK ([Status] IN ('Active','Retiring','Revoked'))
        );
        CREATE UNIQUE INDEX [UX_OrganizationProviderCredentials_Active]
            ON [ai].[OrganizationProviderCredentials] ([OrganizationId], [ProviderId])
            WHERE [Status] = 'Active';
        CREATE INDEX [IX_OrganizationProviderCredentials_Status]
            ON [ai].[OrganizationProviderCredentials] ([OrganizationId], [ProviderId], [Status]);
    END;

    IF OBJECT_ID(N'[ai].[OrganizationBudgetPeriods]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationBudgetPeriods]
        (
            [OrganizationBudgetPeriodId] uniqueidentifier NOT NULL CONSTRAINT [DF_OrganizationBudgetPeriods_Id] DEFAULT NEWSEQUENTIALID(),
            [OrganizationId] uniqueidentifier NOT NULL,
            [StartsAtUtc] datetime2(3) NOT NULL,
            [EndsAtUtc] datetime2(3) NOT NULL,
            [HardLimit] decimal(19,6) NOT NULL,
            [ReservedCost] decimal(19,6) NOT NULL CONSTRAINT [DF_OrganizationBudgetPeriods_Reserved] DEFAULT (0),
            [ActualCost] decimal(19,6) NOT NULL CONSTRAINT [DF_OrganizationBudgetPeriods_Actual] DEFAULT (0),
            [CurrencyCode] char(3) NOT NULL CONSTRAINT [DF_OrganizationBudgetPeriods_Currency] DEFAULT ('USD'),
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationBudgetPeriods_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationBudgetPeriods_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationBudgetPeriods] PRIMARY KEY ([OrganizationBudgetPeriodId]),
            CONSTRAINT [FK_OrganizationBudgetPeriods_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [UQ_OrganizationBudgetPeriods_Start] UNIQUE ([OrganizationId], [StartsAtUtc]),
            CONSTRAINT [CK_OrganizationBudgetPeriods_Dates] CHECK ([EndsAtUtc] > [StartsAtUtc]),
            CONSTRAINT [CK_OrganizationBudgetPeriods_Cost] CHECK ([HardLimit] >= 0 AND [ReservedCost] >= 0 AND [ActualCost] >= 0),
            CONSTRAINT [CK_OrganizationBudgetPeriods_Currency] CHECK ([CurrencyCode] = 'USD')
        );
    END;

    IF OBJECT_ID(N'[ai].[BudgetReservations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[BudgetReservations]
        (
            [AiBudgetReservationId] uniqueidentifier NOT NULL CONSTRAINT [DF_BudgetReservations_Id] DEFAULT NEWSEQUENTIALID(),
            [OrganizationBudgetPeriodId] uniqueidentifier NOT NULL,
            [OrganizationId] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [ProjectId] uniqueidentifier NOT NULL,
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [OperationKey] nvarchar(450) NOT NULL,
            [ProviderCode] varchar(80) NOT NULL,
            [ModelCode] nvarchar(200) NOT NULL,
            [ReservedAmount] decimal(19,6) NOT NULL,
            [ActualAmount] decimal(19,6) NOT NULL CONSTRAINT [DF_BudgetReservations_Actual] DEFAULT (0),
            [CurrencyCode] char(3) NOT NULL CONSTRAINT [DF_BudgetReservations_Currency] DEFAULT ('USD'),
            [Status] varchar(20) NOT NULL CONSTRAINT [DF_BudgetReservations_Status] DEFAULT ('Reserved'),
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_BudgetReservations_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [SettledAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_BudgetReservations] PRIMARY KEY ([AiBudgetReservationId]),
            CONSTRAINT [FK_BudgetReservations_Periods] FOREIGN KEY ([OrganizationBudgetPeriodId]) REFERENCES [ai].[OrganizationBudgetPeriods]([OrganizationBudgetPeriodId]),
            CONSTRAINT [FK_BudgetReservations_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [FK_BudgetReservations_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_BudgetReservations_Projects] FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [UQ_BudgetReservations_Operation] UNIQUE ([OrganizationId], [OperationKey]),
            CONSTRAINT [UQ_BudgetReservations_ProviderRequest] UNIQUE ([ProviderRequestId]),
            CONSTRAINT [CK_BudgetReservations_Status] CHECK ([Status] IN ('Reserved','Settled','Released','Expired')),
            CONSTRAINT [CK_BudgetReservations_Cost] CHECK ([ReservedAmount] >= 0 AND [ActualAmount] >= 0),
            CONSTRAINT [CK_BudgetReservations_Currency] CHECK ([CurrencyCode] = 'USD')
        );
        CREATE INDEX [IX_BudgetReservations_Period_Status_User]
            ON [ai].[BudgetReservations] ([OrganizationBudgetPeriodId], [Status], [UserId]);
    END;

    IF OBJECT_ID(N'[ai].[UsageLedger]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[UsageLedger]
        (
            [AiUsageLedgerEntryId] uniqueidentifier NOT NULL CONSTRAINT [DF_UsageLedger_Id] DEFAULT NEWSEQUENTIALID(),
            [OrganizationBudgetPeriodId] uniqueidentifier NOT NULL,
            [OrganizationId] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [ProjectId] uniqueidentifier NOT NULL,
            [ProviderRequestId] uniqueidentifier NULL,
            [OrganizationProviderCredentialId] uniqueidentifier NULL,
            [ProviderCode] varchar(80) NOT NULL,
            [ModelCode] nvarchar(200) NOT NULL,
            [EntryKind] varchar(20) NOT NULL,
            [Amount] decimal(19,6) NOT NULL,
            [CurrencyCode] char(3) NOT NULL CONSTRAINT [DF_UsageLedger_Currency] DEFAULT ('USD'),
            [UsageJson] nvarchar(max) NULL,
            [RateSnapshotJson] nvarchar(max) NULL,
            [OccurredAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_UsageLedger_OccurredAtUtc] DEFAULT SYSUTCDATETIME(),
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_UsageLedger_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_UsageLedger] PRIMARY KEY ([AiUsageLedgerEntryId]),
            CONSTRAINT [FK_UsageLedger_Periods] FOREIGN KEY ([OrganizationBudgetPeriodId]) REFERENCES [ai].[OrganizationBudgetPeriods]([OrganizationBudgetPeriodId]),
            CONSTRAINT [FK_UsageLedger_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [FK_UsageLedger_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_UsageLedger_Projects] FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_UsageLedger_Credentials] FOREIGN KEY ([OrganizationProviderCredentialId]) REFERENCES [ai].[OrganizationProviderCredentials]([OrganizationProviderCredentialId]),
            CONSTRAINT [CK_UsageLedger_Kind] CHECK ([EntryKind] IN ('Reservation','Actual','Release','Adjustment','Refund')),
            CONSTRAINT [CK_UsageLedger_Amount] CHECK ([Amount] >= 0),
            CONSTRAINT [CK_UsageLedger_Currency] CHECK ([CurrencyCode] = 'USD'),
            CONSTRAINT [CK_UsageLedger_UsageJson] CHECK ([UsageJson] IS NULL OR ISJSON([UsageJson]) = 1),
            CONSTRAINT [CK_UsageLedger_RateJson] CHECK ([RateSnapshotJson] IS NULL OR ISJSON([RateSnapshotJson]) = 1)
        );
        CREATE INDEX [IX_UsageLedger_Organization_Occurred]
            ON [ai].[UsageLedger] ([OrganizationId], [OccurredAtUtc] DESC);
        CREATE INDEX [IX_UsageLedger_Period_User_Kind]
            ON [ai].[UsageLedger] ([OrganizationBudgetPeriodId], [UserId], [EntryKind]);
        CREATE INDEX [IX_UsageLedger_ProviderRequest]
            ON [ai].[UsageLedger] ([ProviderRequestId]);
    END;

    IF OBJECT_ID(N'[ai].[OrganizationAuditLogs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationAuditLogs]
        (
            [OrganizationAuditLogId] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OrganizationAuditLogs] PRIMARY KEY,
            [OrganizationId] uniqueidentifier NOT NULL,
            [ActorUserId] nvarchar(450) NULL,
            [EventType] varchar(100) NOT NULL,
            [DataJson] nvarchar(max) NULL,
            [IpAddress] varchar(45) NULL,
            [UserAgent] nvarchar(1000) NULL,
            [CorrelationId] varchar(100) NULL,
            [OccurredAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_OrganizationAuditLogs_OccurredAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [FK_OrganizationAuditLogs_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]),
            CONSTRAINT [FK_OrganizationAuditLogs_Actors] FOREIGN KEY ([ActorUserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_OrganizationAuditLogs_DataJson] CHECK ([DataJson] IS NULL OR ISJSON([DataJson]) = 1)
        );
        CREATE INDEX [IX_OrganizationAuditLogs_Organization_Occurred]
            ON [ai].[OrganizationAuditLogs] ([OrganizationId], [OccurredAtUtc] DESC);
    END;

    IF COL_LENGTH(N'vf.Projects', N'OrganizationId') IS NULL
        ALTER TABLE [vf].[Projects] ADD [OrganizationId] uniqueidentifier NULL;
    IF COL_LENGTH(N'vf.Projects', N'CreatedByUserId') IS NULL
        ALTER TABLE [vf].[Projects] ADD [CreatedByUserId] nvarchar(450) NULL;

    /*
       SQL Server compiles the containing batch before executing ALTER TABLE.
       Compile statements that reference newly-added columns only after those
       ALTER statements have completed.
    */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_Projects_Organizations')
        EXEC(N'ALTER TABLE [vf].[Projects] ADD CONSTRAINT [FK_Projects_Organizations]
            FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_Projects_CreatedByUser')
        EXEC(N'ALTER TABLE [vf].[Projects] ADD CONSTRAINT [FK_Projects_CreatedByUser]
            FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]);');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[vf].[Projects]') AND [name] = N'IX_Projects_Organization_Status')
        EXEC(N'CREATE INDEX [IX_Projects_Organization_Status]
            ON [vf].[Projects] ([OrganizationId], [Status], [UpdatedAtUtc] DESC)
            WHERE [OrganizationId] IS NOT NULL;');

    IF COL_LENGTH(N'vf.ProviderRequests', N'OrganizationId') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [OrganizationId] uniqueidentifier NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'RequestedByUserId') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [RequestedByUserId] nvarchar(450) NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'OrganizationProviderCredentialId') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [OrganizationProviderCredentialId] uniqueidentifier NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'BudgetReservationId') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [BudgetReservationId] uniqueidentifier NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'RequestHash') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [RequestHash] char(64) NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'InputTokens') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [InputTokens] bigint NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'OutputTokens') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [OutputTokens] bigint NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'UsageJson') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [UsageJson] nvarchar(max) NULL;
    IF COL_LENGTH(N'vf.ProviderRequests', N'RateSnapshotJson') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [RateSnapshotJson] nvarchar(max) NULL;

    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]') AND [name] = N'UQ_ProviderRequests_IdempotencyKey')
        ALTER TABLE [vf].[ProviderRequests] DROP CONSTRAINT [UQ_ProviderRequests_IdempotencyKey];
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]') AND [name] = N'UQ_ProviderRequests_IdempotencyKey')
        DROP INDEX [UQ_ProviderRequests_IdempotencyKey] ON [vf].[ProviderRequests];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]') AND [name] = N'UQ_ProviderRequests_Organization_Idempotency')
        EXEC(N'CREATE UNIQUE INDEX [UQ_ProviderRequests_Organization_Idempotency]
            ON [vf].[ProviderRequests] ([OrganizationId], [IdempotencyKey]);');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]') AND [name] = N'IX_ProviderRequests_Organization_User_Created')
        EXEC(N'CREATE INDEX [IX_ProviderRequests_Organization_User_Created]
            ON [vf].[ProviderRequests] ([OrganizationId], [RequestedByUserId], [CreatedAtUtc] DESC);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ProviderRequests_Organizations')
        EXEC(N'ALTER TABLE [vf].[ProviderRequests] ADD CONSTRAINT [FK_ProviderRequests_Organizations]
            FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations]([OrganizationId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ProviderRequests_RequestedByUser')
        EXEC(N'ALTER TABLE [vf].[ProviderRequests] ADD CONSTRAINT [FK_ProviderRequests_RequestedByUser]
            FOREIGN KEY ([RequestedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ProviderRequests_OrganizationCredential')
        EXEC(N'ALTER TABLE [vf].[ProviderRequests] ADD CONSTRAINT [FK_ProviderRequests_OrganizationCredential]
            FOREIGN KEY ([OrganizationProviderCredentialId]) REFERENCES [ai].[OrganizationProviderCredentials]([OrganizationProviderCredentialId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ProviderRequests_BudgetReservation')
        EXEC(N'ALTER TABLE [vf].[ProviderRequests] ADD CONSTRAINT [FK_ProviderRequests_BudgetReservation]
            FOREIGN KEY ([BudgetReservationId]) REFERENCES [ai].[BudgetReservations]([AiBudgetReservationId]);');

    /* Backward-compatible tenant assignment for existing installations. */
    DECLARE @BootstrapOwnerId nvarchar(450) =
    (
        SELECT TOP (1) u.[Id]
        FROM [dbo].[AspNetUsers] u
        LEFT JOIN [dbo].[AspNetUserRoles] ur ON ur.[UserId] = u.[Id]
        LEFT JOIN [dbo].[AspNetRoles] r ON r.[Id] = ur.[RoleId] AND r.[NormalizedName] = N'ADMIN'
        WHERE u.[DeletedAtUtc] IS NULL
        ORDER BY CASE WHEN r.[Id] IS NULL THEN 1 ELSE 0 END, u.[CreatedAtUtc]
    );
    DECLARE @DefaultOrganizationId uniqueidentifier =
    (
        SELECT TOP (1) [OrganizationId] FROM [ai].[Organizations] WHERE [Code] = 'legacy-default'
    );
    IF @DefaultOrganizationId IS NULL AND @BootstrapOwnerId IS NOT NULL
    BEGIN
        SET @DefaultOrganizationId = NEWID();
        INSERT INTO [ai].[Organizations]
            ([OrganizationId], [Code], [Name], [Status], [MonthlyBudgetLimit], [CurrencyCode], [CreatedByUserId])
        VALUES
            (@DefaultOrganizationId, 'legacy-default', N'VideoMaker mặc định', 'Active', 0, 'USD', @BootstrapOwnerId);
    END;
    IF @DefaultOrganizationId IS NOT NULL
    BEGIN
        INSERT INTO [ai].[OrganizationMembers]
            ([OrganizationId], [UserId], [Role], [Status], [JoinedAtUtc], [UpdatedAtUtc])
        SELECT
            @DefaultOrganizationId,
            u.[Id],
            CASE WHEN EXISTS
            (
                SELECT 1
                FROM [dbo].[AspNetUserRoles] ur
                INNER JOIN [dbo].[AspNetRoles] r ON r.[Id] = ur.[RoleId]
                WHERE ur.[UserId] = u.[Id] AND r.[NormalizedName] = N'ADMIN'
            ) THEN 'Owner' ELSE 'Member' END,
            'Active',
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        FROM [dbo].[AspNetUsers] u
        WHERE u.[DeletedAtUtc] IS NULL
          AND NOT EXISTS
          (
              SELECT 1 FROM [ai].[OrganizationMembers] m
              WHERE m.[OrganizationId] = @DefaultOrganizationId AND m.[UserId] = u.[Id]
          );

        EXEC sys.sp_executesql
            N'UPDATE p
                SET p.[OrganizationId] = @DefaultOrganizationId,
                    p.[CreatedByUserId] = COALESCE(p.[CreatedByUserId], p.[RemoteUserId])
              FROM [vf].[Projects] p
              WHERE p.[OrganizationId] IS NULL
                AND EXISTS
                (
                    SELECT 1 FROM [ai].[OrganizationMembers] m
                    WHERE m.[OrganizationId] = @DefaultOrganizationId AND m.[UserId] = p.[RemoteUserId]
                );',
            N'@DefaultOrganizationId uniqueidentifier',
            @DefaultOrganizationId = @DefaultOrganizationId;
    END;

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.0.0-organization-ai-gateway')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES ('4.0.0-organization-ai-gateway', N'Organization RBAC, server-only provider credentials, budget reservation and per-member usage ledger.');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    ;THROW 51000, 'VideoFactory AI Gateway migration did not create ai.SchemaVersions.', 1;
END;

EXEC sys.sp_executesql N'
    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = ''4.0.0-organization-ai-gateway''
    )
    BEGIN
        ;THROW 51001, ''VideoFactory AI Gateway migration version 4.0.0 was not recorded.'', 1;
    END;';

PRINT N'VideoFactory AI Gateway schema 4.0.0 is ready.';
EXEC sys.sp_executesql N'
    SELECT [Version], [Description], [AppliedAtUtc]
    FROM [ai].[SchemaVersions]
    ORDER BY [SchemaVersionId] DESC;';
GO
