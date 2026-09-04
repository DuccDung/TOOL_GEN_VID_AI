/*
    Fal/Veo long-form policy scope for VideoMaker 4.0.9.
    This migration is idempotent and does not enable providers, models, rates, or credentials.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[ai].[OrganizationVideoPolicies]', N'U') IS NULL OR
   OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51090, 'Organization video policy and ai.SchemaVersions are required before Fal/Veo long-form migration.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'ai.OrganizationVideoPolicies', N'PolicyScope') IS NULL
    BEGIN
        ALTER TABLE [ai].[OrganizationVideoPolicies]
            ADD [PolicyScope] varchar(20) NOT NULL
                CONSTRAINT [DF_OrganizationVideoPolicies_PolicyScope] DEFAULT ('Default') WITH VALUES;
    END;

    -- PolicyScope can be added earlier in this same batch. Dynamic SQL delays
    -- name resolution until after ALTER TABLE has completed.
    EXEC sys.sp_executesql N'
        UPDATE [ai].[OrganizationVideoPolicies]
        SET [PolicyScope] = ''Default''
        WHERE [PolicyScope] IS NULL OR LTRIM(RTRIM([PolicyScope])) = '''';';

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[ai].[OrganizationVideoPolicies]')
          AND [name] = N'CK_OrganizationVideoPolicies_PolicyScope'
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE [ai].[OrganizationVideoPolicies] WITH CHECK
                ADD CONSTRAINT [CK_OrganizationVideoPolicies_PolicyScope]
                    CHECK ([PolicyScope] IN (''Default'', ''LongForm''));
            ALTER TABLE [ai].[OrganizationVideoPolicies]
                CHECK CONSTRAINT [CK_OrganizationVideoPolicies_PolicyScope];';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.key_constraints AS kc
        WHERE kc.[parent_object_id] = OBJECT_ID(N'[ai].[OrganizationVideoPolicies]')
          AND kc.[type] = 'PK'
          AND 2 =
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS ic
              WHERE ic.[object_id] = kc.[parent_object_id]
                AND ic.[index_id] = kc.[unique_index_id]
                AND ic.[key_ordinal] > 0
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                  ON c.[object_id] = ic.[object_id]
                 AND c.[column_id] = ic.[column_id]
              WHERE ic.[object_id] = kc.[parent_object_id]
                AND ic.[index_id] = kc.[unique_index_id]
                AND c.[name] = N'OrganizationId'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS ic
              INNER JOIN sys.columns AS c
                  ON c.[object_id] = ic.[object_id]
                 AND c.[column_id] = ic.[column_id]
              WHERE ic.[object_id] = kc.[parent_object_id]
                AND ic.[index_id] = kc.[unique_index_id]
                AND c.[name] = N'PolicyScope'
          )
    )
    BEGIN
        DECLARE @existingPolicyPk sysname;
        SELECT @existingPolicyPk = kc.[name]
        FROM sys.key_constraints AS kc
        WHERE kc.[parent_object_id] = OBJECT_ID(N'[ai].[OrganizationVideoPolicies]')
          AND kc.[type] = 'PK';

        IF @existingPolicyPk IS NOT NULL
        BEGIN
            DECLARE @dropPolicyPkSql nvarchar(max) =
                N'ALTER TABLE [ai].[OrganizationVideoPolicies] DROP CONSTRAINT ' +
                QUOTENAME(@existingPolicyPk) + N';';
            EXEC sys.sp_executesql @dropPolicyPkSql;
        END;

        EXEC sys.sp_executesql N'
            ALTER TABLE [ai].[OrganizationVideoPolicies]
                ADD CONSTRAINT [PK_OrganizationVideoPolicies]
                    PRIMARY KEY CLUSTERED ([OrganizationId], [PolicyScope]);';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.9-fal-veo-long-form'
    )
    BEGIN
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.9-fal-veo-long-form',
            N'Tách policy video Default/LongForm để chuẩn bị Fal/Veo mà không thay đổi video ngắn.'
        );
    END;

    IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
    BEGIN
        DENY SELECT, INSERT, UPDATE, DELETE
            ON OBJECT::[ai].[OrganizationVideoPolicies]
            TO [VideoMakerDesktopRole];
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

IF COL_LENGTH(N'ai.OrganizationVideoPolicies', N'PolicyScope') IS NULL OR
   NOT EXISTS
   (
       SELECT 1
       FROM [ai].[SchemaVersions]
       WHERE [Version] = '4.0.9-fal-veo-long-form'
   )
BEGIN
    THROW 51091, 'Fal/Veo long-form policy scope migration verification failed.', 1;
END;
