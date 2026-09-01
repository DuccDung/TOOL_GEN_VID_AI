/*
    AI-generated continuity asset metadata for VideoMaker 4.0.8.

    Run only after a verified backup and after
    VideoFactory.4.0.7.ProjectAssetTextLibrary.sql.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[ProjectAssets]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51080, 'Project asset library and ai.SchemaVersions are required before AI asset metadata migration.', 1;
    END;

    IF COL_LENGTH(N'vf.ProjectAssets', N'AssetKey') IS NULL
        ALTER TABLE [vf].[ProjectAssets] ADD [AssetKey] nvarchar(80) NULL;

    IF COL_LENGTH(N'vf.ProjectAssets', N'SourceKind') IS NULL
        ALTER TABLE [vf].[ProjectAssets] ADD [SourceKind] varchar(20) NULL;

    IF COL_LENGTH(N'vf.ProjectAssets', N'SourcePlanVersion') IS NULL
        ALTER TABLE [vf].[ProjectAssets] ADD [SourcePlanVersion] int NULL;

    IF COL_LENGTH(N'vf.ProjectAssets', N'GeneratedByProviderRequestId') IS NULL
        ALTER TABLE [vf].[ProjectAssets] ADD [GeneratedByProviderRequestId] uniqueidentifier NULL;

    -- SQL Server compiles static column references for the whole batch before
    -- executing ALTER TABLE. Defer compilation until the new columns exist.
    EXEC sys.sp_executesql N'
        UPDATE [vf].[ProjectAssets]
        SET [AssetKey] = N''manual-'' + LOWER(REPLACE(CONVERT(nvarchar(36), [ProjectAssetId]), N''-'', N''''))
        WHERE [AssetKey] IS NULL OR LTRIM(RTRIM([AssetKey])) = N'''';

        UPDATE [vf].[ProjectAssets]
        SET [SourceKind] = ''Manual''
        WHERE [SourceKind] IS NULL OR LTRIM(RTRIM([SourceKind])) = '''';
    ';

    EXEC sys.sp_executesql N'
        ALTER TABLE [vf].[ProjectAssets] ALTER COLUMN [AssetKey] nvarchar(80) NOT NULL;
        ALTER TABLE [vf].[ProjectAssets] ALTER COLUMN [SourceKind] varchar(20) NOT NULL;
    ';

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE [name] = N'DF_ProjectAssets_SourceKind')
        EXEC sys.sp_executesql N'
            ALTER TABLE [vf].[ProjectAssets]
                ADD CONSTRAINT [DF_ProjectAssets_SourceKind] DEFAULT (''Manual'') FOR [SourceKind];
        ';

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = N'CK_ProjectAssets_SourceKind')
        EXEC sys.sp_executesql N'
            ALTER TABLE [vf].[ProjectAssets]
                ADD CONSTRAINT [CK_ProjectAssets_SourceKind]
                    CHECK ([SourceKind] IN (''Manual'',''AiGenerated''));
        ';

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [name] = N'CK_ProjectAssets_SourcePlanVersion')
        EXEC sys.sp_executesql N'
            ALTER TABLE [vf].[ProjectAssets]
                ADD CONSTRAINT [CK_ProjectAssets_SourcePlanVersion]
                    CHECK ([SourcePlanVersion] IS NULL OR [SourcePlanVersion] > 0);
        ';

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[ProjectAssets]')
          AND [name] = N'UQ_ProjectAssets_Project_AssetKey'
    )
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX [UQ_ProjectAssets_Project_AssetKey]
                ON [vf].[ProjectAssets] ([ProjectId], [AssetKey]);
        ';

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.8-ai-generated-project-assets'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.8-ai-generated-project-assets',
            N'Add stable asset keys and AI generation provenance to project continuity assets.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
