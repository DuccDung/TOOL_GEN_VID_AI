/*
    Scene workflow status compatibility for VideoMaker 4.0.5.

    The Native Audio workflow persists PromptInvalid, AudioReviewRequired and
    NativeAudioInvalid. Older databases still have CK_Scenes_Status from the
    initial workflow schema and reject those values, which hides the intended
    validation result behind a generic DbUpdateException.

    This migration is intentionally idempotent. Run only after a verified
    backup and after VideoFactory.4.0.0.OrganizationAiGateway.sql.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[Scenes]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51050, 'VideoFactory workflow tables and ai.SchemaVersions are required before scene status migration.', 1;
    END;

    DECLARE @SceneStatusDefinition nvarchar(max);
    SELECT @SceneStatusDefinition = [definition]
    FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
      AND [name] = N'CK_Scenes_Status';

    IF @SceneStatusDefinition IS NULL OR
       @SceneStatusDefinition NOT LIKE N'%PromptInvalid%' OR
       @SceneStatusDefinition NOT LIKE N'%AudioReviewRequired%' OR
       @SceneStatusDefinition NOT LIKE N'%NativeAudioInvalid%'
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
              AND [name] = N'CK_Scenes_Status'
        )
            ALTER TABLE [vf].[Scenes] DROP CONSTRAINT [CK_Scenes_Status];

        ALTER TABLE [vf].[Scenes] WITH CHECK ADD CONSTRAINT [CK_Scenes_Status]
            CHECK ([Status] IN
            (
                'Pending',
                'PromptReady',
                'PromptInvalid',
                'Generating',
                'WaitingProvider',
                'Generated',
                'AudioReviewRequired',
                'NativeAudioInvalid',
                'Validating',
                'Approved',
                'Failed',
                'RetryScheduled',
                'Cancelled'
            ));
        ALTER TABLE [vf].[Scenes] CHECK CONSTRAINT [CK_Scenes_Status];
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.5-scene-native-audio-statuses'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.5-scene-native-audio-statuses',
            N'Allow PromptInvalid, AudioReviewRequired and NativeAudioInvalid in the scene workflow.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
      AND [name] = N'CK_Scenes_Status'
      AND [is_disabled] = 0
      AND [is_not_trusted] = 0
      AND [definition] LIKE N'%PromptInvalid%'
      AND [definition] LIKE N'%AudioReviewRequired%'
      AND [definition] LIKE N'%NativeAudioInvalid%'
)
BEGIN
    THROW 51051, 'Scene status constraint verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.0.5-scene-native-audio-statuses'
)
BEGIN
    THROW 51052, 'Scene status schema version was not recorded.', 1;
END;

PRINT N'VideoFactory scene Native Audio statuses 4.0.5 are ready.';
GO
