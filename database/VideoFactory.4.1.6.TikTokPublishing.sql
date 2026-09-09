/*
    Per-user TikTok OAuth connections and Direct Post job tracking.

    This migration is idempotent. Run only after a verified backup and after
    VideoFactory.4.1.5.SpeechVerificationReview.sql. It does not contact TikTok.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL OR
       OBJECT_ID(N'[auth].[RegisteredDevices]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
        THROW 51160, 'VideoFactory account and schema version tables are required before TikTok migration.', 1;

    IF SCHEMA_ID(N'social') IS NULL
        EXEC(N'CREATE SCHEMA [social] AUTHORIZATION [dbo];');

    IF OBJECT_ID(N'[social].[TikTokConnections]', N'U') IS NULL
    BEGIN
        CREATE TABLE [social].[TikTokConnections]
        (
            [TikTokConnectionId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_TikTokConnections_Id] DEFAULT (NEWSEQUENTIALID()),
            [UserId] nvarchar(450) NOT NULL,
            [OpenId] varchar(128) NOT NULL,
            [CreatorUsername] nvarchar(150) NOT NULL CONSTRAINT [DF_TikTokConnections_Username] DEFAULT (N''),
            [CreatorNickname] nvarchar(200) NOT NULL CONSTRAINT [DF_TikTokConnections_Nickname] DEFAULT (N''),
            [Scopes] varchar(1000) NOT NULL,
            [ProtectedAccessToken] nvarchar(max) NOT NULL,
            [ProtectedRefreshToken] nvarchar(max) NOT NULL,
            [AccessTokenExpiresAtUtc] datetime2(3) NOT NULL,
            [RefreshTokenExpiresAtUtc] datetime2(3) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            [RevokedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_TikTokConnections] PRIMARY KEY ([TikTokConnectionId]),
            CONSTRAINT [FK_TikTokConnections_Users] FOREIGN KEY ([UserId])
                REFERENCES [dbo].[AspNetUsers]([Id])
        );
        CREATE UNIQUE INDEX [UX_TikTokConnections_UserId]
            ON [social].[TikTokConnections]([UserId]);
    END;

    IF OBJECT_ID(N'[social].[TikTokOAuthSessions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [social].[TikTokOAuthSessions]
        (
            [TikTokOAuthSessionId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_TikTokOAuthSessions_Id] DEFAULT (NEWSEQUENTIALID()),
            [UserId] nvarchar(450) NOT NULL,
            [DeviceId] uniqueidentifier NOT NULL,
            [StateHash] binary(32) NOT NULL,
            [CodeChallenge] varchar(64) NOT NULL,
            [RedirectUri] varchar(512) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [ConsumedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_TikTokOAuthSessions] PRIMARY KEY ([TikTokOAuthSessionId]),
            CONSTRAINT [FK_TikTokOAuthSessions_Users] FOREIGN KEY ([UserId])
                REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_TikTokOAuthSessions_Devices] FOREIGN KEY ([DeviceId])
                REFERENCES [auth].[RegisteredDevices]([DeviceId]),
            CONSTRAINT [CK_TikTokOAuthSessions_Expiry]
                CHECK ([ExpiresAtUtc] > [CreatedAtUtc])
        );
        CREATE INDEX [IX_TikTokOAuthSessions_UserDeviceExpiry]
            ON [social].[TikTokOAuthSessions]([UserId], [DeviceId], [ExpiresAtUtc]);
    END;

    IF OBJECT_ID(N'[social].[TikTokPublishJobs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [social].[TikTokPublishJobs]
        (
            [TikTokPublishJobId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_TikTokPublishJobs_Id] DEFAULT (NEWSEQUENTIALID()),
            [TikTokConnectionId] uniqueidentifier NOT NULL,
            [UserId] nvarchar(450) NOT NULL,
            [ClientRequestId] uniqueidentifier NOT NULL,
            [TikTokPublishId] varchar(64) NOT NULL,
            [ProtectedUploadUrl] nvarchar(max) NULL,
            [UploadUrlExpiresAtUtc] datetime2(3) NOT NULL,
            [VideoSizeBytes] bigint NOT NULL,
            [ChunkSizeBytes] bigint NOT NULL,
            [TotalChunkCount] int NOT NULL,
            [Status] varchar(40) NOT NULL,
            [FailureReason] varchar(200) NULL,
            [UploadedBytes] bigint NOT NULL CONSTRAINT [DF_TikTokPublishJobs_UploadedBytes] DEFAULT (0),
            [PublicPostIds] varchar(1000) NOT NULL CONSTRAINT [DF_TikTokPublishJobs_PublicPostIds] DEFAULT (''),
            [CreatedAtUtc] datetime2(3) NOT NULL,
            [UpdatedAtUtc] datetime2(3) NOT NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_TikTokPublishJobs] PRIMARY KEY ([TikTokPublishJobId]),
            CONSTRAINT [FK_TikTokPublishJobs_Connections] FOREIGN KEY ([TikTokConnectionId])
                REFERENCES [social].[TikTokConnections]([TikTokConnectionId]),
            CONSTRAINT [FK_TikTokPublishJobs_Users] FOREIGN KEY ([UserId])
                REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_TikTokPublishJobs_VideoSize]
                CHECK ([VideoSizeBytes] > 0 AND [VideoSizeBytes] <= 4294967296),
            CONSTRAINT [CK_TikTokPublishJobs_Chunks]
                CHECK ([ChunkSizeBytes] > 0 AND [TotalChunkCount] BETWEEN 1 AND 1000),
            CONSTRAINT [CK_TikTokPublishJobs_UploadedBytes]
                CHECK ([UploadedBytes] >= 0 AND [UploadedBytes] <= [VideoSizeBytes])
        );
        CREATE UNIQUE INDEX [UX_TikTokPublishJobs_UserRequest]
            ON [social].[TikTokPublishJobs]([UserId], [ClientRequestId]);
        CREATE INDEX [IX_TikTokPublishJobs_StatusUpdated]
            ON [social].[TikTokPublishJobs]([Status], [UpdatedAtUtc]);
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.6-tiktok-publishing'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.6-tiktok-publishing',
            N'Encrypted per-user TikTok OAuth connections and Direct Post job tracking.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'[social].[TikTokConnections]', N'U') IS NULL OR
   OBJECT_ID(N'[social].[TikTokOAuthSessions]', N'U') IS NULL OR
   OBJECT_ID(N'[social].[TikTokPublishJobs]', N'U') IS NULL OR
   NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.6-tiktok-publishing')
    THROW 51161, 'TikTok publishing schema verification failed.', 1;

PRINT N'VideoFactory TikTok publishing schema 4.1.6 is ready.';
GO
