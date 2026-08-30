/*
    Multi-provider video generation foundation for VideoMaker 4.0.4.

    Adds organization-managed video policy, immutable project snapshots,
    BytePlus Seedance catalog rows (disabled and without pricing), and protected
    server-side metadata for temporary video output files.

    This migration is intentionally idempotent. Run only after a verified backup
    and after VideoFactory.4.0.0.OrganizationAiGateway.sql.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[Projects]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[Providers]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderModels]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[Organizations]', N'U') IS NULL OR
       OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51040, 'VideoFactory 4.0 organization gateway and workflow tables are required before BytePlus Seedance migration.', 1;
    END;

    /* Catalog rows are deliberately disabled. Global Admin must verify current
       capabilities, configure rates and explicitly enable them before use. */
    DECLARE @BytePlusProviderId uniqueidentifier;
    SELECT @BytePlusProviderId = [ProviderId]
    FROM [vf].[Providers]
    WHERE [ProviderCode] = 'byteplus';

    IF @BytePlusProviderId IS NULL
    BEGIN
        SET @BytePlusProviderId = NEWID();
        INSERT INTO [vf].[Providers]
        (
            [ProviderId], [ProviderCode], [DisplayName], [BaseUrl], [IsEnabled],
            [CapabilitiesJson], [CreatedAtUtc], [UpdatedAtUtc]
        )
        VALUES
        (
            @BytePlusProviderId,
            'byteplus',
            N'BytePlus ModelArk',
            'https://ark.ap-southeast.bytepluses.com/api/v3/',
            0,
            N'{"videoGeneration":true,"asyncTasks":true,"nativeAudio":true}',
            SYSUTCDATETIME(),
            SYSUTCDATETIME()
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [vf].[ProviderModels]
        WHERE [ProviderId] = @BytePlusProviderId
          AND [ModelCode] = N'dreamina-seedance-2-0-260128'
          AND [Modality] = 'Video'
    )
        INSERT INTO [vf].[ProviderModels]
        (
            [ProviderModelId], [ProviderId], [ModelCode], [DisplayName], [Modality],
            [IsEnabled], [IsDefault], [CapabilitiesJson], [CreatedAtUtc], [UpdatedAtUtc]
        )
        VALUES
        (
            NEWID(), @BytePlusProviderId,
            N'dreamina-seedance-2-0-260128', N'Dreamina Seedance 2.0', 'Video',
            0, 0,
            N'{"endpoint":"contents/generations/tasks","durations":[4,5,6,7,8,9,10,11,12,13,14,15],"minDurationSeconds":4,"maxDurationSeconds":15,"resolutions":["720p"],"aspectRatios":["16:9","9:16","1:1"],"framesPerSecond":24,"nativeAudio":true,"billingUsageType":"OutputToken","billingUnit":"MillionTokens","referenceImage":true}',
            SYSUTCDATETIME(), SYSUTCDATETIME()
        );

    IF NOT EXISTS
    (
        SELECT 1 FROM [vf].[ProviderModels]
        WHERE [ProviderId] = @BytePlusProviderId
          AND [ModelCode] = N'dreamina-seedance-2-5-260628'
          AND [Modality] = 'Video'
    )
        INSERT INTO [vf].[ProviderModels]
        (
            [ProviderModelId], [ProviderId], [ModelCode], [DisplayName], [Modality],
            [IsEnabled], [IsDefault], [CapabilitiesJson], [CreatedAtUtc], [UpdatedAtUtc]
        )
        VALUES
        (
            NEWID(), @BytePlusProviderId,
            N'dreamina-seedance-2-5-260628', N'Dreamina Seedance 2.5', 'Video',
            0, 0,
            N'{"endpoint":"contents/generations/tasks","durations":[4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30],"minDurationSeconds":4,"maxDurationSeconds":30,"resolutions":["720p"],"aspectRatios":["16:9","9:16","1:1"],"framesPerSecond":24,"nativeAudio":true,"billingUsageType":"OutputToken","billingUnit":"MillionTokens","referenceImage":true}',
            SYSUTCDATETIME(), SYSUTCDATETIME()
        );

    IF COL_LENGTH(N'vf.Projects', N'VideoProviderCode') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VideoProviderCode] varchar(80) NULL;

    IF COL_LENGTH(N'vf.Projects', N'VideoModelCode') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VideoModelCode] nvarchar(200) NULL;

    IF COL_LENGTH(N'vf.Projects', N'VideoPolicyVersion') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VideoPolicyVersion] int NULL;

    IF COL_LENGTH(N'vf.Projects', N'VideoResolution') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VideoResolution] varchar(20) NULL;

    IF COL_LENGTH(N'vf.Projects', N'VideoNativeAudio') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VideoNativeAudio] bit NULL;

    IF COL_LENGTH(N'vf.Projects', N'VideoSnapshotAtUtc') IS NULL
        ALTER TABLE [vf].[Projects] ADD [VideoSnapshotAtUtc] datetime2(3) NULL;

    /* Every project that existed before this migration belongs to the Kling
       workflow. Backfill the immutable snapshot without depending on a rate. */
    /* Dynamic SQL is required here because SQL Server compiles the whole batch
       before executing the ALTER TABLE statements above. */
    EXEC(N'UPDATE [vf].[Projects]
           SET [VideoProviderCode] = ''kling'',
               [VideoModelCode] = N''kling-3.0'',
               [VideoPolicyVersion] = 1,
               [VideoResolution] = ''720p'',
               [VideoNativeAudio] = 1,
               [VideoSnapshotAtUtc] = COALESCE([UpdatedAtUtc], SYSUTCDATETIME())
           WHERE [VideoProviderCode] IS NULL
             AND [VideoModelCode] IS NULL
             AND [VideoPolicyVersion] IS NULL
             AND [VideoResolution] IS NULL
             AND [VideoNativeAudio] IS NULL
             AND [VideoSnapshotAtUtc] IS NULL;');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'CK_Projects_VideoSnapshot'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [CK_Projects_VideoSnapshot]
               CHECK
               (
                   ([VideoProviderCode] IS NULL AND [VideoModelCode] IS NULL AND
                    [VideoPolicyVersion] IS NULL AND [VideoResolution] IS NULL AND
                    [VideoNativeAudio] IS NULL AND [VideoSnapshotAtUtc] IS NULL)
                   OR
                   ([VideoProviderCode] IS NOT NULL AND [VideoModelCode] IS NOT NULL AND
                    [VideoPolicyVersion] > 0 AND [VideoResolution] IS NOT NULL AND
                    [VideoNativeAudio] IS NOT NULL AND [VideoSnapshotAtUtc] IS NOT NULL)
               );');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'CK_Projects_VideoResolution'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [CK_Projects_VideoResolution]
               CHECK ([VideoResolution] IS NULL OR [VideoResolution] IN (''480p'',''720p'',''1080p'',''4k''));');

    IF OBJECT_ID(N'[ai].[OrganizationVideoPolicies]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ai].[OrganizationVideoPolicies]
        (
            [OrganizationId] uniqueidentifier NOT NULL,
            [ProviderId] uniqueidentifier NOT NULL,
            [ProviderModelId] uniqueidentifier NOT NULL,
            [PolicyVersion] int NOT NULL,
            [Resolution] varchar(20) NOT NULL,
            [NativeAudio] bit NOT NULL,
            [IsActive] bit NOT NULL
                CONSTRAINT [DF_OrganizationVideoPolicies_IsActive] DEFAULT (1),
            [UpdatedByUserId] nvarchar(450) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationVideoPolicies_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_OrganizationVideoPolicies_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_OrganizationVideoPolicies] PRIMARY KEY CLUSTERED ([OrganizationId]),
            CONSTRAINT [FK_OrganizationVideoPolicies_Organizations]
                FOREIGN KEY ([OrganizationId]) REFERENCES [ai].[Organizations] ([OrganizationId]),
            CONSTRAINT [FK_OrganizationVideoPolicies_Providers]
                FOREIGN KEY ([ProviderId]) REFERENCES [vf].[Providers] ([ProviderId]),
            CONSTRAINT [FK_OrganizationVideoPolicies_ProviderModels]
                FOREIGN KEY ([ProviderModelId]) REFERENCES [vf].[ProviderModels] ([ProviderModelId]),
            CONSTRAINT [CK_OrganizationVideoPolicies_Version]
                CHECK ([PolicyVersion] > 0),
            CONSTRAINT [CK_OrganizationVideoPolicies_Resolution]
                CHECK ([Resolution] IN ('480p','720p','1080p','4k'))
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[ai].[OrganizationVideoPolicies]')
          AND [name] = N'IX_OrganizationVideoPolicies_Provider_Model_Active'
    )
        CREATE INDEX [IX_OrganizationVideoPolicies_Provider_Model_Active]
            ON [ai].[OrganizationVideoPolicies] ([ProviderId], [ProviderModelId], [IsActive]);

    /* Preserve current behavior for organizations that already existed. New
       organizations receive no implicit policy and must be configured explicitly. */
    DECLARE @KlingProviderId uniqueidentifier;
    DECLARE @KlingModelId uniqueidentifier;
    SELECT @KlingProviderId = [ProviderId]
    FROM [vf].[Providers]
    WHERE [ProviderCode] = 'kling';
    SELECT @KlingModelId = [ProviderModelId]
    FROM [vf].[ProviderModels]
    WHERE [ProviderId] = @KlingProviderId
      AND [ModelCode] = N'kling-3.0'
      AND [Modality] = 'Video';

    IF @KlingProviderId IS NOT NULL AND @KlingModelId IS NOT NULL
        INSERT INTO [ai].[OrganizationVideoPolicies]
        (
            [OrganizationId], [ProviderId], [ProviderModelId], [PolicyVersion],
            [Resolution], [NativeAudio], [IsActive], [UpdatedByUserId],
            [CreatedAtUtc], [UpdatedAtUtc]
        )
        SELECT organization.[OrganizationId],
               @KlingProviderId,
               @KlingModelId,
               1,
               '720p',
               1,
               1,
               organization.[CreatedByUserId],
               SYSUTCDATETIME(),
               SYSUTCDATETIME()
        FROM [ai].[Organizations] AS organization
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM [ai].[OrganizationVideoPolicies] AS policy
            WHERE policy.[OrganizationId] = organization.[OrganizationId]
        );

    IF OBJECT_ID(N'[vf].[GeneratedVideoOutputs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[GeneratedVideoOutputs]
        (
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [StorageKey] nvarchar(500) NOT NULL,
            [MimeType] varchar(150) NOT NULL,
            [Sha256] char(64) NOT NULL,
            [SizeBytes] bigint NOT NULL,
            [Status] varchar(20) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_GeneratedVideoOutputs_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [DownloadedAtUtc] datetime2(3) NULL,
            [DeletedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_GeneratedVideoOutputs] PRIMARY KEY CLUSTERED ([ProviderRequestId]),
            CONSTRAINT [FK_GeneratedVideoOutputs_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]),
            CONSTRAINT [UQ_GeneratedVideoOutputs_StorageKey] UNIQUE ([StorageKey]),
            CONSTRAINT [CK_GeneratedVideoOutputs_MimeType]
                CHECK ([MimeType] LIKE 'video/%' OR [MimeType] = 'application/octet-stream'),
            CONSTRAINT [CK_GeneratedVideoOutputs_Sha256]
                CHECK ([Sha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([Sha256]) = 64),
            CONSTRAINT [CK_GeneratedVideoOutputs_Size]
                CHECK ([SizeBytes] > 0 AND [SizeBytes] <= 1073741824),
            CONSTRAINT [CK_GeneratedVideoOutputs_Status]
                CHECK ([Status] IN ('Ready','Deleted','Failed')),
            CONSTRAINT [CK_GeneratedVideoOutputs_Expiry]
                CHECK ([ExpiresAtUtc] > [CreatedAtUtc])
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[GeneratedVideoOutputs]')
          AND [name] = N'IX_GeneratedVideoOutputs_Status_ExpiresAtUtc'
    )
        CREATE INDEX [IX_GeneratedVideoOutputs_Status_ExpiresAtUtc]
            ON [vf].[GeneratedVideoOutputs] ([Status], [ExpiresAtUtc]);

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.4-byteplus-seedance'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.4-byteplus-seedance',
            N'Organization video policy, immutable project provider snapshot, disabled BytePlus Seedance catalog and protected temporary video outputs.'
        );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
BEGIN
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[ai].[OrganizationVideoPolicies] TO [VideoMakerDesktopRole];
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedVideoOutputs] TO [VideoMakerDesktopRole];
END;
GO

IF COL_LENGTH(N'vf.Projects', N'VideoProviderCode') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'VideoModelCode') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'VideoPolicyVersion') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'VideoResolution') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'VideoNativeAudio') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'VideoSnapshotAtUtc') IS NULL OR
   OBJECT_ID(N'[ai].[OrganizationVideoPolicies]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[GeneratedVideoOutputs]', N'U') IS NULL
BEGIN
    THROW 51041, 'BytePlus Seedance schema verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1 FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.0.4-byteplus-seedance'
)
BEGIN
    THROW 51042, 'BytePlus Seedance schema version was not recorded.', 1;
END;

PRINT N'VideoFactory multi-provider video schema 4.0.4 is ready. BytePlus remains disabled until credential, pricing and policy are configured.';
GO
