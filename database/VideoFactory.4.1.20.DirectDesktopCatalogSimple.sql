/* Simple alternative to 4.1.19: catalog schema only; existing data and permissions stay unchanged.
   Run in the approved application database after backup/restore review and complete 4.1.18.
   The 4.1.19 marker below confirms schema compatibility, not permission verification. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF @@TRANCOUNT <> 0 THROW 51220, 'Run this migration in its own session.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'ai.SchemaVersions',N'U') IS NULL OR OBJECT_ID(N'auth.DesktopBootstrappers',N'U') IS NULL
        OR NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.18-shared-desktop-bootstrapper')
        THROW 51221, 'Verify and apply the complete 4.1.18 schema first.', 1;
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
            CONSTRAINT PK_DesktopDirectReleases PRIMARY KEY(DirectReleaseId),
            CONSTRAINT CK_DesktopDirectReleases_Scope CHECK ([CatalogProtocol]=4 AND [ApplicationDistributionProtocol]=3 AND [Channel]='Stable' AND [Platform]='win-x64' AND LEN([Profile])>0 AND LEN([ModelBundleId])>0),
            CONSTRAINT CK_DesktopDirectReleases_Identity CHECK (LEN([Version])>0 AND [DeclaredBuildNumber]>0 AND LEN([DownloadUrl])>0 AND LEN([FileName])>0 AND LEN([MinimumBootstrapperVersion])>0 AND LEN([ActorUserId])>0),
            CONSTRAINT CK_DesktopDirectReleases_Hashes CHECK ([ExpectedSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([ExpectedSha256])=64 AND [CreateRequestFingerprint] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([CreateRequestFingerprint])=64),
            CONSTRAINT CK_DesktopDirectReleases_Publication CHECK (([FirstPublishedAtUtc] IS NULL AND [PublishedAtUtc] IS NULL AND [IsActive]=0) OR ([FirstPublishedAtUtc] IS NOT NULL AND [PublishedAtUtc] IS NOT NULL)),
            CONSTRAINT CK_DesktopDirectReleases_Notes CHECK ([ReleaseNotes] IS NULL OR DATALENGTH([ReleaseNotes])<=20000)
        );
    END;
    DECLARE @object int=OBJECT_ID(N'auth.DesktopDirectReleases');
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
    IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=@object)<>24 OR EXISTS(
        SELECT 1 FROM @columns e LEFT JOIN sys.columns c ON c.object_id=@object AND c.name=e.ColumnName
        WHERE c.column_id IS NULL OR c.system_type_id<>TYPE_ID(e.TypeName) OR c.max_length<>e.MaxLength OR c.is_nullable<>e.Nullable
            OR c.is_computed=1 OR c.is_identity=1 OR (e.TypeName=N'datetime2' AND c.scale<>3))
        THROW 51222, 'Unexpected direct catalog columns; inspect partial schema.', 1;
    -- SQL Server stores CHECK expressions in this canonical form; reject a changed existing definition.
    DECLARE @checks TABLE([Name] sysname, Definition nvarchar(2000));
    INSERT @checks VALUES
        (N'CK_DesktopDirectReleases_Scope',N'([CatalogProtocol]=(4) AND [ApplicationDistributionProtocol]=(3) AND [Channel]=''Stable'' AND [Platform]=''win-x64'' AND len([Profile])>(0) AND len([ModelBundleId])>(0))'),
        (N'CK_DesktopDirectReleases_Identity',N'(len([Version])>(0) AND [DeclaredBuildNumber]>(0) AND len([DownloadUrl])>(0) AND len([FileName])>(0) AND len([MinimumBootstrapperVersion])>(0) AND len([ActorUserId])>(0))'),
        (N'CK_DesktopDirectReleases_Hashes',N'(NOT [ExpectedSha256] like ''%[^0-9A-Fa-f]%'' AND len([ExpectedSha256])=(64) AND NOT [CreateRequestFingerprint] like ''%[^0-9A-Fa-f]%'' AND len([CreateRequestFingerprint])=(64))'),
        (N'CK_DesktopDirectReleases_Publication',N'([FirstPublishedAtUtc] IS NULL AND [PublishedAtUtc] IS NULL AND [IsActive]=(0) OR [FirstPublishedAtUtc] IS NOT NULL AND [PublishedAtUtc] IS NOT NULL)'),
        (N'CK_DesktopDirectReleases_Notes',N'([ReleaseNotes] IS NULL OR datalength([ReleaseNotes])<=(20000))');
    IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=@object)<>5 OR EXISTS(
        SELECT 1 FROM @checks e LEFT JOIN sys.check_constraints c ON c.parent_object_id=@object AND c.name=e.[Name]
        WHERE c.object_id IS NULL OR c.is_disabled=1 OR c.is_not_trusted=1 OR c.is_not_for_replication=1
            OR c.definition COLLATE Latin1_General_100_BIN2<>e.Definition COLLATE Latin1_General_100_BIN2)
        THROW 51223, 'Unexpected or untrusted direct catalog CHECK.', 1;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@object AND name=N'UX_DesktopDirectReleases_Identity')
        CREATE UNIQUE INDEX UX_DesktopDirectReleases_Identity ON auth.DesktopDirectReleases([Profile],Channel,Platform,[Version]);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@object AND name=N'UX_DesktopDirectReleases_Request')
        CREATE UNIQUE INDEX UX_DesktopDirectReleases_Request ON auth.DesktopDirectReleases(ActorUserId,ClientRequestId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=@object AND name=N'IX_DesktopDirectReleases_Visible')
        CREATE INDEX IX_DesktopDirectReleases_Visible ON auth.DesktopDirectReleases([Profile],Channel,Platform,IsActive,PublishedAtUtc);
    IF EXISTS(SELECT 1 FROM (VALUES
        (N'PK_DesktopDirectReleases',1,1,N'DirectReleaseId'),
        (N'UX_DesktopDirectReleases_Identity',1,0,N'Profile,Channel,Platform,Version'),
        (N'UX_DesktopDirectReleases_Request',1,0,N'ActorUserId,ClientRequestId'),
        (N'IX_DesktopDirectReleases_Visible',0,0,N'Profile,Channel,Platform,IsActive,PublishedAtUtc')) e([Name],IsUnique,IsPrimary,Keys)
        WHERE NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=@object AND i.name=e.[Name]
            AND i.is_unique=e.IsUnique AND i.is_primary_key=e.IsPrimary AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.ignore_dup_key=0 AND i.has_filter=0
            AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
                FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=e.Keys))
        THROW 51224, 'Unexpected direct catalog index.', 1;
    IF NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.19-direct-desktop-catalog')
        INSERT ai.SchemaVersions([Version],[Description]) VALUES('4.1.19-direct-desktop-catalog',N'Direct catalog schema compatibility via 4.1.20; existing database permissions unchanged, not verified.');
    IF NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.20-direct-desktop-catalog-simple')
        INSERT ai.SchemaVersions([Version],[Description]) VALUES('4.1.20-direct-desktop-catalog-simple',N'Simple direct catalog schema initialization; existing releases, data and database permissions preserved.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
