/*
    Canonical Voice lip-sync workflow for long-form VideoMaker projects.

    This migration is idempotent. Run only after a verified backup and after
    VideoFactory.4.1.5.SpeechVerificationReview.sql. It does not enable a
    provider/model, seed a price, upload media, or call an AI provider.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[Projects]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[Scenes]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[VideoGenerations]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[VoiceGenerations]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderModels]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[MediaAssets]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[Organizations]', N'U') IS NULL OR
       OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL
        THROW 51160, 'VideoFactory schema 4.1.5 is required before lip-sync migration.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.5-speech-verification-review'
    )
        THROW 51162, 'Apply VideoFactory.4.1.5.SpeechVerificationReview.sql before lip-sync migration.', 1;

    IF COL_LENGTH(N'vf.Projects', N'LipSyncProviderCode') IS NULL
        ALTER TABLE [vf].[Projects] ADD [LipSyncProviderCode] varchar(80) NULL;
    IF COL_LENGTH(N'vf.Projects', N'LipSyncModelCode') IS NULL
        ALTER TABLE [vf].[Projects] ADD [LipSyncModelCode] nvarchar(200) NULL;
    IF COL_LENGTH(N'vf.Projects', N'LipSyncPolicyVersion') IS NULL
        ALTER TABLE [vf].[Projects] ADD [LipSyncPolicyVersion] varchar(40) NULL;
    IF COL_LENGTH(N'vf.Projects', N'LipSyncSnapshotAtUtc') IS NULL
        ALTER TABLE [vf].[Projects] ADD [LipSyncSnapshotAtUtc] datetime2(3) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'CK_Projects_LipSyncSnapshot'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [CK_Projects_LipSyncSnapshot] CHECK
               (([LipSyncProviderCode] IS NULL AND [LipSyncModelCode] IS NULL AND [LipSyncPolicyVersion] IS NULL AND [LipSyncSnapshotAtUtc] IS NULL) OR
                ([LipSyncProviderCode] IS NOT NULL AND [LipSyncModelCode] IS NOT NULL AND [LipSyncPolicyVersion] IS NOT NULL AND [LipSyncSnapshotAtUtc] IS NOT NULL));');

    IF OBJECT_ID(N'[vf].[LipSyncInputSessions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[LipSyncInputSessions]
        (
            [LipSyncInputSessionId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_LipSyncInputSessions_Id] DEFAULT NEWSEQUENTIALID(),
            [OrganizationId] uniqueidentifier NOT NULL,
            [RequestedByUserId] nvarchar(450) NOT NULL,
            [ProjectId] uniqueidentifier NOT NULL,
            [SceneId] uniqueidentifier NOT NULL,
            [ScenePlanVersion] int NOT NULL,
            [VideoGenerationId] uniqueidentifier NOT NULL,
            [VoiceGenerationId] uniqueidentifier NOT NULL,
            [VideoSha256] char(64) NOT NULL,
            [AudioSha256] char(64) NOT NULL,
            [PreparedVideoSha256] char(64) NOT NULL,
            [PreparedAudioSha256] char(64) NOT NULL,
            [DurationMs] bigint NOT NULL,
            [VideoStorageKey] nvarchar(300) NULL,
            [AudioStorageKey] nvarchar(300) NULL,
            [VideoSizeBytes] bigint NULL,
            [AudioSizeBytes] bigint NULL,
            [Status] varchar(20) NOT NULL CONSTRAINT [DF_LipSyncInputSessions_Status] DEFAULT ('Uploading'),
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_LipSyncInputSessions_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [SubmittedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_LipSyncInputSessions] PRIMARY KEY CLUSTERED ([LipSyncInputSessionId]),
            CONSTRAINT [FK_LipSyncInputSessions_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations] ([OrganizationId]),
            CONSTRAINT [FK_LipSyncInputSessions_Users] FOREIGN KEY ([RequestedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]),
            CONSTRAINT [FK_LipSyncInputSessions_Projects] FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects] ([ProjectId]),
            CONSTRAINT [FK_LipSyncInputSessions_Scenes] FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes] ([SceneId]),
            CONSTRAINT [FK_LipSyncInputSessions_VideoGenerations] FOREIGN KEY ([VideoGenerationId]) REFERENCES [vf].[VideoGenerations] ([VideoGenerationId]),
            CONSTRAINT [FK_LipSyncInputSessions_VoiceGenerations] FOREIGN KEY ([VoiceGenerationId]) REFERENCES [vf].[VoiceGenerations] ([VoiceGenerationId]),
            CONSTRAINT [CK_LipSyncInputSessions_Hashes] CHECK
                ([VideoSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([VideoSha256]) = 64 AND
                 [AudioSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([AudioSha256]) = 64 AND
                 [PreparedVideoSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([PreparedVideoSha256]) = 64 AND
                 [PreparedAudioSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([PreparedAudioSha256]) = 64),
            CONSTRAINT [CK_LipSyncInputSessions_Duration] CHECK ([DurationMs] BETWEEN 1000 AND 120000),
            CONSTRAINT [CK_LipSyncInputSessions_Sizes] CHECK
                (([VideoSizeBytes] IS NULL OR [VideoSizeBytes] > 0) AND
                 ([AudioSizeBytes] IS NULL OR [AudioSizeBytes] > 0)),
            CONSTRAINT [CK_LipSyncInputSessions_Status] CHECK
                ([Status] IN ('Uploading','Ready','Submitted','Expired','Failed')),
            CONSTRAINT [CK_LipSyncInputSessions_Lifecycle] CHECK
                ((([VideoStorageKey] IS NULL AND [VideoSizeBytes] IS NULL) OR
                  ([VideoStorageKey] IS NOT NULL AND [VideoSizeBytes] IS NOT NULL)) AND
                 (([AudioStorageKey] IS NULL AND [AudioSizeBytes] IS NULL) OR
                  ([AudioStorageKey] IS NOT NULL AND [AudioSizeBytes] IS NOT NULL)) AND
                 ([Status] NOT IN ('Ready','Submitted') OR
                  ([VideoStorageKey] IS NOT NULL AND [AudioStorageKey] IS NOT NULL))),
            CONSTRAINT [CK_LipSyncInputSessions_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc])
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[vf].[LipSyncInputSessions]') AND [name] = N'IX_LipSyncInputSessions_Scene_Created')
        CREATE INDEX [IX_LipSyncInputSessions_Scene_Created]
            ON [vf].[LipSyncInputSessions] ([ProjectId], [SceneId], [CreatedAtUtc] DESC);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[vf].[LipSyncInputSessions]') AND [name] = N'IX_LipSyncInputSessions_Status_Expiry')
        CREATE INDEX [IX_LipSyncInputSessions_Status_Expiry]
            ON [vf].[LipSyncInputSessions] ([Status], [ExpiresAtUtc]);

    IF OBJECT_ID(N'[vf].[LipSyncGenerations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[LipSyncGenerations]
        (
            [LipSyncGenerationId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_LipSyncGenerations_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId] uniqueidentifier NOT NULL,
            [SceneId] uniqueidentifier NOT NULL,
            [LipSyncInputSessionId] uniqueidentifier NOT NULL,
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [VideoGenerationId] uniqueidentifier NOT NULL,
            [VoiceGenerationId] uniqueidentifier NOT NULL,
            [AttemptNumber] int NOT NULL,
            [Status] varchar(20) NOT NULL,
            [PolicyVersion] varchar(40) NOT NULL,
            [RequestedDurationMs] bigint NOT NULL,
            [ActualDurationMs] bigint NULL,
            [VideoSha256] char(64) NOT NULL,
            [AudioSha256] char(64) NOT NULL,
            [PreparedVideoSha256] char(64) NOT NULL,
            [PreparedAudioSha256] char(64) NOT NULL,
            [OutputMediaAssetId] uniqueidentifier NULL,
            [OutputSha256] char(64) NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL CONSTRAINT [DF_LipSyncGenerations_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [CompletedAtUtc] datetime2(3) NULL,
            [ApprovedAtUtc] datetime2(3) NULL,
            [ApprovedByUserId] nvarchar(450) NULL,
            [ReviewReason] nvarchar(1000) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_LipSyncGenerations] PRIMARY KEY CLUSTERED ([LipSyncGenerationId]),
            CONSTRAINT [FK_LipSyncGenerations_Projects] FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects] ([ProjectId]),
            CONSTRAINT [FK_LipSyncGenerations_Scenes] FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes] ([SceneId]),
            CONSTRAINT [FK_LipSyncGenerations_InputSessions] FOREIGN KEY ([LipSyncInputSessionId]) REFERENCES [vf].[LipSyncInputSessions] ([LipSyncInputSessionId]),
            CONSTRAINT [FK_LipSyncGenerations_ProviderRequests] FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]),
            CONSTRAINT [FK_LipSyncGenerations_VideoGenerations] FOREIGN KEY ([VideoGenerationId]) REFERENCES [vf].[VideoGenerations] ([VideoGenerationId]),
            CONSTRAINT [FK_LipSyncGenerations_VoiceGenerations] FOREIGN KEY ([VoiceGenerationId]) REFERENCES [vf].[VoiceGenerations] ([VoiceGenerationId]),
            CONSTRAINT [FK_LipSyncGenerations_OutputMediaAsset] FOREIGN KEY ([OutputMediaAssetId]) REFERENCES [vf].[MediaAssets] ([MediaAssetId]),
            CONSTRAINT [FK_LipSyncGenerations_ApprovedUsers] FOREIGN KEY ([ApprovedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]),
            CONSTRAINT [UQ_LipSyncGenerations_ProviderRequest] UNIQUE ([ProviderRequestId]),
            CONSTRAINT [UQ_LipSyncGenerations_Scene_Attempt] UNIQUE ([SceneId], [AttemptNumber]),
            CONSTRAINT [CK_LipSyncGenerations_Attempt] CHECK ([AttemptNumber] > 0),
            CONSTRAINT [CK_LipSyncGenerations_Duration] CHECK
                ([RequestedDurationMs] BETWEEN 1000 AND 120000 AND ([ActualDurationMs] IS NULL OR [ActualDurationMs] > 0)),
            CONSTRAINT [CK_LipSyncGenerations_Hashes] CHECK
                ([VideoSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([VideoSha256]) = 64 AND
                 [AudioSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([AudioSha256]) = 64 AND
                 [PreparedVideoSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([PreparedVideoSha256]) = 64 AND
                 [PreparedAudioSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([PreparedAudioSha256]) = 64 AND
                 ([OutputSha256] IS NULL OR ([OutputSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([OutputSha256]) = 64))),
            CONSTRAINT [CK_LipSyncGenerations_Status] CHECK
                ([Status] IN ('Submitting','Submitted','Queued','Processing','Unknown','Completed','Downloading','ReviewRequired','Approved','Rejected','Failed','Cancelled','Expired')),
            CONSTRAINT [CK_LipSyncGenerations_Lifecycle] CHECK
                ((([OutputMediaAssetId] IS NULL AND [OutputSha256] IS NULL) OR
                  ([OutputMediaAssetId] IS NOT NULL AND [OutputSha256] IS NOT NULL)) AND
                 ([Status] NOT IN ('ReviewRequired','Approved','Rejected') OR [OutputMediaAssetId] IS NOT NULL) AND
                 (([Status] = 'Approved' AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL) OR
                  ([Status] <> 'Approved' AND [ApprovedAtUtc] IS NULL AND [ApprovedByUserId] IS NULL)) AND
                 ([Status] <> 'Rejected' OR [ReviewReason] IS NOT NULL))
        );
    END;

    IF COL_LENGTH(N'vf.Scenes', N'ApprovedLipSyncGenerationId') IS NULL
        ALTER TABLE [vf].[Scenes] ADD [ApprovedLipSyncGenerationId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
          AND [name] = N'FK_Scenes_ApprovedLipSyncGeneration'
    )
        EXEC(N'ALTER TABLE [vf].[Scenes] WITH CHECK
               ADD CONSTRAINT [FK_Scenes_ApprovedLipSyncGeneration]
               FOREIGN KEY ([ApprovedLipSyncGenerationId])
               REFERENCES [vf].[LipSyncGenerations] ([LipSyncGenerationId]);');

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderModels]') AND [name] = N'CK_ProviderModels_Modality')
        ALTER TABLE [vf].[ProviderModels] DROP CONSTRAINT [CK_ProviderModels_Modality];
    ALTER TABLE [vf].[ProviderModels] WITH CHECK ADD CONSTRAINT [CK_ProviderModels_Modality]
        CHECK ([Modality] IN ('Text','TextRepair','Image','Video','Voice','VoicePreview','Transcription','LipSync','Search','Music','SoundEffect'));

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]') AND [name] = N'CK_ProviderRequests_Kind')
        ALTER TABLE [vf].[ProviderRequests] DROP CONSTRAINT [CK_ProviderRequests_Kind];
    ALTER TABLE [vf].[ProviderRequests] WITH CHECK ADD CONSTRAINT [CK_ProviderRequests_Kind]
        CHECK ([RequestKind] IN ('Text','TextRepair','Image','Video','Voice','VoicePreview','Transcription','LipSync','Search','Music','SoundEffect'));

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]') AND [name] = N'CK_ProviderRequests_Status')
        ALTER TABLE [vf].[ProviderRequests] DROP CONSTRAINT [CK_ProviderRequests_Status];
    ALTER TABLE [vf].[ProviderRequests] WITH CHECK ADD CONSTRAINT [CK_ProviderRequests_Status]
        CHECK ([Status] IN ('Created','Submitting','Submitted','Queued','Processing','Completed','Failed','Cancelled','Unknown','Expired'));

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.6-lip-sync-generation')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES ('4.1.6-lip-sync-generation', N'Canonical Voice on-camera lip-sync input staging, provider generation lineage and audited approval.');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
BEGIN
    GRANT SELECT ON OBJECT::[vf].[LipSyncInputSessions] TO [VideoMakerDesktopRole];
    GRANT SELECT ON OBJECT::[vf].[LipSyncGenerations] TO [VideoMakerDesktopRole];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[LipSyncInputSessions] TO [VideoMakerDesktopRole];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[LipSyncGenerations] TO [VideoMakerDesktopRole];
    DENY UPDATE ON OBJECT::[vf].[Projects]
        ([LipSyncProviderCode], [LipSyncModelCode], [LipSyncPolicyVersion], [LipSyncSnapshotAtUtc])
        TO [VideoMakerDesktopRole];
    DENY UPDATE ON OBJECT::[vf].[Scenes] ([ApprovedLipSyncGenerationId]) TO [VideoMakerDesktopRole];
END;
GO

IF OBJECT_ID(N'[vf].[LipSyncInputSessions]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[LipSyncGenerations]', N'U') IS NULL OR
   COL_LENGTH(N'vf.Scenes', N'ApprovedLipSyncGenerationId') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'LipSyncProviderCode') IS NULL OR
   NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.6-lip-sync-generation')
    THROW 51161, 'Lip-sync schema verification failed.', 1;

PRINT N'VideoFactory lip-sync generation schema 4.1.6 is ready.';
GO
