/*
    Global Admin management for the TikTok Developer App credential.

    The protected payload contains Client Key + Client Secret encrypted by the
    server Data Protection key ring. A credential remains Pending until a
    Global Admin completes a real Desktop OAuth exchange with TikTok.

    This migration is idempotent. Run only after a verified backup and after
    VideoFactory.4.1.6.TikTokPublishing.sql.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
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

    IF OBJECT_ID(N'[social].[TikTokConnections]', N'U') IS NULL OR
       OBJECT_ID(N'[social].[TikTokOAuthSessions]', N'U') IS NULL OR
       OBJECT_ID(N'[social].[TikTokPublishJobs]', N'U') IS NULL OR
       OBJECT_ID(N'[auth].[AccountAuditLogs]', N'U') IS NULL OR
       OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
        THROW 51170, 'VideoFactory TikTok 4.1.6, account audit and schema version tables are required.', 1;

    IF OBJECT_ID(N'[social].[TikTokAppCredentials]', N'U') IS NULL
    BEGIN
        CREATE TABLE [social].[TikTokAppCredentials]
        (
            [TikTokAppCredentialId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_TikTokAppCredentials_Id] DEFAULT (NEWSEQUENTIALID()),
            [Version] int NOT NULL,
            [ProtectedPayload] nvarchar(max) NOT NULL,
            [ClientKeyHint] varchar(32) NOT NULL,
            [SecretHint] varchar(32) NOT NULL,
            [Status] varchar(20) NOT NULL,
            [CreatedByUserId] nvarchar(450) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            [VerificationRequestedByUserId] nvarchar(450) NULL,
            [VerificationExpiresAtUtc] datetime2(3) NULL,
            [LastTestedAtUtc] datetime2(3) NULL,
            [LastTestFailureCode] varchar(100) NULL,
            [ActivatedAtUtc] datetime2(3) NULL,
            [RetiredAtUtc] datetime2(3) NULL,
            [RevokedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_TikTokAppCredentials] PRIMARY KEY ([TikTokAppCredentialId]),
            CONSTRAINT [FK_TikTokAppCredentials_CreatedByUser] FOREIGN KEY ([CreatedByUserId])
                REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_TikTokAppCredentials_VerificationUser] FOREIGN KEY ([VerificationRequestedByUserId])
                REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_TikTokAppCredentials_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_TikTokAppCredentials_Status]
                CHECK ([Status] IN ('Pending', 'Active', 'Retiring', 'Revoked')),
            CONSTRAINT [CK_TikTokAppCredentials_Verification]
                CHECK
                (
                    ([VerificationRequestedByUserId] IS NULL AND [VerificationExpiresAtUtc] IS NULL) OR
                    ([VerificationRequestedByUserId] IS NOT NULL AND [VerificationExpiresAtUtc] IS NOT NULL)
                )
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[social].[TikTokAppCredentials]')
          AND [name] = N'UX_TikTokAppCredentials_Version'
    )
        CREATE UNIQUE INDEX [UX_TikTokAppCredentials_Version]
            ON [social].[TikTokAppCredentials]([Version]);

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[social].[TikTokAppCredentials]')
          AND [name] = N'UX_TikTokAppCredentials_Active'
    )
        CREATE UNIQUE INDEX [UX_TikTokAppCredentials_Active]
            ON [social].[TikTokAppCredentials]([Status])
            WHERE [Status] = 'Active';

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[social].[TikTokAppCredentials]')
          AND [name] = N'UX_TikTokAppCredentials_Pending'
    )
        CREATE UNIQUE INDEX [UX_TikTokAppCredentials_Pending]
            ON [social].[TikTokAppCredentials]([Status])
            WHERE [Status] = 'Pending';

    IF OBJECT_ID(N'[social].[TikTokIntegrationSettings]', N'U') IS NULL
    BEGIN
        CREATE TABLE [social].[TikTokIntegrationSettings]
        (
            [TikTokIntegrationSettingId] tinyint NOT NULL,
            [Enabled] bit NOT NULL CONSTRAINT [DF_TikTokIntegrationSettings_Enabled] DEFAULT (0),
            [AuditedForPublicPosting] bit NOT NULL
                CONSTRAINT [DF_TikTokIntegrationSettings_Audited] DEFAULT (0),
            [AuditEvidence] nvarchar(500) NULL,
            [UpdatedByUserId] nvarchar(450) NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_TikTokIntegrationSettings] PRIMARY KEY ([TikTokIntegrationSettingId]),
            CONSTRAINT [FK_TikTokIntegrationSettings_UpdatedByUser] FOREIGN KEY ([UpdatedByUserId])
                REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_TikTokIntegrationSettings_Singleton]
                CHECK ([TikTokIntegrationSettingId] = 1),
            CONSTRAINT [CK_TikTokIntegrationSettings_AuditEvidence]
                CHECK ([AuditedForPublicPosting] = 0 OR LEN([AuditEvidence]) >= 8)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [social].[TikTokIntegrationSettings]
        WHERE [TikTokIntegrationSettingId] = 1
    )
        INSERT INTO [social].[TikTokIntegrationSettings]
        (
            [TikTokIntegrationSettingId], [Enabled], [AuditedForPublicPosting],
            [AuditEvidence], [UpdatedByUserId], [UpdatedAtUtc]
        )
        VALUES (1, 0, 0, NULL, NULL, SYSUTCDATETIME());

    IF COL_LENGTH(N'social.TikTokOAuthSessions', N'TikTokAppCredentialId') IS NULL
        ALTER TABLE [social].[TikTokOAuthSessions]
            ADD [TikTokAppCredentialId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[social].[TikTokOAuthSessions]')
          AND [name] = N'FK_TikTokOAuthSessions_AppCredentials'
    )
        ALTER TABLE [social].[TikTokOAuthSessions] WITH CHECK
            ADD CONSTRAINT [FK_TikTokOAuthSessions_AppCredentials]
            FOREIGN KEY ([TikTokAppCredentialId])
            REFERENCES [social].[TikTokAppCredentials]([TikTokAppCredentialId]);

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[social].[TikTokOAuthSessions]')
          AND [name] = N'IX_TikTokOAuthSessions_AppCredential'
    )
        CREATE INDEX [IX_TikTokOAuthSessions_AppCredential]
            ON [social].[TikTokOAuthSessions]([TikTokAppCredentialId]);

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.7-tiktok-admin-credentials'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.7-tiktok-admin-credentials',
            N'Encrypted Global Admin TikTok app credential with OAuth verification and audited rollout settings.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'[social].[TikTokAppCredentials]', N'U') IS NULL OR
   OBJECT_ID(N'[social].[TikTokIntegrationSettings]', N'U') IS NULL OR
   COL_LENGTH(N'social.TikTokOAuthSessions', N'TikTokAppCredentialId') IS NULL OR
   NOT EXISTS
   (
       SELECT 1 FROM [ai].[SchemaVersions]
       WHERE [Version] = '4.1.7-tiktok-admin-credentials'
   )
    THROW 51171, 'TikTok Admin credential schema verification failed.', 1;

PRINT N'VideoFactory TikTok Admin credential schema 4.1.7 is ready.';
GO
