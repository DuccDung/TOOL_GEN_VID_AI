/* Additive upgrade after 4.1.17. Apply only on the approved database after QA/backup review.
   Protocol 1/2 data and permissions are preserved. Do not rerun 4.1.17 as a verifier after this upgrade. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF @@TRANCOUNT <> 0 THROW 51180, 'Run this migration in its own session without an outer transaction.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'auth.AppReleases', N'U') IS NULL OR OBJECT_ID(N'auth.AppReleaseArtifacts', N'U') IS NULL
        OR OBJECT_ID(N'ai.SchemaVersions', N'U') IS NULL
        THROW 51181, 'Desktop release tables and ai.SchemaVersions are required.', 1;
    IF NOT EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE [Version] = '4.1.17-desktop-external-artifacts')
        THROW 51182, 'Verify and apply 4.1.17 before this migration.', 1;
    IF COL_LENGTH(N'auth.AppReleases', N'FirstPublishedAtUtc') IS NULL OR COL_LENGTH(N'auth.AppReleases', N'Revision') IS NULL
        OR COL_LENGTH(N'auth.AppReleaseArtifacts', N'SourceKind') IS NULL OR COL_LENGTH(N'auth.AppReleaseArtifacts', N'ModelBundleId') IS NULL
        THROW 51183, 'The 4.1.17 schema is incomplete; inspect before proceeding.', 1;

    IF OBJECT_ID(N'auth.DesktopReleasePreparations', N'U') IS NULL
    BEGIN
        CREATE TABLE auth.DesktopReleasePreparations (
            PreparationId uniqueidentifier NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Id DEFAULT NEWID(),
            ActorUserId nvarchar(450) NOT NULL, ClientRequestId uniqueidentifier NOT NULL,
            [Version] varchar(50) NOT NULL, DownloadUrl nvarchar(2000) NOT NULL, ReleaseNotes nvarchar(max) NULL,
            IsMandatory bit NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Mandatory DEFAULT (0),
            [Profile] varchar(50) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Profile DEFAULT ('Production'),
            Channel varchar(20) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Channel DEFAULT ('Stable'),
            Platform varchar(20) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Platform DEFAULT ('win-x64'),
            Revision uniqueidentifier NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Revision DEFAULT NEWID(),
            InputFingerprint char(64) NOT NULL, CreateRequestFingerprint char(64) NOT NULL,
            Generation int NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Generation DEFAULT (1),
            [State] varchar(30) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_State DEFAULT ('Queued'),
            Stage varchar(30) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Stage DEFAULT ('queued'),
            BytesReceived bigint NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Bytes DEFAULT (0), TotalBytes bigint NULL,
            AttemptCount int NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Attempts DEFAULT (0),
            LeaseOwner uniqueidentifier NULL, LeaseExpiresAtUtc datetime2(3) NULL, HeartbeatAtUtc datetime2(3) NULL,
            NextAttemptAtUtc datetime2(3) NULL,
            CancellationRequested bit NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Cancel DEFAULT (0),
            ErrorCode varchar(100) NULL, ErrorMessage nvarchar(1000) NULL, InspectionEvidenceJson nvarchar(max) NULL,
            VerifiedAtUtc datetime2(3) NULL, ReleaseId uniqueidentifier NULL,
            CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Created DEFAULT SYSUTCDATETIME(),
            UpdatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_DesktopReleasePreparations_Updated DEFAULT SYSUTCDATETIME(),
            CONSTRAINT PK_DesktopReleasePreparations PRIMARY KEY (PreparationId)
        );
    END;
    IF OBJECT_ID(N'auth.DesktopBootstrappers', N'U') IS NULL
    BEGIN
        CREATE TABLE auth.DesktopBootstrappers (
            BootstrapperId uniqueidentifier NOT NULL CONSTRAINT DF_DesktopBootstrappers_Id DEFAULT NEWID(),
            [Version] varchar(50) NOT NULL, [Profile] varchar(50) NOT NULL, Platform varchar(20) NOT NULL, Channel varchar(20) NOT NULL,
            ServerBaseUrl nvarchar(2000) NOT NULL, ModelBundleId varchar(100) NOT NULL, DistributionProtocol int NOT NULL,
            FileName nvarchar(180) NOT NULL, RelativePath nvarchar(1000) NOT NULL, SizeBytes bigint NOT NULL, Sha256 char(64) NOT NULL,
            EmbeddedMetadataJson nvarchar(max) NOT NULL,
            IsActive bit NOT NULL CONSTRAINT DF_DesktopBootstrappers_Active DEFAULT (0),
            IsRevoked bit NOT NULL CONSTRAINT DF_DesktopBootstrappers_Revoked DEFAULT (0),
            Revision uniqueidentifier NOT NULL CONSTRAINT DF_DesktopBootstrappers_Revision DEFAULT NEWID(),
            CreatedByUserId nvarchar(450) NOT NULL,
            CreatedAtUtc datetime2(3) NOT NULL CONSTRAINT DF_DesktopBootstrappers_Created DEFAULT SYSUTCDATETIME(),
            ActivatedAtUtc datetime2(3) NULL,
            CONSTRAINT PK_DesktopBootstrappers PRIMARY KEY (BootstrapperId)
        );
    END;
    IF COL_LENGTH(N'auth.AppReleaseArtifacts', N'VerifiedPreparationId') IS NULL
        ALTER TABLE auth.AppReleaseArtifacts ADD VerifiedPreparationId uniqueidentifier NULL;

    DECLARE @columns TABLE (TableName nvarchar(128), ColumnName sysname, TypeName sysname, MaxLength smallint, Nullable bit);
    INSERT @columns VALUES
        (N'auth.DesktopReleasePreparations',N'PreparationId',N'uniqueidentifier',16,0),
        (N'auth.DesktopReleasePreparations',N'ActorUserId',N'nvarchar',900,0),(N'auth.DesktopReleasePreparations',N'ClientRequestId',N'uniqueidentifier',16,0),
        (N'auth.DesktopReleasePreparations',N'Version',N'varchar',50,0),(N'auth.DesktopReleasePreparations',N'DownloadUrl',N'nvarchar',4000,0),
        (N'auth.DesktopReleasePreparations',N'ReleaseNotes',N'nvarchar',-1,1),(N'auth.DesktopReleasePreparations',N'IsMandatory',N'bit',1,0),
        (N'auth.DesktopReleasePreparations',N'Profile',N'varchar',50,0),(N'auth.DesktopReleasePreparations',N'Channel',N'varchar',20,0),
        (N'auth.DesktopReleasePreparations',N'Platform',N'varchar',20,0),(N'auth.DesktopReleasePreparations',N'Revision',N'uniqueidentifier',16,0),
        (N'auth.DesktopReleasePreparations',N'InputFingerprint',N'char',64,0),(N'auth.DesktopReleasePreparations',N'CreateRequestFingerprint',N'char',64,0),(N'auth.DesktopReleasePreparations',N'Generation',N'int',4,0),
        (N'auth.DesktopReleasePreparations',N'State',N'varchar',30,0),(N'auth.DesktopReleasePreparations',N'Stage',N'varchar',30,0),
        (N'auth.DesktopReleasePreparations',N'BytesReceived',N'bigint',8,0),(N'auth.DesktopReleasePreparations',N'TotalBytes',N'bigint',8,1),
        (N'auth.DesktopReleasePreparations',N'AttemptCount',N'int',4,0),(N'auth.DesktopReleasePreparations',N'LeaseOwner',N'uniqueidentifier',16,1),
        (N'auth.DesktopReleasePreparations',N'LeaseExpiresAtUtc',N'datetime2',7,1),(N'auth.DesktopReleasePreparations',N'HeartbeatAtUtc',N'datetime2',7,1),
        (N'auth.DesktopReleasePreparations',N'NextAttemptAtUtc',N'datetime2',7,1),(N'auth.DesktopReleasePreparations',N'CancellationRequested',N'bit',1,0),
        (N'auth.DesktopReleasePreparations',N'ErrorCode',N'varchar',100,1),(N'auth.DesktopReleasePreparations',N'ErrorMessage',N'nvarchar',2000,1),
        (N'auth.DesktopReleasePreparations',N'InspectionEvidenceJson',N'nvarchar',-1,1),(N'auth.DesktopReleasePreparations',N'VerifiedAtUtc',N'datetime2',7,1),
        (N'auth.DesktopReleasePreparations',N'ReleaseId',N'uniqueidentifier',16,1),(N'auth.DesktopReleasePreparations',N'CreatedAtUtc',N'datetime2',7,0),
        (N'auth.DesktopReleasePreparations',N'UpdatedAtUtc',N'datetime2',7,0),
        (N'auth.DesktopBootstrappers',N'BootstrapperId',N'uniqueidentifier',16,0),(N'auth.DesktopBootstrappers',N'Version',N'varchar',50,0),
        (N'auth.DesktopBootstrappers',N'Profile',N'varchar',50,0),(N'auth.DesktopBootstrappers',N'Platform',N'varchar',20,0),(N'auth.DesktopBootstrappers',N'Channel',N'varchar',20,0),
        (N'auth.DesktopBootstrappers',N'ServerBaseUrl',N'nvarchar',4000,0),(N'auth.DesktopBootstrappers',N'ModelBundleId',N'varchar',100,0),
        (N'auth.DesktopBootstrappers',N'DistributionProtocol',N'int',4,0),(N'auth.DesktopBootstrappers',N'FileName',N'nvarchar',360,0),
        (N'auth.DesktopBootstrappers',N'RelativePath',N'nvarchar',2000,0),(N'auth.DesktopBootstrappers',N'SizeBytes',N'bigint',8,0),
        (N'auth.DesktopBootstrappers',N'Sha256',N'char',64,0),(N'auth.DesktopBootstrappers',N'EmbeddedMetadataJson',N'nvarchar',-1,0),
        (N'auth.DesktopBootstrappers',N'IsActive',N'bit',1,0),(N'auth.DesktopBootstrappers',N'IsRevoked',N'bit',1,0),
        (N'auth.DesktopBootstrappers',N'Revision',N'uniqueidentifier',16,0),(N'auth.DesktopBootstrappers',N'CreatedByUserId',N'nvarchar',900,0),
        (N'auth.DesktopBootstrappers',N'CreatedAtUtc',N'datetime2',7,0),(N'auth.DesktopBootstrappers',N'ActivatedAtUtc',N'datetime2',7,1),
        (N'auth.AppReleaseArtifacts',N'VerifiedPreparationId',N'uniqueidentifier',16,1);
    IF EXISTS (SELECT 1 FROM @columns e LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(e.TableName,N'U') AND c.name=e.ColumnName
               WHERE c.column_id IS NULL OR c.system_type_id<>TYPE_ID(e.TypeName) OR c.max_length<>e.MaxLength OR c.is_nullable<>e.Nullable
                   OR (e.TypeName=N'datetime2' AND c.scale<>3))
        THROW 51184, 'Unexpected shared desktop schema; inspect partial tables before continuing.', 1;

    DECLARE @checks TABLE (TableName nvarchar(128), [Name] sysname, OldExpression nvarchar(max) NULL, NewExpression nvarchar(max) NOT NULL);
    INSERT @checks VALUES
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_State',NULL,N'([State] IN (''Queued'',''Downloading'',''Inspecting'',''Ready'',''Failed'',''Cancelled'',''Published''))'),
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_Progress',NULL,N'([Generation]>=1 AND [AttemptCount]>=0 AND [AttemptCount]<=5 AND [BytesReceived]>=0 AND [BytesReceived]<=2147483648 AND ([TotalBytes] IS NULL OR ([TotalBytes]>=0 AND [TotalBytes]<=2147483648)))'),
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_Lease',NULL,N'(([LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL) OR ([LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL AND [State] IN (''Downloading'',''Inspecting'')))'),
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_Proof',NULL,N'(([InspectionEvidenceJson] IS NULL OR ISJSON([InspectionEvidenceJson])=1) AND ([State] NOT IN (''Ready'',''Published'') OR ([VerifiedAtUtc] IS NOT NULL AND [ReleaseId] IS NOT NULL AND [InspectionEvidenceJson] IS NOT NULL)))'),
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_Fingerprint',NULL,N'([InputFingerprint] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([InputFingerprint])=64 AND [CreateRequestFingerprint] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([CreateRequestFingerprint])=64)'),
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_Scope',NULL,N'(LEN([Profile])>0 AND [Channel]=''Stable'' AND [Platform]=''win-x64'')'),
        (N'auth.DesktopReleasePreparations',N'CK_DesktopReleasePreparations_Notes',NULL,N'([ReleaseNotes] IS NULL OR DATALENGTH([ReleaseNotes])<=20000)'),
        (N'auth.DesktopBootstrappers',N'CK_DesktopBootstrappers_Protocol',NULL,N'([DistributionProtocol]=3 AND [Channel]=''Stable'' AND [Platform]=''win-x64'' AND LEN([Profile])>0 AND LEN([ModelBundleId])>0)'),
        (N'auth.DesktopBootstrappers',N'CK_DesktopBootstrappers_File',NULL,N'([SizeBytes]>=2 AND [SizeBytes]<=536870912 AND LEN([FileName])>0 AND LEN([FileName])<=180 AND LEN([RelativePath])>0 AND [Sha256] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([Sha256])=64)'),
        (N'auth.DesktopBootstrappers',N'CK_DesktopBootstrappers_Metadata',NULL,N'(ISJSON([EmbeddedMetadataJson])=1 AND DATALENGTH([EmbeddedMetadataJson])<=65536)'),
        (N'auth.DesktopBootstrappers',N'CK_DesktopBootstrappers_Active',NULL,N'([IsActive]=0 OR [IsRevoked]=0)'),
        (N'auth.AppReleaseArtifacts',N'CK_AppReleaseArtifacts_Protocol',N'(([InstallMode] = ''Legacy'' AND [DistributionProtocol] = 1) OR ([InstallMode] = ''StaticArchive'' AND [DistributionProtocol] = 2))',N'(([InstallMode]=''Legacy'' AND [DistributionProtocol]=1) OR ([InstallMode]=''StaticArchive'' AND [DistributionProtocol] IN (2,3)))'),
        (N'auth.AppReleaseArtifacts',N'CK_AppReleaseArtifacts_Source',N'(([SourceKind] = ''Internal'' AND [RelativePath] IS NOT NULL AND LEN([RelativePath]) > 0 AND [ExternalDownloadUrl] IS NULL) OR ([SourceKind] = ''ExternalVps'' AND [Kind] = ''DesktopPackage'' AND [RelativePath] IS NULL AND [ExternalDownloadUrl] IS NOT NULL AND LEN([ExternalDownloadUrl]) > 0 AND [InstallMode] = ''StaticArchive'' AND [DistributionProtocol] = 2 AND [RegistrationEvidenceJson] IS NOT NULL AND [BoundPackageSha256] IS NOT NULL AND [BoundPackageSha256] = [Sha256] AND [ModelBundleId] IS NOT NULL AND LEN([ModelBundleId]) > 0))',
            N'(([SourceKind]=''Internal'' AND [RelativePath] IS NOT NULL AND LEN([RelativePath])>0 AND [ExternalDownloadUrl] IS NULL) OR ([SourceKind]=''ExternalVps'' AND [Kind]=''DesktopPackage'' AND [RelativePath] IS NULL AND [ExternalDownloadUrl] IS NOT NULL AND LEN([ExternalDownloadUrl])>0 AND [InstallMode]=''StaticArchive'' AND [DistributionProtocol] IN (2,3) AND [RegistrationEvidenceJson] IS NOT NULL AND [BoundPackageSha256] IS NOT NULL AND [BoundPackageSha256]=[Sha256] AND [ModelBundleId] IS NOT NULL AND LEN([ModelBundleId])>0))');
    CREATE TABLE #VMSharedExpected (
        [SourceKind] varchar(20) NOT NULL,[RelativePath] nvarchar(1000) NULL,[ExternalDownloadUrl] nvarchar(2000) NULL,[Kind] varchar(30) NOT NULL,
        [InstallMode] varchar(30) NOT NULL,[DistributionProtocol] int NOT NULL,[RegistrationEvidenceJson] nvarchar(max) NULL,[BoundPackageSha256] char(64) NULL,
        [Sha256] char(64) NOT NULL,[ModelBundleId] varchar(100) NULL,[Profile] varchar(50) NOT NULL,[Channel] varchar(20) NOT NULL,[Platform] varchar(20) NOT NULL,
        [State] varchar(30) NOT NULL,[Generation] int NOT NULL,[AttemptCount] int NOT NULL,[BytesReceived] bigint NOT NULL,[TotalBytes] bigint NULL,
        [LeaseOwner] uniqueidentifier NULL,[LeaseExpiresAtUtc] datetime2(3) NULL,[InspectionEvidenceJson] nvarchar(max) NULL,
        [VerifiedAtUtc] datetime2(3) NULL,[ReleaseId] uniqueidentifier NULL,[InputFingerprint] char(64) NOT NULL,[CreateRequestFingerprint] char(64) NOT NULL,
        [SizeBytes] bigint NOT NULL,[FileName] nvarchar(180) NOT NULL,[EmbeddedMetadataJson] nvarchar(max) NOT NULL,[IsActive] bit NOT NULL,[IsRevoked] bit NOT NULL,[ReleaseNotes] nvarchar(max) NULL
    );
    DECLARE @table nvarchar(128),@name sysname,@old nvarchar(max),@new nvarchar(max),@statement nvarchar(max),@tempName sysname,
            @oldDefinition nvarchar(max),@newDefinition nvarchar(max),@actual nvarchar(max),@message nvarchar(2048);
    DECLARE shared_checks CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,[Name],OldExpression,NewExpression FROM @checks ORDER BY TableName,[Name];
    OPEN shared_checks; FETCH NEXT FROM shared_checks INTO @table,@name,@old,@new;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @tempName=N'CK_VMShared_'+REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N'');
        SET @statement=N'ALTER TABLE #VMSharedExpected WITH CHECK ADD CONSTRAINT '+QUOTENAME(@tempName)+N' CHECK '+@new;
        EXEC sys.sp_executesql @statement;
        SET @newDefinition=NULL;
        SELECT @newDefinition=definition FROM tempdb.sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'tempdb..#VMSharedExpected') AND name=@tempName;
        SET @oldDefinition=NULL;
        IF @old IS NOT NULL
        BEGIN
            SET @tempName=N'CK_VMShared_'+REPLACE(CONVERT(nvarchar(36),NEWID()),N'-',N'');
            SET @statement=N'ALTER TABLE #VMSharedExpected WITH CHECK ADD CONSTRAINT '+QUOTENAME(@tempName)+N' CHECK '+@old;
            EXEC sys.sp_executesql @statement;
            SELECT @oldDefinition=definition FROM tempdb.sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'tempdb..#VMSharedExpected') AND name=@tempName;
        END;
        IF @newDefinition IS NULL OR (@old IS NOT NULL AND @oldDefinition IS NULL) THROW 51185,'Cannot inspect expected shared desktop CHECK definitions.',1;
        SET @actual=NULL;
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(@table) AND name=@name)
        BEGIN
            SELECT @actual=definition FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(@table) AND name=@name
                AND is_disabled=0 AND is_not_trusted=0 AND is_not_for_replication=0;
            IF @actual IS NULL OR (@actual COLLATE Latin1_General_100_BIN2<>@newDefinition COLLATE Latin1_General_100_BIN2
                AND (@oldDefinition IS NULL OR @actual COLLATE Latin1_General_100_BIN2<>@oldDefinition COLLATE Latin1_General_100_BIN2))
            BEGIN SET @message=N'Existing CHECK is disabled, untrusted or unexpected: '+@name; THROW 51186,@message,1; END;
            IF @actual COLLATE Latin1_General_100_BIN2<>@newDefinition COLLATE Latin1_General_100_BIN2
            BEGIN SET @statement=N'ALTER TABLE '+QUOTENAME(PARSENAME(@table,2))+N'.'+QUOTENAME(PARSENAME(@table,1))+N' DROP CONSTRAINT '+QUOTENAME(@name);EXEC sys.sp_executesql @statement; END;
        END;
        IF @actual IS NULL OR @actual COLLATE Latin1_General_100_BIN2<>@newDefinition COLLATE Latin1_General_100_BIN2
        BEGIN SET @statement=N'ALTER TABLE '+QUOTENAME(PARSENAME(@table,2))+N'.'+QUOTENAME(PARSENAME(@table,1))+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(@name)+N' CHECK '+@new;EXEC sys.sp_executesql @statement; END;
        FETCH NEXT FROM shared_checks INTO @table,@name,@old,@new;
    END;
    CLOSE shared_checks; DEALLOCATE shared_checks; DROP TABLE #VMSharedExpected;

    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'auth.DesktopReleasePreparations') AND name=N'FK_DesktopReleasePreparations_Release')
        ALTER TABLE auth.DesktopReleasePreparations WITH CHECK ADD CONSTRAINT FK_DesktopReleasePreparations_Release FOREIGN KEY(ReleaseId) REFERENCES auth.AppReleases(AppReleaseId);
    IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'auth.AppReleaseArtifacts') AND name=N'FK_AppReleaseArtifacts_VerifiedPreparation')
        ALTER TABLE auth.AppReleaseArtifacts WITH CHECK ADD CONSTRAINT FK_AppReleaseArtifacts_VerifiedPreparation FOREIGN KEY(VerifiedPreparationId) REFERENCES auth.DesktopReleasePreparations(PreparationId);
    IF EXISTS (SELECT 1 FROM (VALUES(N'auth.DesktopReleasePreparations',N'FK_DesktopReleasePreparations_Release',N'ReleaseId',N'auth.AppReleases',N'AppReleaseId'),
                                  (N'auth.AppReleaseArtifacts',N'FK_AppReleaseArtifacts_VerifiedPreparation',N'VerifiedPreparationId',N'auth.DesktopReleasePreparations',N'PreparationId')) e(TableName,FKName,ColumnName,ReferenceTable,ReferenceColumn)
        WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            WHERE f.parent_object_id=OBJECT_ID(e.TableName) AND f.name=e.FKName AND f.referenced_object_id=OBJECT_ID(e.ReferenceTable)
              AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0 AND f.delete_referential_action=0 AND f.update_referential_action=0
              AND COL_NAME(fc.parent_object_id,fc.parent_column_id)=e.ColumnName AND COL_NAME(fc.referenced_object_id,fc.referenced_column_id)=e.ReferenceColumn
              AND (SELECT COUNT(*) FROM sys.foreign_key_columns fcc WHERE fcc.constraint_object_id=f.object_id)=1))
        THROW 51187,'Unexpected shared desktop foreign key.',1;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopReleasePreparations') AND name=N'UX_DesktopReleasePreparations_Request')
        CREATE UNIQUE INDEX UX_DesktopReleasePreparations_Request ON auth.DesktopReleasePreparations(ActorUserId,ClientRequestId);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopReleasePreparations') AND name=N'UX_DesktopReleasePreparations_Release')
        CREATE UNIQUE INDEX UX_DesktopReleasePreparations_Release ON auth.DesktopReleasePreparations(ReleaseId) WHERE ReleaseId IS NOT NULL;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopReleasePreparations') AND name=N'IX_DesktopReleasePreparations_Queue')
        CREATE INDEX IX_DesktopReleasePreparations_Queue ON auth.DesktopReleasePreparations([State],NextAttemptAtUtc,LeaseExpiresAtUtc);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopBootstrappers') AND name=N'UX_DesktopBootstrappers_Identity')
        CREATE UNIQUE INDEX UX_DesktopBootstrappers_Identity ON auth.DesktopBootstrappers([Profile],Platform,Channel,[Version]);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auth.DesktopBootstrappers') AND name=N'UX_DesktopBootstrappers_Active')
        CREATE UNIQUE INDEX UX_DesktopBootstrappers_Active ON auth.DesktopBootstrappers([Profile],Platform,Channel) WHERE IsActive=1;
    DECLARE @indexes TABLE(TableName nvarchar(128),IndexName sysname,IsUnique bit,Keys nvarchar(200),FilterText nvarchar(100) NULL);
    INSERT @indexes VALUES
        (N'auth.DesktopReleasePreparations',N'UX_DesktopReleasePreparations_Request',1,N'ActorUserId,ClientRequestId',NULL),
        (N'auth.DesktopReleasePreparations',N'UX_DesktopReleasePreparations_Release',1,N'ReleaseId',N'([ReleaseId] IS NOT NULL)'),
        (N'auth.DesktopReleasePreparations',N'IX_DesktopReleasePreparations_Queue',0,N'State,NextAttemptAtUtc,LeaseExpiresAtUtc',NULL),
        (N'auth.DesktopBootstrappers',N'UX_DesktopBootstrappers_Identity',1,N'Profile,Platform,Channel,Version',NULL),
        (N'auth.DesktopBootstrappers',N'UX_DesktopBootstrappers_Active',1,N'Profile,Platform,Channel',N'([IsActive]=(1))');
    IF EXISTS (SELECT 1 FROM @indexes e WHERE NOT EXISTS(SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(e.TableName) AND i.name=e.IndexName
        AND i.is_unique=e.IsUnique AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.ignore_dup_key=0
        AND ((e.FilterText IS NULL AND i.has_filter=0) OR (e.FilterText IS NOT NULL AND i.has_filter=1 AND i.filter_definition COLLATE Latin1_General_100_BIN2=e.FilterText COLLATE Latin1_General_100_BIN2))
        AND (SELECT STRING_AGG(CONVERT(nvarchar(max),c.name),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
             FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
             WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=e.Keys))
        THROW 51188,'Unexpected shared desktop index shape or filter.',1;
    IF EXISTS (SELECT 1 FROM (VALUES(N'auth.DesktopReleasePreparations',N'PreparationId'),(N'auth.DesktopBootstrappers',N'BootstrapperId')) e(TableName,ColumnName)
        WHERE NOT EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            WHERE i.object_id=OBJECT_ID(e.TableName) AND i.is_primary_key=1 AND i.is_unique=1 AND i.is_disabled=0
                AND ic.key_ordinal=1 AND COL_NAME(ic.object_id,ic.column_id)=e.ColumnName
                AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.key_ordinal>0)=1))
        THROW 51189,'Unexpected shared desktop primary key.',1;
    IF NOT EXISTS(SELECT 1 FROM ai.SchemaVersions WHERE [Version]='4.1.18-shared-desktop-bootstrapper')
        INSERT ai.SchemaVersions([Version],[Description]) VALUES('4.1.18-shared-desktop-bootstrapper',N'Durable release preparation, verified external packages and immutable system-wide desktop bootstrappers; preserve protocol 1/2.');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
