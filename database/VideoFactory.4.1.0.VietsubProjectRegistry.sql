/*
    Independent Vietsub project registry for VideoMaker 4.1.0.

    This migration stores metadata only. Video, subtitle text, local paths,
    workspace state and project.db remain on the desktop.

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

    IF OBJECT_ID(N'[ai].[Organizations]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[OrganizationAuditLogs]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL OR
       OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL
    BEGIN
        THROW 51100, 'Organization AI Gateway and Identity schema are required before Vietsub registry migration.', 1;
    END;

    IF SCHEMA_ID(N'vs') IS NULL
        EXEC(N'CREATE SCHEMA [vs] AUTHORIZATION [dbo];');

    IF OBJECT_ID(N'[vs].[Projects]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vs].[Projects]
        (
            [ProjectId]             uniqueidentifier NOT NULL,
            [OrganizationId]        uniqueidentifier NOT NULL,
            [CreatedByUserId]       nvarchar(450) NOT NULL,
            [Name]                  nvarchar(120) NOT NULL,
            [Status]                varchar(20) NOT NULL,
            [SourceLanguageCode]    varchar(16) NOT NULL,
            [TargetLanguageCode]    varchar(16) NOT NULL,
            [IsArchived]            bit NOT NULL
                CONSTRAINT [DF_VietsubProjects_IsArchived] DEFAULT (0),
            [CreatedAtUtc]          datetime2(3) NOT NULL,
            [UpdatedAtUtc]          datetime2(3) NOT NULL,
            [ArchivedAtUtc]         datetime2(3) NULL,
            [RowVersion]            rowversion NOT NULL,

            CONSTRAINT [PK_VietsubProjects]
                PRIMARY KEY CLUSTERED ([ProjectId]),
            CONSTRAINT [FK_VietsubProjects_Organizations]
                FOREIGN KEY ([OrganizationId])
                REFERENCES [ai].[Organizations] ([OrganizationId]),
            CONSTRAINT [FK_VietsubProjects_Users]
                FOREIGN KEY ([CreatedByUserId])
                REFERENCES [dbo].[AspNetUsers] ([Id]),
            CONSTRAINT [CK_VietsubProjects_Name]
                CHECK (LEN(LTRIM(RTRIM([Name]))) BETWEEN 1 AND 120),
            CONSTRAINT [CK_VietsubProjects_Status]
                CHECK ([Status] IN ('DRAFT','READY','PROCESSING','COMPLETED','FAILED')),
            CONSTRAINT [CK_VietsubProjects_SourceLanguage]
                CHECK ([SourceLanguageCode] IN ('auto','en','zh')),
            CONSTRAINT [CK_VietsubProjects_TargetLanguage]
                CHECK ([TargetLanguageCode] = 'vi'),
            CONSTRAINT [CK_VietsubProjects_Archive]
                CHECK (([IsArchived] = 0 AND [ArchivedAtUtc] IS NULL) OR
                       ([IsArchived] = 1 AND [ArchivedAtUtc] IS NOT NULL))
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vs].[Projects]')
          AND [name] = N'IX_VietsubProjects_Owner_Active_Updated'
    )
    BEGIN
        CREATE INDEX [IX_VietsubProjects_Owner_Active_Updated]
            ON [vs].[Projects]
            (
                [OrganizationId],
                [CreatedByUserId],
                [IsArchived],
                [UpdatedAtUtc] DESC
            )
            INCLUDE ([Name], [Status], [SourceLanguageCode], [TargetLanguageCode]);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.0-vietsub-project-registry'
    )
    BEGIN
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.0-vietsub-project-registry',
            N'Add isolated vs.Projects metadata registry for desktop Vietsub workspaces.'
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
