/*
    Scene voice/TTS foundation for VideoMaker 4.0.

    This migration is intentionally idempotent. Run only after a verified backup
    and after VideoFactory.4.0.0.OrganizationAiGateway.sql.
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
       OBJECT_ID(N'[vf].[VoiceGenerations]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL
    BEGIN
        THROW 51030, 'VideoFactory workflow tables are required before scene voice/TTS migration.', 1;
    END;

    IF COL_LENGTH(N'vf.Projects', N'VoiceCode') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VoiceCode] nvarchar(100) NULL;

    IF COL_LENGTH(N'vf.Projects', N'VoiceSpeakingRate') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VoiceSpeakingRate] decimal(6,3) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'CK_Projects_VoiceSpeakingRate'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [CK_Projects_VoiceSpeakingRate]
               CHECK ([VoiceSpeakingRate] IS NULL OR [VoiceSpeakingRate] BETWEEN 0.5 AND 2.0);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'CK_Projects_VoiceSettingsPair'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [CK_Projects_VoiceSettingsPair]
               CHECK (([VoiceCode] IS NULL AND [VoiceSpeakingRate] IS NULL) OR
                      ([VoiceCode] IS NOT NULL AND [VoiceSpeakingRate] IS NOT NULL));');

    IF COL_LENGTH(N'vf.VoiceGenerations', N'SceneId') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [SceneId] uniqueidentifier NULL;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'ScenePlanVersion') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [ScenePlanVersion] int NULL;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'NarrationHash') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [NarrationHash] char(64) NULL;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'ProviderVoiceCode') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [ProviderVoiceCode] nvarchar(100) NULL;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'VoiceSnapshotJson') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [VoiceSnapshotJson] nvarchar(max) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'FK_VoiceGenerations_Scenes'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [FK_VoiceGenerations_Scenes]
               FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes] ([SceneId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'CK_VoiceGenerations_ScenePlanVersion'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [CK_VoiceGenerations_ScenePlanVersion]
               CHECK ([ScenePlanVersion] IS NULL OR [ScenePlanVersion] > 0);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'CK_VoiceGenerations_NarrationHash'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [CK_VoiceGenerations_NarrationHash]
               CHECK ([NarrationHash] IS NULL OR
                     ([NarrationHash] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([NarrationHash]) = 64));');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'CK_VoiceGenerations_VoiceSnapshotJson'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [CK_VoiceGenerations_VoiceSnapshotJson]
               CHECK ([VoiceSnapshotJson] IS NULL OR ISJSON([VoiceSnapshotJson]) = 1);');

    IF EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'UQ_VoiceGenerations_Project_Version'
    )
        ALTER TABLE [vf].[VoiceGenerations]
            DROP CONSTRAINT [UQ_VoiceGenerations_Project_Version];

    IF EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'UQ_VoiceGenerations_Project_Version'
          AND [is_unique_constraint] = 0
    )
        DROP INDEX [UQ_VoiceGenerations_Project_Version] ON [vf].[VoiceGenerations];

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'UX_VoiceGenerations_Scene_Version'
    )
        EXEC(N'CREATE UNIQUE INDEX [UX_VoiceGenerations_Scene_Version]
               ON [vf].[VoiceGenerations] ([SceneId], [Version])
               WHERE [SceneId] IS NOT NULL;');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'IX_VoiceGenerations_Scene_NarrationHash'
    )
        EXEC(N'CREATE INDEX [IX_VoiceGenerations_Scene_NarrationHash]
               ON [vf].[VoiceGenerations] ([SceneId], [NarrationHash], [CreatedAtUtc] DESC)
               INCLUDE ([Status], [ProviderRequestId], [VoiceCode], [SpeakingRate])
               WHERE [SceneId] IS NOT NULL AND [NarrationHash] IS NOT NULL;');

    IF OBJECT_ID(N'[vf].[GeneratedVoiceOutputs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[GeneratedVoiceOutputs]
        (
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [Payload] varbinary(max) NOT NULL,
            [MimeType] varchar(150) NOT NULL,
            [Sha256] char(64) NOT NULL,
            [SizeBytes] bigint NOT NULL,
            [DurationMs] bigint NOT NULL,
            [SampleRate] int NOT NULL,
            [Channels] tinyint NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_GeneratedVoiceOutputs_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [DownloadedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_GeneratedVoiceOutputs] PRIMARY KEY CLUSTERED ([ProviderRequestId]),
            CONSTRAINT [FK_GeneratedVoiceOutputs_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]),
            CONSTRAINT [CK_GeneratedVoiceOutputs_MimeType]
                CHECK ([MimeType] IN ('audio/wav','audio/x-wav')),
            CONSTRAINT [CK_GeneratedVoiceOutputs_Sha256]
                CHECK ([Sha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([Sha256]) = 64),
            CONSTRAINT [CK_GeneratedVoiceOutputs_Size]
                CHECK ([SizeBytes] > 0 AND [SizeBytes] <= 52428800 AND DATALENGTH([Payload]) = [SizeBytes]),
            CONSTRAINT [CK_GeneratedVoiceOutputs_Duration]
                CHECK ([DurationMs] > 0),
            CONSTRAINT [CK_GeneratedVoiceOutputs_SampleRate]
                CHECK ([SampleRate] BETWEEN 8000 AND 192000),
            CONSTRAINT [CK_GeneratedVoiceOutputs_Channels]
                CHECK ([Channels] BETWEEN 1 AND 2),
            CONSTRAINT [CK_GeneratedVoiceOutputs_Expiry]
                CHECK ([ExpiresAtUtc] > [CreatedAtUtc])
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[GeneratedVoiceOutputs]')
          AND [name] = N'IX_GeneratedVoiceOutputs_ExpiresAtUtc'
    )
        CREATE INDEX [IX_GeneratedVoiceOutputs_ExpiresAtUtc]
            ON [vf].[GeneratedVoiceOutputs] ([ExpiresAtUtc]);

    IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51031, 'Organization AI Gateway migration 4.0.0 must run first.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.3-scene-voice-tts'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.3-scene-voice-tts',
            N'Scene voice settings, versioned voice generation snapshots and protected temporary WAV payload.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
BEGIN
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedVoiceOutputs] TO [VideoMakerDesktopRole];
END;
GO

IF COL_LENGTH(N'vf.Projects', N'VoiceCode') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'VoiceSpeakingRate') IS NULL OR
   COL_LENGTH(N'vf.VoiceGenerations', N'SceneId') IS NULL OR
   COL_LENGTH(N'vf.VoiceGenerations', N'NarrationHash') IS NULL OR
   OBJECT_ID(N'[vf].[GeneratedVoiceOutputs]', N'U') IS NULL
BEGIN
    THROW 51032, 'Scene voice/TTS schema verification failed.', 1;
END;

IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51033, 'Scene voice/TTS schema version table verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.0.3-scene-voice-tts'
)
BEGIN
    THROW 51034, 'Scene voice/TTS schema version was not recorded.', 1;
END;

PRINT N'VideoFactory scene voice/TTS schema 4.0.3 is ready.';
GO
