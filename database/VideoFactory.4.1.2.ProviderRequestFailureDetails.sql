/*
    Provider request failure details and repair lineage, VideoMaker 4.1.2.

    Persists safe structured validation details and links a paid content-repair
    request to the failed content request that it repairs. This migration is
    idempotent and does not enable providers, credentials, rates, policies, or
    paid requests.
*/

USE [VideoFactory];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51120, 'VideoFactory provider requests and ai.SchemaVersions are required before failure-details migration.', 1;
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'vf.ProviderRequests', N'ErrorDetailsJson') IS NULL
    BEGIN
        ALTER TABLE [vf].[ProviderRequests]
            ADD [ErrorDetailsJson] nvarchar(max) NULL;
    END;

    IF COL_LENGTH(N'vf.ProviderRequests', N'ParentProviderRequestId') IS NULL
    BEGIN
        ALTER TABLE [vf].[ProviderRequests]
            ADD [ParentProviderRequestId] uniqueidentifier NULL;
    END;

    /*
        SQL Server binds static column references for the whole batch before it
        executes ALTER TABLE. Keep all DDL that consumes the new columns in
        dynamic SQL so a first-time migration can compile successfully.
    */

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_ProviderRequests_ErrorDetailsJson'
          AND [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [vf].[ProviderRequests] WITH CHECK
                ADD CONSTRAINT [CK_ProviderRequests_ErrorDetailsJson]
                CHECK ([ErrorDetailsJson] IS NULL OR ISJSON([ErrorDetailsJson]) = 1);
        ';
    END;

    ALTER TABLE [vf].[ProviderRequests] WITH CHECK
        CHECK CONSTRAINT [CK_ProviderRequests_ErrorDetailsJson];

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_ProviderRequests_ParentProviderRequest'
          AND [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [vf].[ProviderRequests] WITH CHECK
                ADD CONSTRAINT [FK_ProviderRequests_ParentProviderRequest]
                FOREIGN KEY ([ParentProviderRequestId])
                REFERENCES [vf].[ProviderRequests]([ProviderRequestId]);
        ';
    END;

    ALTER TABLE [vf].[ProviderRequests] WITH CHECK
        CHECK CONSTRAINT [FK_ProviderRequests_ParentProviderRequest];

    DECLARE @ProviderRequestKindDefinition nvarchar(max);
    SELECT @ProviderRequestKindDefinition = [definition]
    FROM sys.check_constraints
    WHERE [name] = N'CK_ProviderRequests_Kind'
      AND [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]');

    IF @ProviderRequestKindDefinition IS NULL OR
       @ProviderRequestKindDefinition NOT LIKE N'%TextRepair%'
    BEGIN
        IF @ProviderRequestKindDefinition IS NOT NULL
        BEGIN
            ALTER TABLE [vf].[ProviderRequests]
                DROP CONSTRAINT [CK_ProviderRequests_Kind];
        END;

        ALTER TABLE [vf].[ProviderRequests] WITH CHECK
            ADD CONSTRAINT [CK_ProviderRequests_Kind]
            CHECK ([RequestKind] IN
            (
                'Text',
                'TextRepair',
                'Image',
                'Video',
                'Voice',
                'Search',
                'Music',
                'SoundEffect'
            ));
    END;

    ALTER TABLE [vf].[ProviderRequests] WITH CHECK
        CHECK CONSTRAINT [CK_ProviderRequests_Kind];

    IF EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'IX_ProviderRequests_Parent'
          AND [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
    ) AND NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes i
        WHERE i.[name] = N'IX_ProviderRequests_Parent'
          AND i.[object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND i.[is_unique] = 1
          AND i.[has_filter] = 1
          AND CHARINDEX(N'ParentProviderRequestId', i.[filter_definition]) > 0
          AND CHARINDEX(N'RequestKind', i.[filter_definition]) > 0
          AND CHARINDEX(N'TextRepair', i.[filter_definition]) > 0
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns ic
              INNER JOIN sys.columns c
                  ON c.[object_id] = ic.[object_id]
                 AND c.[column_id] = ic.[column_id]
              WHERE ic.[object_id] = i.[object_id]
                AND ic.[index_id] = i.[index_id]
                AND ic.[key_ordinal] = 1
                AND c.[name] = N'ParentProviderRequestId'
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns ic
              WHERE ic.[object_id] = i.[object_id]
                AND ic.[index_id] = i.[index_id]
                AND ic.[key_ordinal] > 1
          )
    )
    BEGIN
        DROP INDEX [IX_ProviderRequests_Parent] ON [vf].[ProviderRequests];
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'IX_ProviderRequests_Parent'
          AND [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [is_unique] = 1
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX [IX_ProviderRequests_Parent]
                ON [vf].[ProviderRequests]([ParentProviderRequestId])
                WHERE [ParentProviderRequestId] IS NOT NULL
                  AND [RequestKind] = ''TextRepair'';
        ';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.2-provider-request-failure-details'
    )
    BEGIN
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.2-provider-request-failure-details',
            N'Lưu chi tiết lỗi an toàn và quan hệ request repair cho content plan AI.'
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF COL_LENGTH(N'vf.ProviderRequests', N'ErrorDetailsJson') IS NULL OR
   COL_LENGTH(N'vf.ProviderRequests', N'ParentProviderRequestId') IS NULL OR
   NOT EXISTS
   (
       SELECT 1
       FROM sys.check_constraints
       WHERE [name] = N'CK_ProviderRequests_ErrorDetailsJson'
         AND [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
         AND [is_disabled] = 0
         AND [is_not_trusted] = 0
         AND CHARINDEX(N'ErrorDetailsJson', [definition]) > 0
         AND CHARINDEX(N'ISJSON', [definition]) > 0
   ) OR
   NOT EXISTS
   (
       SELECT 1
       FROM sys.check_constraints
       WHERE [name] = N'CK_ProviderRequests_Kind'
         AND [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
         AND [is_disabled] = 0
         AND [is_not_trusted] = 0
         AND CHARINDEX(N'TextRepair', [definition]) > 0
   ) OR
   NOT EXISTS
   (
       SELECT 1
       FROM sys.foreign_keys
       WHERE [name] = N'FK_ProviderRequests_ParentProviderRequest'
         AND [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
         AND [referenced_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
         AND [is_disabled] = 0
         AND [is_not_trusted] = 0
   ) OR
   NOT EXISTS
   (
       SELECT 1
       FROM sys.indexes i
       WHERE i.[name] = N'IX_ProviderRequests_Parent'
         AND i.[object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
         AND i.[is_unique] = 1
         AND i.[has_filter] = 1
         AND CHARINDEX(N'ParentProviderRequestId', i.[filter_definition]) > 0
         AND CHARINDEX(N'RequestKind', i.[filter_definition]) > 0
         AND CHARINDEX(N'TextRepair', i.[filter_definition]) > 0
         AND EXISTS
         (
             SELECT 1
             FROM sys.index_columns ic
             INNER JOIN sys.columns c
                 ON c.[object_id] = ic.[object_id]
                AND c.[column_id] = ic.[column_id]
             WHERE ic.[object_id] = i.[object_id]
               AND ic.[index_id] = i.[index_id]
               AND ic.[key_ordinal] = 1
               AND c.[name] = N'ParentProviderRequestId'
         )
         AND NOT EXISTS
         (
             SELECT 1
             FROM sys.index_columns ic
             WHERE ic.[object_id] = i.[object_id]
               AND ic.[index_id] = i.[index_id]
               AND ic.[key_ordinal] > 1
         )
   ) OR
   NOT EXISTS
   (
       SELECT 1
       FROM [ai].[SchemaVersions]
       WHERE [Version] = '4.1.2-provider-request-failure-details'
   )
BEGIN
    THROW 51121, 'Provider request failure-details migration verification failed.', 1;
END;

PRINT N'VideoFactory provider request failure details 4.1.2 are ready.';
GO
