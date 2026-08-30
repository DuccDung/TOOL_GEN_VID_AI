/*
    Native Audio workflow status compatibility for VideoMaker 4.0.6.

    VideoFactory.4.0.5 added the new statuses to vf.Scenes, but the final
    download transaction also writes AudioReviewRequired or NativeAudioInvalid
    to vf.VideoGenerations. Databases with the original generation constraint
    reject that transaction after the provider output has already downloaded.

    This migration is intentionally idempotent and does not depend on 4.0.5
    having been applied. Run only after a verified backup and after
    VideoFactory.4.0.0.OrganizationAiGateway.sql.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[Scenes]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[VideoGenerations]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51060, 'VideoFactory workflow tables and ai.SchemaVersions are required before Native Audio status migration.', 1;
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
    END;

    ALTER TABLE [vf].[Scenes]
        WITH CHECK CHECK CONSTRAINT [CK_Scenes_Status];

    DECLARE @VideoGenerationStatusDefinition nvarchar(max);
    SELECT @VideoGenerationStatusDefinition = [definition]
    FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VideoGenerations]')
      AND [name] = N'CK_VideoGenerations_Status';

    IF @VideoGenerationStatusDefinition IS NULL OR
       @VideoGenerationStatusDefinition NOT LIKE N'%AudioReviewRequired%' OR
       @VideoGenerationStatusDefinition NOT LIKE N'%NativeAudioInvalid%'
    BEGIN
        IF EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VideoGenerations]')
              AND [name] = N'CK_VideoGenerations_Status'
        )
            ALTER TABLE [vf].[VideoGenerations] DROP CONSTRAINT [CK_VideoGenerations_Status];

        ALTER TABLE [vf].[VideoGenerations] WITH CHECK ADD CONSTRAINT [CK_VideoGenerations_Status]
            CHECK ([Status] IN
            (
                'Pending',
                'Submitting',
                'WaitingProvider',
                'Downloading',
                'Generated',
                'AudioReviewRequired',
                'NativeAudioInvalid',
                'Validating',
                'Approved',
                'Failed',
                'Cancelled'
            ));
    END;

    ALTER TABLE [vf].[VideoGenerations]
        WITH CHECK CHECK CONSTRAINT [CK_VideoGenerations_Status];

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.6-native-audio-workflow-statuses'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.6-native-audio-workflow-statuses',
            N'Allow Native Audio review results in both scene and video generation workflow constraints.'
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
    THROW 51061, 'Scene status constraint verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VideoGenerations]')
      AND [name] = N'CK_VideoGenerations_Status'
      AND [is_disabled] = 0
      AND [is_not_trusted] = 0
      AND [definition] LIKE N'%AudioReviewRequired%'
      AND [definition] LIKE N'%NativeAudioInvalid%'
)
BEGIN
    THROW 51062, 'Video generation status constraint verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.0.6-native-audio-workflow-statuses'
)
BEGIN
    THROW 51063, 'Native Audio workflow status schema version was not recorded.', 1;
END;

PRINT N'VideoFactory Native Audio workflow statuses 4.0.6 are ready.';
GO
