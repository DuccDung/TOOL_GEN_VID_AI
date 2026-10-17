/* Additive catalog publication after 4.1.18. No package is downloaded or declared server-verified.
   Run only on the approved database after backup/restore review. Existing protocol 1/2/3 data stays intact. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF @@TRANCOUNT <> 0 THROW 51190, 'Run this migration in its own session.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ai.SchemaVersions',N'U') IS NULL OR OBJECT_ID(N'auth.DesktopBootstrappers',N'U') IS NULL
        OR NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.18-shared-desktop-bootstrapper')
        THROW 51191, 'Verify and apply the complete 4.1.18 schema first.', 1;
    IF OBJECT_ID(N'auth.DesktopDirectReleases',N'U') IS NULL
    BEGIN
        CREATE TABLE auth.DesktopDirectReleases (
            DirectReleaseId uniqueidentifier NOT NULL CONSTRAINT DF_DesktopDirectReleases_Id DEFAULT NEWID(),
            [Version] varchar(50) NOT NULL, DownloadUrl nvarchar(2000) NOT NULL, FileName nvarchar(180) NOT NULL,
            ExpectedSha256 char(64) NOT NULL, DeclaredBuildNumber int NOT NULL,
            [Profile] varchar(50) NOT NULL, Channel varchar(20) NOT NULL, Platform varchar(20) NOT NULL,
            CatalogProtocol int NOT NULL, ApplicationDistributionProtocol int NOT NULL,
            ModelBundleId varchar(100) NOT NULL, MinimumBootstrapperVersion varchar(50) NOT NULL,
            ReleaseNotes nvarchar(max) NULL, IsMandatory bit NOT NULL, IsActive bit NOT NULL,
            PublishedAtUtc datetime2(3) NULL, FirstPublishedAtUtc datetime2(3) NULL, Revision uniqueidentifier NOT NULL,
            ActorUserId nvarchar(450) NOT NULL, ClientRequestId uniqueidentifier NOT NULL, CreateRequestFingerprint char(64) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL, UpdatedAtUtc datetime2(3) NOT NULL,
            CONSTRAINT PK_DesktopDirectReleases PRIMARY KEY(DirectReleaseId)
        );
    END;
    DECLARE @columns TABLE(ColumnName sysname, TypeName sysname, MaxLength smallint, Nullable bit);
    INSERT @columns VALUES
        (N'DirectReleaseId',N'uniqueidentifier',16,0),(N'Version',N'varchar',50,0),(N'DownloadUrl',N'nvarchar',4000,0),
        (N'FileName',N'nvarchar',360,0),(N'ExpectedSha256',N'char',64,0),(N'DeclaredBuildNumber',N'int',4,0),
        (N'Profile',N'varchar',50,0),(N'Channel',N'varchar',20,0),(N'Platform',N'varchar',20,0),
        (N'CatalogProtocol',N'int',4,0),(N'ApplicationDistributionProtocol',N'int',4,0),(N'ModelBundleId',N'varchar',100,0),
        (N'MinimumBootstrapperVersion',N'varchar',50,0),(N'ReleaseNotes',N'nvarchar',-1,1),(N'IsMandatory',N'bit',1,0),
        (N'IsActive',N'bit',1,0),(N'PublishedAtUtc',N'datetime2',7,1),(N'FirstPublishedAtUtc',N'datetime2',7,1),
        (N'Revision',N'uniqueidentifier',16,0),(N'ActorUserId',N'nvarchar',900,0),(N'ClientRequestId',N'uniqueidentifier',16,0),
        (N'CreateRequestFingerprint',N'char',64,0),(N'CreatedAtUtc',N'datetime2',7,0),(N'UpdatedAtUtc',N'datetime2',7,0);
    IF EXISTS(SELECT 1 FROM @columns e LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND c.name=e.ColumnName
        WHERE c.column_id IS NULL OR c.system_type_id<>TYPE_ID(e.TypeName) OR c.max_length<>e.MaxLength OR c.is_nullable<>e.Nullable
            OR (e.TypeName=N'datetime2' AND c.scale<>3))
        THROW 51192, 'Unexpected direct catalog columns; inspect partial schema.', 1;
    DECLARE @checks TABLE([Name] sysname, Expression nvarchar(max));
    INSERT @checks VALUES
        (N'CK_DesktopDirectReleases_Scope',N'([CatalogProtocol]=4 AND [ApplicationDistributionProtocol]=3 AND [Channel]=''Stable'' AND [Platform]=''win-x64'' AND LEN([Profile])>0 AND LEN([ModelBundleId])>0)'),
        (N'CK_DesktopDirectReleases_Identity',N'(LEN([Version])>0 AND [DeclaredBuildNumber]>0 AND LEN([DownloadUrl])>0 AND LEN([FileName])>0 AND LEN([MinimumBootstrapperVersion])>0 AND LEN([ActorUserId])>0)'),
        (N'CK_DesktopDirectReleases_Hashes',N'([ExpectedSha256] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([ExpectedSha256])=64 AND [CreateRequestFingerprint] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([CreateRequestFingerprint])=64)'),
        (N'CK_DesktopDirectReleases_Publication',N'(([FirstPublishedAtUtc] IS NULL AND [PublishedAtUtc] IS NULL AND [IsActive]=0) OR ([FirstPublishedAtUtc] IS NOT NULL AND [PublishedAtUtc] IS NOT NULL))'),
        (N'CK_DesktopDirectReleases_Notes',N'([ReleaseNotes] IS NULL OR DATALENGTH([ReleaseNotes])<=20000)');
    CREATE TABLE #DirectExpected(CatalogProtocol int,ApplicationDistributionProtocol int,Channel varchar(20),Platform varchar(20),[Profile] varchar(50),
        ModelBundleId varchar(100),[Version] varchar(50),DeclaredBuildNumber int,DownloadUrl nvarchar(2000),FileName nvarchar(180),
        MinimumBootstrapperVersion varchar(50),ActorUserId nvarchar(450),ExpectedSha256 char(64),CreateRequestFingerprint char(64),
        FirstPublishedAtUtc datetime2(3),PublishedAtUtc datetime2(3),IsActive bit,ReleaseNotes nvarchar(max));
    DECLARE @name sysname,@expression nvarchar(max),@temporary sysname,@statement nvarchar(max),@expected nvarchar(max),@actual nvarchar(max);
    DECLARE direct_checks CURSOR LOCAL FAST_FORWARD FOR SELECT [Name],Expression FROM @checks ORDER BY [Name];
    OPEN direct_checks; FETCH NEXT FROM direct_checks INTO @name,@expression;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @temporary=N'CK_DirectExpected_'+REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N'');
        SET @statement=N'ALTER TABLE #DirectExpected WITH CHECK ADD CONSTRAINT '+QUOTENAME(@temporary)+N' CHECK '+@expression;
        EXEC sys.sp_executesql @statement;
        SET @expected=NULL; SET @actual=NULL;
        SELECT @expected=definition FROM tempdb.sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'tempdb..#DirectExpected') AND name=@temporary;
        IF @expected IS NULL THROW 51193,'Cannot inspect expected direct catalog CHECK.',1;
        IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND name=@name)
        BEGIN
            SELECT @actual=definition FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND name=@name
                AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0;
            IF @actual IS NULL OR @actual COLLATE Latin1_General_100_BIN2<>@expected COLLATE Latin1_General_100_BIN2
                THROW 51194,'Unexpected or untrusted direct catalog CHECK.',1;
        END
        ELSE
        BEGIN
            SET @statement=N'ALTER TABLE auth.DesktopDirectReleases WITH CHECK ADD CONSTRAINT '+QUOTENAME(@name)+N' CHECK '+@expression;
            EXEC sys.sp_executesql @statement;
        END;
        FETCH NEXT FROM direct_checks INTO @name,@expression;
    END;
    CLOSE direct_checks; DEALLOCATE direct_checks; DROP TABLE #DirectExpected;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND name=N'UX_DesktopDirectReleases_Identity')
        CREATE UNIQUE INDEX UX_DesktopDirectReleases_Identity ON auth.DesktopDirectReleases([Profile],Channel,Platform,[Version]);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND name=N'UX_DesktopDirectReleases_Request')
        CREATE UNIQUE INDEX UX_DesktopDirectReleases_Request ON auth.DesktopDirectReleases(ActorUserId,ClientRequestId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND name=N'IX_DesktopDirectReleases_Visible')
        CREATE INDEX IX_DesktopDirectReleases_Visible ON auth.DesktopDirectReleases([Profile],Channel,Platform,IsActive,PublishedAtUtc);
    IF EXISTS(SELECT 1 FROM (VALUES(N'UX_DesktopDirectReleases_Identity',1,N'Profile,Channel,Platform,Version'),
                                  (N'UX_DesktopDirectReleases_Request',1,N'ActorUserId,ClientRequestId'),
                                  (N'IX_DesktopDirectReleases_Visible',0,N'Profile,Channel,Platform,IsActive,PublishedAtUtc')) e([Name],IsUnique,Keys)
        WHERE NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND i.name=e.[Name]
            AND i.is_unique=e.IsUnique AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.ignore_dup_key=0 AND i.has_filter=0
            AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
                FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=e.Keys))
        THROW 51195,'Unexpected direct catalog index.',1;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'auth.DesktopDirectReleases') AND i.is_primary_key=1 AND i.is_disabled=0
        AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=N'DirectReleaseId')
        THROW 51196,'Unexpected direct catalog primary key.',1;
    DECLARE @desktopRole int=DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole');
    IF @desktopRole IS NULL THROW 51197,'Desktop database role must exist and be visible before granting catalog access to server.',1;
    IF NOT EXISTS(SELECT 1 FROM sys.database_permissions WHERE grantee_principal_id=@desktopRole AND class=3 AND major_id=SCHEMA_ID(N'auth')
        AND state='D' AND permission_name IN ('CONTROL'))
    BEGIN
        IF EXISTS(SELECT 1 FROM (VALUES(N'SELECT'),(N'INSERT'),(N'UPDATE'),(N'DELETE')) p([Name])
            WHERE NOT EXISTS(SELECT 1 FROM sys.database_permissions d WHERE d.grantee_principal_id=@desktopRole AND d.state='D' AND d.minor_id=0
                AND d.permission_name=p.[Name] AND ((d.class=3 AND d.major_id=SCHEMA_ID(N'auth')) OR (d.class=1 AND d.major_id=OBJECT_ID(N'auth.DesktopDirectReleases')))))
            DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::auth.DesktopDirectReleases TO VideoMakerDesktopRole;
    END;
    IF NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.19-direct-desktop-catalog')
        INSERT ai.SchemaVersions([Version],[Description]) VALUES('4.1.19-direct-desktop-catalog',N'Administrator-declared direct catalog; clients verify checksum-named ZIP. Existing inspected protocols remain unchanged.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
