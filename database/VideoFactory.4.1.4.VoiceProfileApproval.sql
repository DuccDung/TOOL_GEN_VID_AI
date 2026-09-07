/*
    Explicit VoiceProfileVersion preview and approval lifecycle.

    This migration is idempotent. Run only after a verified backup and after
    VideoFactory.4.1.3.SpeechSynchronization.sql. It does not call an AI provider.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[VoiceProfileVersions]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
        THROW 51140, 'Speech synchronization schema 4.1.3 is required before voice profile approval migration.', 1;

    IF COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewProviderRequestId') IS NULL
        ALTER TABLE [vf].[VoiceProfileVersions] ADD [PreviewProviderRequestId] uniqueidentifier NULL;

    IF COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewSha256') IS NULL
        ALTER TABLE [vf].[VoiceProfileVersions] ADD [PreviewSha256] char(64) NULL;

    IF COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewDurationMs') IS NULL
        ALTER TABLE [vf].[VoiceProfileVersions] ADD [PreviewDurationMs] bigint NULL;

    IF COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewExpiresAtUtc') IS NULL
        ALTER TABLE [vf].[VoiceProfileVersions] ADD [PreviewExpiresAtUtc] datetime2(3) NULL;

    IF COL_LENGTH(N'vf.VoiceProfileVersions', N'ApprovedByUserId') IS NULL
        ALTER TABLE [vf].[VoiceProfileVersions] ADD [ApprovedByUserId] nvarchar(450) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceProfileVersions]')
          AND [name] = N'FK_VoiceProfileVersions_PreviewProviderRequest'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceProfileVersions] WITH CHECK
               ADD CONSTRAINT [FK_VoiceProfileVersions_PreviewProviderRequest]
               FOREIGN KEY ([PreviewProviderRequestId])
               REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceProfileVersions]')
          AND [name] = N'UX_VoiceProfileVersions_PreviewProviderRequest'
    )
        EXEC(N'CREATE UNIQUE INDEX [UX_VoiceProfileVersions_PreviewProviderRequest]
               ON [vf].[VoiceProfileVersions] ([PreviewProviderRequestId])
               WHERE [PreviewProviderRequestId] IS NOT NULL;');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceProfileVersions]')
          AND [name] = N'CK_VoiceProfileVersions_PreviewMetadata'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceProfileVersions] WITH CHECK
               ADD CONSTRAINT [CK_VoiceProfileVersions_PreviewMetadata]
               CHECK (([PreviewProviderRequestId] IS NULL AND [PreviewSha256] IS NULL AND [PreviewDurationMs] IS NULL AND [PreviewExpiresAtUtc] IS NULL) OR
                      ([PreviewProviderRequestId] IS NOT NULL AND [PreviewSha256] IS NOT NULL AND [PreviewDurationMs] > 0 AND [PreviewExpiresAtUtc] IS NOT NULL));');

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [name] = N'CK_ProviderRequests_Kind'
    )
        ALTER TABLE [vf].[ProviderRequests] DROP CONSTRAINT [CK_ProviderRequests_Kind];

    ALTER TABLE [vf].[ProviderRequests] WITH CHECK
        ADD CONSTRAINT [CK_ProviderRequests_Kind]
        CHECK ([RequestKind] IN
        ('Text','TextRepair','Image','Video','Voice','VoicePreview','Transcription','Search','Music','SoundEffect'));

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.4-voice-profile-approval'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.4-voice-profile-approval',
            N'Explicit costed voice preview evidence and user approval audit for immutable voice profile versions.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewProviderRequestId') IS NULL OR
   COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewSha256') IS NULL OR
   COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewDurationMs') IS NULL OR
   COL_LENGTH(N'vf.VoiceProfileVersions', N'PreviewExpiresAtUtc') IS NULL OR
   COL_LENGTH(N'vf.VoiceProfileVersions', N'ApprovedByUserId') IS NULL OR
   NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.4-voice-profile-approval')
    THROW 51141, 'Voice profile approval schema verification failed.', 1;

PRINT N'VideoFactory voice profile approval schema 4.1.4 is ready.';
GO
