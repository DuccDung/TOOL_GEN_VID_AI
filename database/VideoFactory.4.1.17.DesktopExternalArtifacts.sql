/* External desktop artifact metadata only. Apply on the approved server database
   after backup/restore rehearsal. Hide all ExternalVps releases before rolling
   back to an older server. No remote file, credential or desktop SQL permission is changed. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'auth.AppReleases', N'U') IS NULL
        OR OBJECT_ID(N'auth.AppReleaseArtifacts', N'U') IS NULL
        OR OBJECT_ID(N'ai.SchemaVersions', N'U') IS NULL
        THROW 51170, 'Desktop release tables and schema versions are required.', 1;

    IF COL_LENGTH(N'auth.AppReleases', N'FirstPublishedAtUtc') IS NULL
        ALTER TABLE [auth].[AppReleases] ADD [FirstPublishedAtUtc] datetime2(3) NULL;
    IF COL_LENGTH(N'auth.AppReleases', N'Revision') IS NULL
        ALTER TABLE [auth].[AppReleases] ADD [Revision] uniqueidentifier NOT NULL
            CONSTRAINT [DF_AppReleases_Revision] DEFAULT NEWID() WITH VALUES;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'SourceKind') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [SourceKind] varchar(20) NOT NULL
            CONSTRAINT [DF_AppReleaseArtifacts_SourceKind] DEFAULT ('Internal') WITH VALUES;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'ExternalDownloadUrl') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [ExternalDownloadUrl] nvarchar(2000) NULL;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'InstallMode') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [InstallMode] varchar(30) NOT NULL
            CONSTRAINT [DF_AppReleaseArtifacts_InstallMode] DEFAULT ('Legacy') WITH VALUES;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'DistributionProtocol') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [DistributionProtocol] int NOT NULL
            CONSTRAINT [DF_AppReleaseArtifacts_DistributionProtocol] DEFAULT (1) WITH VALUES;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'RegistrationEvidenceJson') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [RegistrationEvidenceJson] nvarchar(max) NULL;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'BoundPackageSha256') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [BoundPackageSha256] char(64) NULL;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'ModelBundleId') IS NULL
        ALTER TABLE [auth].[AppReleaseArtifacts] ADD [ModelBundleId] varchar(100) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'auth.AppReleaseArtifacts')
                   AND name = N'RelativePath' AND system_type_id = TYPE_ID(N'nvarchar') AND max_length = 2000)
        THROW 51171, 'Unexpected artifact path schema; inspect the target database before migration.', 1;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'auth.AppReleaseArtifacts')
               AND name = N'RelativePath' AND is_nullable = 0)
        ALTER TABLE [auth].[AppReleaseArtifacts] ALTER COLUMN [RelativePath] nvarchar(1000) NULL;

    IF EXISTS (
        SELECT 1 FROM (VALUES
            (N'auth.AppReleases', N'FirstPublishedAtUtc', N'datetime2', NULL, 1),
            (N'auth.AppReleases', N'Revision', N'uniqueidentifier', 16, 0),
            (N'auth.AppReleaseArtifacts', N'SourceKind', N'varchar', 20, 0),
            (N'auth.AppReleaseArtifacts', N'ExternalDownloadUrl', N'nvarchar', 4000, 1),
            (N'auth.AppReleaseArtifacts', N'InstallMode', N'varchar', 30, 0),
            (N'auth.AppReleaseArtifacts', N'DistributionProtocol', N'int', 4, 0),
            (N'auth.AppReleaseArtifacts', N'RegistrationEvidenceJson', N'nvarchar', -1, 1),
            (N'auth.AppReleaseArtifacts', N'BoundPackageSha256', N'char', 64, 1),
            (N'auth.AppReleaseArtifacts', N'ModelBundleId', N'varchar', 100, 1)
        ) AS expected(table_name, column_name, type_name, max_length, is_nullable)
        LEFT JOIN sys.columns c ON c.object_id = OBJECT_ID(expected.table_name) AND c.name = expected.column_name
        WHERE c.column_id IS NULL OR c.system_type_id <> TYPE_ID(expected.type_name)
            OR (expected.max_length IS NOT NULL AND c.max_length <> expected.max_length) OR c.is_nullable <> expected.is_nullable
    ) THROW 51172, 'Unexpected external artifact column schema; inspect the target database.', 1;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'auth.AppReleases')
               AND name = N'FirstPublishedAtUtc' AND scale <> 3)
        THROW 51173, 'Unexpected publication timestamp precision.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'auth.AppReleaseArtifacts')
                   AND name = N'UQ_AppReleaseArtifacts_Release_Kind')
        CREATE UNIQUE INDEX [UQ_AppReleaseArtifacts_Release_Kind]
            ON [auth].[AppReleaseArtifacts] ([AppReleaseId], [Kind]);

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes i WHERE i.object_id = OBJECT_ID(N'auth.AppReleaseArtifacts')
          AND i.name = N'UQ_AppReleaseArtifacts_Release_Kind' AND i.is_unique = 1 AND i.is_disabled = 0
          AND i.is_hypothetical = 0 AND i.has_filter = 0 AND i.ignore_dup_key = 0
          AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 AND c.name = N'AppReleaseId')
          AND EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 2 AND c.name = N'Kind')
    ) THROW 51174, 'The artifact release/kind index must be enabled, unfiltered, unique and cover the exact two keys.', 1;

    DECLARE @checks TABLE ([Name] sysname NOT NULL, [Expression] nvarchar(max) NOT NULL);
    INSERT INTO @checks ([Name], [Expression]) VALUES
        (N'CK_AppReleaseArtifacts_Source', N'(([SourceKind] = ''Internal'' AND [RelativePath] IS NOT NULL AND LEN([RelativePath]) > 0 AND [ExternalDownloadUrl] IS NULL)
                OR ([SourceKind] = ''ExternalVps'' AND [Kind] = ''DesktopPackage'' AND [RelativePath] IS NULL
                    AND [ExternalDownloadUrl] IS NOT NULL AND LEN([ExternalDownloadUrl]) > 0
                    AND [InstallMode] = ''StaticArchive'' AND [DistributionProtocol] = 2
                    AND [RegistrationEvidenceJson] IS NOT NULL AND [BoundPackageSha256] IS NOT NULL AND [BoundPackageSha256] = [Sha256]
                    AND [ModelBundleId] IS NOT NULL AND LEN([ModelBundleId]) > 0))'),
        (N'CK_AppReleaseArtifacts_Protocol', N'(([InstallMode] = ''Legacy'' AND [DistributionProtocol] = 1)
                OR ([InstallMode] = ''StaticArchive'' AND [DistributionProtocol] = 2))'),
        (N'CK_AppReleaseArtifacts_Evidence', N'(([RegistrationEvidenceJson] IS NULL OR ISJSON([RegistrationEvidenceJson]) = 1)
                AND ([BoundPackageSha256] IS NULL OR ([BoundPackageSha256] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([BoundPackageSha256]) = 64)))');

    -- Let the same SQL engine canonicalize the expected expression, rather than
    -- removing parentheses and accidentally treating a different predicate as equal.
    -- This empty, session-owned temp table never contains application data.
    CREATE TABLE #DesktopExternalExpectedChecks (
        [SourceKind] varchar(20) NOT NULL, [RelativePath] nvarchar(1000) NULL,
        [ExternalDownloadUrl] nvarchar(2000) NULL, [Kind] varchar(30) NOT NULL,
        [InstallMode] varchar(30) NOT NULL, [DistributionProtocol] int NOT NULL,
        [RegistrationEvidenceJson] nvarchar(max) NULL, [BoundPackageSha256] char(64) NULL,
        [Sha256] char(64) NOT NULL, [ModelBundleId] varchar(100) NULL
    );
    DECLARE @name sysname, @expression nvarchar(max), @expectedName sysname,
            @expectedDefinition nvarchar(max), @actualDefinition nvarchar(max), @message nvarchar(2048), @statement nvarchar(max);
    DECLARE external_checks CURSOR LOCAL FAST_FORWARD FOR SELECT [Name], [Expression] FROM @checks ORDER BY [Name];
    OPEN external_checks;
    FETCH NEXT FROM external_checks INTO @name, @expression;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @expectedName = N'CK_VM_Expected_' + REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N'');
        SET @statement = N'ALTER TABLE #DesktopExternalExpectedChecks WITH CHECK ADD CONSTRAINT ' + QUOTENAME(@expectedName) + N' CHECK ' + @expression;
        EXEC sys.sp_executesql @statement;
        SET @expectedDefinition = NULL;
        SELECT @expectedDefinition = [definition] FROM tempdb.sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'tempdb..#DesktopExternalExpectedChecks') AND [name] = @expectedName;
        IF @expectedDefinition IS NULL THROW 51175, 'Cannot inspect expected external artifact constraints.', 1;
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'auth.AppReleaseArtifacts') AND [name] = @name)
        BEGIN
            SET @actualDefinition = NULL;
            SELECT @actualDefinition = [definition] FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'auth.AppReleaseArtifacts') AND [name] = @name
                    AND is_disabled = 0 AND is_not_trusted = 0 AND is_not_for_replication = 0;
            -- Compare the engine's complete definition, including string literals.
            -- Removing whitespace could accept a changed literal such as 'Inter nal'.
            IF @actualDefinition IS NULL OR
                @actualDefinition COLLATE Latin1_General_100_BIN2 <>
                @expectedDefinition COLLATE Latin1_General_100_BIN2
            BEGIN
                SET @message = N'Existing constraint is disabled, untrusted or has an unexpected definition: ' + @name;
                THROW 51176, @message, 1;
            END;
        END
        ELSE
        BEGIN
            SET @statement = N'ALTER TABLE [auth].[AppReleaseArtifacts] WITH CHECK ADD CONSTRAINT ' + QUOTENAME(@name) + N' CHECK ' + @expression;
            EXEC sys.sp_executesql @statement;
        END;
        FETCH NEXT FROM external_checks INTO @name, @expression;
    END;
    CLOSE external_checks;
    DEALLOCATE external_checks;
    DROP TABLE #DesktopExternalExpectedChecks;

    IF NOT EXISTS (SELECT 1 FROM [ai].[SchemaVersions] WHERE [Version] = '4.1.17-desktop-external-artifacts')
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description]) VALUES
            ('4.1.17-desktop-external-artifacts', N'Add explicit external VPS desktop package metadata, immutable publication and exact Setup binding; preserve internal release artifacts.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
