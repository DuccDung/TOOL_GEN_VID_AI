/*
    GPT-Image-2 character reference outputs for VideoMaker 4.0.

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

    IF OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[Characters]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[MediaAssets]', N'U') IS NULL
    BEGIN
        THROW 51020, 'VideoFactory workflow tables are required before GPT-Image-2 migration.', 1;
    END;

    IF COL_LENGTH(N'vf.ProviderRequests', N'CharacterId') IS NULL
        ALTER TABLE [vf].[ProviderRequests] ADD [CharacterId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [name] = N'FK_ProviderRequests_Characters'
    )
        EXEC(N'ALTER TABLE [vf].[ProviderRequests] WITH CHECK
               ADD CONSTRAINT [FK_ProviderRequests_Characters]
               FOREIGN KEY ([CharacterId]) REFERENCES [vf].[Characters] ([CharacterId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [name] = N'IX_ProviderRequests_Character_Created'
    )
        EXEC(N'CREATE INDEX [IX_ProviderRequests_Character_Created]
               ON [vf].[ProviderRequests] ([CharacterId], [CreatedAtUtc] DESC)
               INCLUDE ([ProjectId], [OrganizationId], [RequestKind], [Status]);');

    IF OBJECT_ID(N'[vf].[GeneratedImageOutputs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[GeneratedImageOutputs]
        (
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [Payload] varbinary(max) NOT NULL,
            [MimeType] varchar(150) NOT NULL,
            [Sha256] char(64) NOT NULL,
            [SizeBytes] bigint NOT NULL,
            [Width] int NOT NULL,
            [Height] int NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_GeneratedImageOutputs_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc] datetime2(3) NOT NULL,
            [DownloadedAtUtc] datetime2(3) NULL,
            CONSTRAINT [PK_GeneratedImageOutputs] PRIMARY KEY CLUSTERED ([ProviderRequestId]),
            CONSTRAINT [FK_GeneratedImageOutputs_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]),
            CONSTRAINT [CK_GeneratedImageOutputs_MimeType]
                CHECK ([MimeType] IN ('image/png','image/jpeg')),
            CONSTRAINT [CK_GeneratedImageOutputs_Sha256]
                CHECK ([Sha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([Sha256]) = 64),
            CONSTRAINT [CK_GeneratedImageOutputs_Size]
                CHECK ([SizeBytes] > 0 AND [SizeBytes] <= 10485760 AND DATALENGTH([Payload]) = [SizeBytes]),
            CONSTRAINT [CK_GeneratedImageOutputs_Dimensions]
                CHECK ([Width] > 0 AND [Height] > 0),
            CONSTRAINT [CK_GeneratedImageOutputs_Expiry]
                CHECK ([ExpiresAtUtc] > [CreatedAtUtc])
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[GeneratedImageOutputs]')
          AND [name] = N'IX_GeneratedImageOutputs_ExpiresAtUtc'
    )
        CREATE INDEX [IX_GeneratedImageOutputs_ExpiresAtUtc]
            ON [vf].[GeneratedImageOutputs] ([ExpiresAtUtc]);

    IF COL_LENGTH(N'vf.MediaAssets', N'SourceProviderRequestId') IS NULL
        ALTER TABLE [vf].[MediaAssets] ADD [SourceProviderRequestId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[MediaAssets]')
          AND [name] = N'FK_MediaAssets_SourceProviderRequest'
    )
        EXEC(N'ALTER TABLE [vf].[MediaAssets] WITH CHECK
               ADD CONSTRAINT [FK_MediaAssets_SourceProviderRequest]
               FOREIGN KEY ([SourceProviderRequestId]) REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[MediaAssets]')
          AND [name] = N'UX_MediaAssets_SourceProviderRequest'
    )
        EXEC(N'CREATE UNIQUE INDEX [UX_MediaAssets_SourceProviderRequest]
               ON [vf].[MediaAssets] ([SourceProviderRequestId])
               WHERE [SourceProviderRequestId] IS NOT NULL;');

    IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
    BEGIN
        THROW 51021, 'Organization AI Gateway migration 4.0.0 must run first.', 1;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.0.2-gpt-image-character-reference'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.0.2-gpt-image-character-reference',
            N'GPT-Image-2 character reference generation, protected temporary image payload and provider-request linkage.'
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
    DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedImageOutputs] TO [VideoMakerDesktopRole];
END;
GO

IF COL_LENGTH(N'vf.ProviderRequests', N'CharacterId') IS NULL OR
   OBJECT_ID(N'[vf].[GeneratedImageOutputs]', N'U') IS NULL OR
   COL_LENGTH(N'vf.MediaAssets', N'SourceProviderRequestId') IS NULL
BEGIN
    THROW 51022, 'GPT-Image-2 character reference schema verification failed.', 1;
END;

IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
BEGIN
    THROW 51023, 'GPT-Image-2 schema version table verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.0.2-gpt-image-character-reference'
)
BEGIN
    THROW 51024, 'GPT-Image-2 character reference schema version was not recorded.', 1;
END;

PRINT N'VideoFactory GPT-Image-2 character reference schema 4.0.2 is ready.';
GO
