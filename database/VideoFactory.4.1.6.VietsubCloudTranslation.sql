/* Server-owned Cloud translation. Rehearse on a verified clone before deployment. */
USE [VideoFactory];
GO
SET XACT_ABORT ON;
SET NOCOUNT ON;
-- Required for filtered indexes, including when executed by sqlcmd defaults.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'vs.Projects', N'U') IS NULL OR OBJECT_ID(N'ai.SchemaVersions', N'U') IS NULL
        THROW 51160, 'Vietsub registry and AI governance migrations are required.', 1;

    IF OBJECT_ID(N'vs.CloudTranslationJobs', N'U') IS NULL
    CREATE TABLE [vs].[CloudTranslationJobs] (
        [Id] uniqueidentifier NOT NULL PRIMARY KEY,
        [OrganizationId] uniqueidentifier NOT NULL REFERENCES [ai].[Organizations]([OrganizationId]),
        [ProjectId] uniqueidentifier NOT NULL REFERENCES [vs].[Projects]([ProjectId]),
        [UserId] nvarchar(450) NOT NULL REFERENCES [dbo].[AspNetUsers]([Id]),
        [SessionId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NOT NULL,
        [ClientOperationId] uniqueidentifier NOT NULL,
        [TrackId] uniqueidentifier NOT NULL,
        [SnapshotHash] varchar(64) NOT NULL,
        [ProtectedInput] nvarchar(max) NULL,
        [ModelCode] nvarchar(200) NOT NULL,
        [ProviderModelId] uniqueidentifier NOT NULL REFERENCES [vf].[ProviderModels]([ProviderModelId]),
        [CredentialId] uniqueidentifier NOT NULL REFERENCES [ai].[OrganizationProviderCredentials]([OrganizationProviderCredentialId]),
        [RateSnapshotJson] nvarchar(max) NOT NULL,
        [CurrencyCode] varchar(3) NOT NULL,
        [PromptVersion] nvarchar(40) NOT NULL,
        [MaximumOutputTokens] int NOT NULL,
        [Status] varchar(24) NOT NULL,
        [Active] bit NOT NULL,
        [ErrorCode] varchar(100) NULL,
        [TotalCues] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [FinishedAtUtc] datetime2 NULL,
        [ResultExpiresAtUtc] datetime2 NULL,
        [LeaseOwner] uniqueidentifier NULL,
        [LeaseUntilUtc] datetime2 NULL,
        [Acknowledged] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [UQ_CloudJobs_Operation] UNIQUE ([OrganizationId], [ClientOperationId])
    );
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'vs.CloudTranslationJobs') AND name = N'UX_CloudJobs_ActiveProject')
        CREATE UNIQUE INDEX [UX_CloudJobs_ActiveProject] ON [vs].[CloudTranslationJobs]([ProjectId]) WHERE [Active] = 1;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'vs.CloudTranslationJobs') AND name = N'IX_CloudJobs_Queue')
        CREATE INDEX [IX_CloudJobs_Queue] ON [vs].[CloudTranslationJobs]([Status], [LeaseUntilUtc]);

    IF OBJECT_ID(N'vs.CloudTranslationBatches', N'U') IS NULL
    CREATE TABLE [vs].[CloudTranslationBatches] (
        [Id] uniqueidentifier NOT NULL PRIMARY KEY,
        [JobId] uniqueidentifier NOT NULL REFERENCES [vs].[CloudTranslationJobs]([Id]),
        [Ordinal] int NOT NULL,
        [Attempt] int NOT NULL,
        [TargetCount] int NOT NULL,
        [Status] varchar(24) NOT NULL,
        [RequestId] uniqueidentifier NOT NULL UNIQUE,
        [ReservationId] uniqueidentifier NULL REFERENCES [ai].[BudgetReservations]([AiBudgetReservationId]),
        [EstimatedCost] decimal(19,6) NOT NULL,
        [ActualCost] decimal(19,6) NOT NULL,
        [UsageJson] nvarchar(max) NULL,
        [ResponseId] nvarchar(200) NULL,
        [ErrorCode] varchar(100) NULL,
        [ProtectedResult] nvarchar(max) NULL,
        [Settled] bit NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [UQ_CloudBatches_Ordinal] UNIQUE ([JobId], [Ordinal])
    );

    IF OBJECT_ID(N'vs.CloudTranslationAttempts', N'U') IS NULL
    CREATE TABLE [vs].[CloudTranslationAttempts] (
        [RequestId] uniqueidentifier NOT NULL PRIMARY KEY,
        [JobId] uniqueidentifier NOT NULL REFERENCES [vs].[CloudTranslationJobs]([Id]),
        [Ordinal] int NOT NULL,
        [Attempt] int NOT NULL,
        [Status] varchar(24) NOT NULL,
        [UsageJson] nvarchar(max) NULL,
        [ResponseId] nvarchar(200) NULL,
        [ErrorCode] varchar(100) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [UQ_CloudAttempts_OrdinalAttempt] UNIQUE ([JobId], [Ordinal], [Attempt])
    );

    IF COL_LENGTH(N'ai.BudgetReservations', N'VietsubProjectId') IS NULL
        ALTER TABLE [ai].[BudgetReservations] ADD [VietsubProjectId] uniqueidentifier NULL;
    IF COL_LENGTH(N'ai.UsageLedger', N'VietsubProjectId') IS NULL
        ALTER TABLE [ai].[UsageLedger] ADD [VietsubProjectId] uniqueidentifier NULL;
    ALTER TABLE [ai].[BudgetReservations] ALTER COLUMN [ProjectId] uniqueidentifier NULL;
    ALTER TABLE [ai].[UsageLedger] ALTER COLUMN [ProjectId] uniqueidentifier NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_BudgetReservations_VietsubProjects')
        EXEC(N'ALTER TABLE [ai].[BudgetReservations] WITH CHECK ADD CONSTRAINT [FK_BudgetReservations_VietsubProjects] FOREIGN KEY ([VietsubProjectId]) REFERENCES [vs].[Projects]([ProjectId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_UsageLedger_VietsubProjects')
        EXEC(N'ALTER TABLE [ai].[UsageLedger] WITH CHECK ADD CONSTRAINT [FK_UsageLedger_VietsubProjects] FOREIGN KEY ([VietsubProjectId]) REFERENCES [vs].[Projects]([ProjectId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BudgetReservations_ProjectKind')
        EXEC(N'ALTER TABLE [ai].[BudgetReservations] WITH CHECK ADD CONSTRAINT [CK_BudgetReservations_ProjectKind] CHECK (([ProjectId] IS NOT NULL AND [VietsubProjectId] IS NULL) OR ([ProjectId] IS NULL AND [VietsubProjectId] IS NOT NULL));');
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_UsageLedger_ProjectKind')
        EXEC(N'ALTER TABLE [ai].[UsageLedger] WITH CHECK ADD CONSTRAINT [CK_UsageLedger_ProjectKind] CHECK (([ProjectId] IS NOT NULL AND [VietsubProjectId] IS NULL) OR ([ProjectId] IS NULL AND [VietsubProjectId] IS NOT NULL));');

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = N'4.1.6')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description], [AppliedAtUtc])
        VALUES (N'4.1.6', N'Vietsub Cloud translation jobs and budget project references', SYSUTCDATETIME());
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'vs.CloudTranslationJobs', N'U') IS NULL
    OR OBJECT_ID(N'vs.CloudTranslationBatches', N'U') IS NULL
    OR OBJECT_ID(N'vs.CloudTranslationAttempts', N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE Version = N'4.1.6')
    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_BudgetReservations_ProjectKind' AND is_disabled = 0 AND is_not_trusted = 0)
    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_UsageLedger_ProjectKind' AND is_disabled = 0 AND is_not_trusted = 0)
    THROW 51161, 'Vietsub Cloud schema verification failed.', 1;
PRINT 'VideoFactory Vietsub Cloud schema 4.1.6 is ready. Runtime remains disabled until configured.';
GO
