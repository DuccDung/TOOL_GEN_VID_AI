/*
    Text-only continuity library for VideoMaker 4.0.7.

    Adds project-scoped background/prop/item profiles, immutable locked text
    versions, per-scene assignments and provider-request version snapshots.
    No image payload or provider credential is stored by this migration.

    Run only after a verified backup and after
    VideoFactory.4.0.0.OrganizationAiGateway.sql.
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
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51070, 'VideoFactory workflow tables and ai.SchemaVersions are required before project asset migration.', 1;
    END;

    IF OBJECT_ID(N'[vf].[ProjectAssets]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ProjectAssets]
        (
            [ProjectAssetId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_ProjectAssets_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]            uniqueidentifier NOT NULL,
            [AssetType]            varchar(20) NOT NULL,
            [Name]                 nvarchar(160) NOT NULL,
            [CanonicalDescription] nvarchar(2000) NOT NULL,
            [Status]               varchar(20) NOT NULL
                CONSTRAINT [DF_ProjectAssets_Status] DEFAULT ('Draft'),
            [CurrentVersion]       int NOT NULL
                CONSTRAINT [DF_ProjectAssets_CurrentVersion] DEFAULT (0),
            [LockedAtUtc]          datetime2(3) NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ProjectAssets_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [CreatedByUserId]      nvarchar(450) NOT NULL,
            [UpdatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ProjectAssets_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedByUserId]      nvarchar(450) NOT NULL,
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_ProjectAssets] PRIMARY KEY CLUSTERED ([ProjectAssetId]),
            CONSTRAINT [FK_ProjectAssets_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [UQ_ProjectAssets_Project_Type_Name]
                UNIQUE ([ProjectId], [AssetType], [Name]),
            CONSTRAINT [CK_ProjectAssets_Type]
                CHECK ([AssetType] IN ('Background','Prop','Item')),
            CONSTRAINT [CK_ProjectAssets_Status]
                CHECK ([Status] IN ('Draft','Locked')),
            CONSTRAINT [CK_ProjectAssets_CurrentVersion]
                CHECK ([CurrentVersion] >= 0),
            CONSTRAINT [CK_ProjectAssets_LockState]
                CHECK
                (
                    ([Status] = 'Draft' AND [LockedAtUtc] IS NULL) OR
                    ([Status] = 'Locked' AND [CurrentVersion] > 0 AND [LockedAtUtc] IS NOT NULL)
                )
        );

        CREATE INDEX [IX_ProjectAssets_Project_Status]
            ON [vf].[ProjectAssets] ([ProjectId], [Status]);
    END;

    IF OBJECT_ID(N'[vf].[ProjectAssetVersions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ProjectAssetVersions]
        (
            [ProjectAssetVersionId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_ProjectAssetVersions_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectAssetId]       uniqueidentifier NOT NULL,
            [Version]              int NOT NULL,
            [AssetType]            varchar(20) NOT NULL,
            [Name]                 nvarchar(160) NOT NULL,
            [CanonicalDescription] nvarchar(2000) NOT NULL,
            [LockedAtUtc]          datetime2(3) NOT NULL,
            [LockedByUserId]       nvarchar(450) NOT NULL,
            CONSTRAINT [PK_ProjectAssetVersions] PRIMARY KEY CLUSTERED ([ProjectAssetVersionId]),
            CONSTRAINT [FK_ProjectAssetVersions_ProjectAssets]
                FOREIGN KEY ([ProjectAssetId]) REFERENCES [vf].[ProjectAssets]([ProjectAssetId]),
            CONSTRAINT [UQ_ProjectAssetVersions_Asset_Version]
                UNIQUE ([ProjectAssetId], [Version]),
            CONSTRAINT [CK_ProjectAssetVersions_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_ProjectAssetVersions_Type]
                CHECK ([AssetType] IN ('Background','Prop','Item'))
        );
    END;

    IF OBJECT_ID(N'[vf].[SceneAssetAssignments]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[SceneAssetAssignments]
        (
            [SceneId]           uniqueidentifier NOT NULL,
            [ProjectAssetId]    uniqueidentifier NOT NULL,
            [AssignedByUserId]  nvarchar(450) NOT NULL,
            [AssignedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_SceneAssetAssignments_AssignedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_SceneAssetAssignments]
                PRIMARY KEY CLUSTERED ([SceneId], [ProjectAssetId]),
            CONSTRAINT [FK_SceneAssetAssignments_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_SceneAssetAssignments_ProjectAssets]
                FOREIGN KEY ([ProjectAssetId]) REFERENCES [vf].[ProjectAssets]([ProjectAssetId])
        );

        CREATE INDEX [IX_SceneAssetAssignments_ProjectAsset]
            ON [vf].[SceneAssetAssignments] ([ProjectAssetId]);
    END;

    IF OBJECT_ID(N'[vf].[ProviderRequestAssetVersions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ProviderRequestAssetVersions]
        (
            [ProviderRequestId]      uniqueidentifier NOT NULL,
            [ProjectAssetVersionId]  uniqueidentifier NOT NULL,
            [AppliedOrder]           smallint NOT NULL,
            CONSTRAINT [PK_ProviderRequestAssetVersions]
                PRIMARY KEY CLUSTERED ([ProviderRequestId], [ProjectAssetVersionId]),
            CONSTRAINT [FK_ProviderRequestAssetVersions_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests]([ProviderRequestId]),
            CONSTRAINT [FK_ProviderRequestAssetVersions_ProjectAssetVersions]
                FOREIGN KEY ([ProjectAssetVersionId]) REFERENCES [vf].[ProjectAssetVersions]([ProjectAssetVersionId]),
            CONSTRAINT [CK_ProviderRequestAssetVersions_AppliedOrder]
                CHECK ([AppliedOrder] >= 0)
        );

        CREATE INDEX [IX_ProviderRequestAssetVersions_AssetVersion]
            ON [vf].[ProviderRequestAssetVersions] ([ProjectAssetVersionId]);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.7-project-asset-text-library'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.7-project-asset-text-library',
            N'Add versioned text continuity assets, scene assignments and provider request snapshots.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF OBJECT_ID(N'[vf].[ProjectAssets]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[ProjectAssetVersions]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[SceneAssetAssignments]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[ProviderRequestAssetVersions]', N'U') IS NULL
BEGIN
    THROW 51071, 'Project asset text library table verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.0.7-project-asset-text-library'
)
BEGIN
    THROW 51072, 'Project asset text library schema version was not recorded.', 1;
END;

PRINT N'VideoFactory project asset text library 4.0.7 is ready.';
GO
