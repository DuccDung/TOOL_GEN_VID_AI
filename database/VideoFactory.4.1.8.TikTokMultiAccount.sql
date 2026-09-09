/* Multi-account TikTok. Requires 4.1.7 and a verified backup.
   Deploy the compatible server before enabling TikTok:MultiAccountEnabled.
   Legacy ciphertext and connection/job IDs are intentionally preserved.
   App identity is bound on verified OAuth/refresh, never guessed by this SQL.
   Do not run the old single-account server after creating multiple connections. */
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
    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.7-tiktok-admin-credentials')
        THROW 51180, 'TikTok schema 4.1.7 is required.', 1;

    IF COL_LENGTH(N'social.TikTokConnections', N'AppKeyHash') IS NULL
        ALTER TABLE [social].[TikTokConnections] ADD [AppKeyHash] varchar(64) NULL;
    IF COL_LENGTH(N'social.TikTokConnections', N'TikTokAppCredentialId') IS NULL
        ALTER TABLE [social].[TikTokConnections] ADD [TikTokAppCredentialId] uniqueidentifier NULL;
    IF COL_LENGTH(N'social.TikTokConnections', N'DisconnectedAtUtc') IS NULL
        ALTER TABLE [social].[TikTokConnections] ADD [DisconnectedAtUtc] datetime2(3) NULL;
    IF COL_LENGTH(N'social.TikTokConnections', N'ProtectedAvatarUrl') IS NULL
        ALTER TABLE [social].[TikTokConnections] ADD [ProtectedAvatarUrl] nvarchar(max) NULL;
    IF COL_LENGTH(N'social.TikTokConnections', N'AvatarExpiresAtUtc') IS NULL
        ALTER TABLE [social].[TikTokConnections] ADD [AvatarExpiresAtUtc] datetime2(3) NULL;
    IF COL_LENGTH(N'social.TikTokOAuthSessions', N'TargetConnectionId') IS NULL
        ALTER TABLE [social].[TikTokOAuthSessions] ADD [TargetConnectionId] uniqueidentifier NULL;
    IF COL_LENGTH(N'social.TikTokOAuthSessions', N'MultiAccount') IS NULL
        ALTER TABLE [social].[TikTokOAuthSessions] ADD [MultiAccount] bit NOT NULL
            CONSTRAINT [DF_TikTokOAuthSessions_MultiAccount] DEFAULT (0);
    IF COL_LENGTH(N'social.TikTokPublishJobs', N'CreatorUsernameSnapshot') IS NULL
        ALTER TABLE [social].[TikTokPublishJobs] ADD [CreatorUsernameSnapshot] nvarchar(150) NULL;
    IF COL_LENGTH(N'social.TikTokPublishJobs', N'CreatorNicknameSnapshot') IS NULL
        ALTER TABLE [social].[TikTokPublishJobs] ADD [CreatorNicknameSnapshot] nvarchar(200) NULL;
    IF COL_LENGTH(N'social.TikTokPublishJobs', N'NextPollAtUtc') IS NULL
        ALTER TABLE [social].[TikTokPublishJobs] ADD [NextPollAtUtc] datetime2(3) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TikTokConnections_AppCredential')
        EXEC(N'ALTER TABLE [social].[TikTokConnections] ADD CONSTRAINT [FK_TikTokConnections_AppCredential]
            FOREIGN KEY ([TikTokAppCredentialId]) REFERENCES [social].[TikTokAppCredentials]([TikTokAppCredentialId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_TikTokOAuthSessions_TargetConnection')
        EXEC(N'ALTER TABLE [social].[TikTokOAuthSessions] ADD CONSTRAINT [FK_TikTokOAuthSessions_TargetConnection]
            FOREIGN KEY ([TargetConnectionId]) REFERENCES [social].[TikTokConnections]([TikTokConnectionId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'social.TikTokConnections') AND name = N'UX_TikTokConnections_UserAppOpenId')
        EXEC(N'CREATE UNIQUE INDEX [UX_TikTokConnections_UserAppOpenId]
            ON [social].[TikTokConnections]([UserId], [AppKeyHash], [OpenId]);');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'social.TikTokConnections') AND name = N'IX_TikTokConnections_UserState')
        CREATE INDEX [IX_TikTokConnections_UserState] ON [social].[TikTokConnections]([UserId], [RevokedAtUtc]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'social.TikTokPublishJobs') AND name = N'IX_TikTokPublishJobs_AccountHistory')
        CREATE INDEX [IX_TikTokPublishJobs_AccountHistory]
            ON [social].[TikTokPublishJobs]([UserId], [TikTokConnectionId], [CreatedAtUtc] DESC);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'social.TikTokPublishJobs') AND name = N'IX_TikTokPublishJobs_Due')
        EXEC(N'CREATE INDEX [IX_TikTokPublishJobs_Due] ON [social].[TikTokPublishJobs]([NextPollAtUtc], [Status]);');

    IF OBJECT_ID(N'social.TikTokPublishAttempts', N'U') IS NULL
    BEGIN
        CREATE TABLE [social].[TikTokPublishAttempts]
        (
            [TikTokPublishAttemptId] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [ClientRequestId] uniqueidentifier NOT NULL,
            [TikTokConnectionId] uniqueidentifier NOT NULL,
            [RequestHash] varchar(64) NOT NULL,
            [Status] varchar(20) NOT NULL,
            [TikTokPublishJobId] uniqueidentifier NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_TikTokPublishAttempts] PRIMARY KEY ([TikTokPublishAttemptId]),
            CONSTRAINT [FK_TikTokPublishAttempts_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_TikTokPublishAttempts_Connection] FOREIGN KEY ([TikTokConnectionId]) REFERENCES [social].[TikTokConnections]([TikTokConnectionId]),
            CONSTRAINT [FK_TikTokPublishAttempts_Job] FOREIGN KEY ([TikTokPublishJobId]) REFERENCES [social].[TikTokPublishJobs]([TikTokPublishJobId]),
            CONSTRAINT [CK_TikTokPublishAttempts_Status] CHECK ([Status] IN ('Initializing', 'Initialized', 'Unknown', 'Rejected')),
            CONSTRAINT [CK_TikTokPublishAttempts_Hash] CHECK (LEN([RequestHash]) = 64)
        );
        CREATE UNIQUE INDEX [UX_TikTokPublishAttempts_UserRequest]
            ON [social].[TikTokPublishAttempts]([UserId], [ClientRequestId]);
    END;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'social.TikTokConnections') AND name = N'UX_TikTokConnections_UserId')
        DROP INDEX [UX_TikTokConnections_UserId] ON [social].[TikTokConnections];
    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.8-tiktok-multi-account')
        INSERT INTO [ai].[SchemaVersions]([Version], [Description])
        VALUES ('4.1.8-tiktok-multi-account', N'TikTok account identity, account-bound OAuth, durable publish attempts and history.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
