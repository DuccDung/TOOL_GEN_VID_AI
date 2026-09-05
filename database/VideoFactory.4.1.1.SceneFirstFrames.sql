/*
    Scene-specific first frames for Fal/Veo, VideoMaker 4.1.1.

    Keeps square character identity references separate from the approved
    16:9/9:16 image that is submitted to Veo. This migration is idempotent,
    does not backfill old character images, and does not enable providers,
    credentials, rates, policies, or paid requests.
*/

USE [VideoFactory];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'[vf].[Projects]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[Scenes]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[ScenePrompts]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[MediaAssets]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[CharacterReferences]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51110, 'VideoFactory workflow tables and ai.SchemaVersions are required before scene first-frame migration.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[SceneFirstFrames]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[SceneFirstFrames]
        (
            [SceneFirstFrameId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_SceneFirstFrames_Id] DEFAULT NEWSEQUENTIALID(),
            [SceneId] uniqueidentifier NOT NULL,
            [MediaAssetId] uniqueidentifier NOT NULL,
            [Version] int NOT NULL,
            [Status] varchar(20) NOT NULL
                CONSTRAINT [DF_SceneFirstFrames_Status] DEFAULT ('PendingReview'),
            [SourceCharacterReferenceId] uniqueidentifier NULL,
            [GeneratedByProviderRequestId] uniqueidentifier NULL,
            [ScenePlanVersion] int NOT NULL,
            [ScenePromptId] uniqueidentifier NOT NULL,
            [ScenePromptVersion] int NOT NULL,
            [AspectRatio] varchar(10) NOT NULL,
            [PromptTemplateVersion] varchar(80) NOT NULL,
            [CreatedByUserId] nvarchar(450) NOT NULL,
            [ApprovedByUserId] nvarchar(450) NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_SceneFirstFrames_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc] datetime2(3) NULL,
            [InvalidatedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,

            CONSTRAINT [PK_SceneFirstFrames]
                PRIMARY KEY CLUSTERED ([SceneFirstFrameId]),
            CONSTRAINT [FK_SceneFirstFrames_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_SceneFirstFrames_MediaAssets]
                FOREIGN KEY ([MediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [FK_SceneFirstFrames_CharacterReferences]
                FOREIGN KEY ([SourceCharacterReferenceId]) REFERENCES [vf].[CharacterReferences]([CharacterReferenceId]),
            CONSTRAINT [FK_SceneFirstFrames_GeneratedProviderRequests]
                FOREIGN KEY ([GeneratedByProviderRequestId]) REFERENCES [vf].[ProviderRequests]([ProviderRequestId]),
            CONSTRAINT [FK_SceneFirstFrames_ScenePrompts]
                FOREIGN KEY ([ScenePromptId]) REFERENCES [vf].[ScenePrompts]([ScenePromptId]),
            CONSTRAINT [UQ_SceneFirstFrames_Scene_Version]
                UNIQUE ([SceneId], [Version]),
            CONSTRAINT [UQ_SceneFirstFrames_MediaAsset]
                UNIQUE ([MediaAssetId]),
            CONSTRAINT [CK_SceneFirstFrames_Version]
                CHECK ([Version] > 0 AND [ScenePlanVersion] > 0 AND [ScenePromptVersion] > 0),
            CONSTRAINT [CK_SceneFirstFrames_Status]
                CHECK ([Status] IN ('PendingReview','Approved','Rejected','Superseded','Invalidated')),
            CONSTRAINT [CK_SceneFirstFrames_AspectRatio]
                CHECK ([AspectRatio] IN ('16:9','9:16')),
            CONSTRAINT [CK_SceneFirstFrames_ApprovalState]
                CHECK
                (
                    ([Status] = 'Approved' AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [InvalidatedAtUtc] IS NULL) OR
                    ([Status] = 'Invalidated' AND [InvalidatedAtUtc] IS NOT NULL) OR
                    ([Status] IN ('PendingReview','Rejected','Superseded'))
                )
        );

        CREATE INDEX [IX_SceneFirstFrames_Scene_Status_Version]
            ON [vf].[SceneFirstFrames] ([SceneId], [Status], [Version] DESC);

        CREATE UNIQUE INDEX [UX_SceneFirstFrames_GeneratedProviderRequest]
            ON [vf].[SceneFirstFrames] ([GeneratedByProviderRequestId])
            WHERE [GeneratedByProviderRequestId] IS NOT NULL;

        CREATE UNIQUE INDEX [UX_SceneFirstFrames_ActiveApproved]
            ON [vf].[SceneFirstFrames] ([SceneId])
            WHERE [Status] = 'Approved';
    END;

    IF COL_LENGTH(N'vf.ProviderRequests', N'InputSceneFirstFrameId') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [InputSceneFirstFrameId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [name] = N'FK_ProviderRequests_InputSceneFirstFrames'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [vf].[ProviderRequests] WITH CHECK
                ADD CONSTRAINT [FK_ProviderRequests_InputSceneFirstFrames]
                    FOREIGN KEY ([InputSceneFirstFrameId])
                    REFERENCES [vf].[SceneFirstFrames]([SceneFirstFrameId]);
            ALTER TABLE [vf].[ProviderRequests]
                CHECK CONSTRAINT [FK_ProviderRequests_InputSceneFirstFrames];';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [name] = N'IX_ProviderRequests_InputSceneFirstFrame'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE INDEX [IX_ProviderRequests_InputSceneFirstFrame]
                ON [vf].[ProviderRequests] ([InputSceneFirstFrameId])
                WHERE [InputSceneFirstFrameId] IS NOT NULL;';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.1-scene-first-frames'
    )
    BEGIN
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.1-scene-first-frames',
            N'Tách ảnh nhận diện nhân vật khỏi first-frame theo scene cho Fal/Veo.'
        );
    END;

    IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
    BEGIN
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[vf].[SceneFirstFrames]
            TO [VideoMakerDesktopRole];
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'[vf].[SceneFirstFrames]', N'U') IS NULL OR
   COL_LENGTH(N'vf.ProviderRequests', N'InputSceneFirstFrameId') IS NULL OR
   NOT EXISTS
   (
       SELECT 1
       FROM [ai].[SchemaVersions]
       WHERE [Version] = '4.1.1-scene-first-frames'
   )
BEGIN
    THROW 51111, 'Scene first-frame migration verification failed.', 1;
END;
GO

PRINT N'VideoFactory scene first-frames 4.1.1 is ready.';
GO
