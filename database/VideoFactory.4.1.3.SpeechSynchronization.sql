/*
    Speech synchronization foundation for long-form VideoMaker projects.

    This migration is intentionally idempotent. Run only after a verified backup
    and after VideoFactory.4.1.2.ProviderRequestFailureDetails.sql.
    It does not enable transcription pricing or perform provider calls.
*/

USE [VideoFactory];
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'[vf].[Projects]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[Characters]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[Scenes]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[VoiceGenerations]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL OR
       OBJECT_ID(N'[vf].[MediaAssets]', N'U') IS NULL
    BEGIN
        THROW 51130, 'VideoFactory workflow tables are required before speech synchronization migration.', 1;
    END;

    IF COL_LENGTH(N'vf.Projects', N'SpeechProductionPolicy') IS NULL
        ALTER TABLE [vf].[Projects] ADD
            [SpeechProductionPolicy] varchar(40) NOT NULL
                CONSTRAINT [DF_Projects_SpeechProductionPolicy]
                DEFAULT ('ProviderNativeVerified') WITH VALUES;

    IF COL_LENGTH(N'vf.Projects', N'ApprovedNarratorVoiceProfileVersionId') IS NULL
        ALTER TABLE [vf].[Projects] ADD [ApprovedNarratorVoiceProfileVersionId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'CK_Projects_SpeechProductionPolicy'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [CK_Projects_SpeechProductionPolicy]
               CHECK ([SpeechProductionPolicy] IN (''ProviderNativeVerified'',''CanonicalVoice''));');

    IF COL_LENGTH(N'vf.Characters', N'VoiceCode') IS NULL
        ALTER TABLE [vf].[Characters] ADD [VoiceCode] nvarchar(100) NULL;

    IF COL_LENGTH(N'vf.Characters', N'VoiceSpeakingRate') IS NULL
        ALTER TABLE [vf].[Characters] ADD [VoiceSpeakingRate] decimal(6,3) NULL;

    IF COL_LENGTH(N'vf.Characters', N'ApprovedVoiceProfileVersionId') IS NULL
        ALTER TABLE [vf].[Characters] ADD [ApprovedVoiceProfileVersionId] uniqueidentifier NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Characters]')
          AND [name] = N'CK_Characters_VoiceSpeakingRate'
    )
        EXEC(N'ALTER TABLE [vf].[Characters] WITH CHECK
               ADD CONSTRAINT [CK_Characters_VoiceSpeakingRate]
               CHECK ([VoiceSpeakingRate] IS NULL OR [VoiceSpeakingRate] BETWEEN 0.5 AND 2.0);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Characters]')
          AND [name] = N'CK_Characters_VoiceSettingsPair'
    )
        EXEC(N'ALTER TABLE [vf].[Characters] WITH CHECK
               ADD CONSTRAINT [CK_Characters_VoiceSettingsPair]
               CHECK (([VoiceCode] IS NULL AND [VoiceSpeakingRate] IS NULL) OR
                       ([VoiceCode] IS NOT NULL AND [VoiceSpeakingRate] IS NOT NULL));');

    IF OBJECT_ID(N'[vf].[VoiceProfiles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[VoiceProfiles]
        (
            [VoiceProfileId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_VoiceProfiles_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId] uniqueidentifier NOT NULL,
            [Scope] varchar(30) NOT NULL,
            [CharacterId] uniqueidentifier NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_VoiceProfiles_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_VoiceProfiles] PRIMARY KEY CLUSTERED ([VoiceProfileId]),
            CONSTRAINT [FK_VoiceProfiles_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects] ([ProjectId]),
            CONSTRAINT [FK_VoiceProfiles_Characters]
                FOREIGN KEY ([CharacterId]) REFERENCES [vf].[Characters] ([CharacterId]),
            CONSTRAINT [CK_VoiceProfiles_Scope]
                CHECK (([Scope] = 'ProjectNarrator' AND [CharacterId] IS NULL) OR
                       ([Scope] = 'Character' AND [CharacterId] IS NOT NULL))
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceProfiles]')
          AND [name] = N'UX_VoiceProfiles_ProjectNarrator'
    )
        CREATE UNIQUE INDEX [UX_VoiceProfiles_ProjectNarrator]
            ON [vf].[VoiceProfiles] ([ProjectId])
            WHERE [Scope] = 'ProjectNarrator';

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceProfiles]')
          AND [name] = N'UX_VoiceProfiles_Character'
    )
        CREATE UNIQUE INDEX [UX_VoiceProfiles_Character]
            ON [vf].[VoiceProfiles] ([ProjectId], [CharacterId])
            WHERE [Scope] = 'Character' AND [CharacterId] IS NOT NULL;

    IF OBJECT_ID(N'[vf].[VoiceProfileVersions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[VoiceProfileVersions]
        (
            [VoiceProfileVersionId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_VoiceProfileVersions_Id] DEFAULT NEWSEQUENTIALID(),
            [VoiceProfileId] uniqueidentifier NOT NULL,
            [Version] int NOT NULL,
            [ProviderCode] varchar(80) NOT NULL,
            [ModelCode] nvarchar(200) NOT NULL,
            [VoiceCode] nvarchar(100) NOT NULL,
            [ProviderVoiceCode] nvarchar(100) NOT NULL,
            [LanguageCode] varchar(10) NOT NULL,
            [SpeakingRate] decimal(6,3) NOT NULL,
            [VoiceInstructions] nvarchar(2000) NOT NULL,
            [SnapshotHash] char(64) NOT NULL,
            [Status] varchar(20) NOT NULL,
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_VoiceProfileVersions_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc] datetime2(3) NULL,
            [SupersededAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_VoiceProfileVersions] PRIMARY KEY CLUSTERED ([VoiceProfileVersionId]),
            CONSTRAINT [FK_VoiceProfileVersions_VoiceProfiles]
                FOREIGN KEY ([VoiceProfileId]) REFERENCES [vf].[VoiceProfiles] ([VoiceProfileId]),
            CONSTRAINT [UQ_VoiceProfileVersions_Profile_Version]
                UNIQUE ([VoiceProfileId], [Version]),
            CONSTRAINT [CK_VoiceProfileVersions_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_VoiceProfileVersions_Rate] CHECK ([SpeakingRate] BETWEEN 0.5 AND 2.0),
            CONSTRAINT [CK_VoiceProfileVersions_SnapshotHash]
                CHECK ([SnapshotHash] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([SnapshotHash]) = 64),
            CONSTRAINT [CK_VoiceProfileVersions_Status]
                CHECK ([Status] IN ('Draft','Approved','Superseded','Revoked')),
            CONSTRAINT [CK_VoiceProfileVersions_Approval]
                CHECK (([Status] = 'Approved' AND [ApprovedAtUtc] IS NOT NULL AND [SupersededAtUtc] IS NULL) OR
                       ([Status] IN ('Draft','Revoked') AND [ApprovedAtUtc] IS NULL AND [SupersededAtUtc] IS NULL) OR
                       ([Status] = 'Superseded' AND [ApprovedAtUtc] IS NOT NULL AND [SupersededAtUtc] IS NOT NULL))
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceProfileVersions]')
          AND [name] = N'UX_VoiceProfileVersions_Approved'
    )
        CREATE UNIQUE INDEX [UX_VoiceProfileVersions_Approved]
            ON [vf].[VoiceProfileVersions] ([VoiceProfileId])
            WHERE [Status] = 'Approved';

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[VoiceProfileVersions]')
          AND [name] = N'IX_VoiceProfileVersions_SnapshotHash'
    )
        CREATE INDEX [IX_VoiceProfileVersions_SnapshotHash]
            ON [vf].[VoiceProfileVersions] ([SnapshotHash]);

    IF COL_LENGTH(N'vf.VoiceGenerations', N'VoiceSnapshotHash') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [VoiceSnapshotHash] char(64) NULL;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'VoiceProfileVersionId') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [VoiceProfileVersionId] uniqueidentifier NULL;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'VerificationStatus') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD
            [VerificationStatus] varchar(30) NOT NULL
                CONSTRAINT [DF_VoiceGenerations_VerificationStatus]
                DEFAULT ('NotRequested') WITH VALUES;

    IF COL_LENGTH(N'vf.VoiceGenerations', N'ApprovedAtUtc') IS NULL
        ALTER TABLE [vf].[VoiceGenerations] ADD [ApprovedAtUtc] datetime2(3) NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'CK_VoiceGenerations_VoiceSnapshotHash'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [CK_VoiceGenerations_VoiceSnapshotHash]
               CHECK ([VoiceSnapshotHash] IS NULL OR
                     ([VoiceSnapshotHash] NOT LIKE ''%[^0-9A-Fa-f]%'' AND LEN([VoiceSnapshotHash]) = 64));');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'CK_VoiceGenerations_VerificationStatus'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [CK_VoiceGenerations_VerificationStatus]
               CHECK ([VerificationStatus] IN
               (''NotRequested'',''Pending'',''Processing'',''Passed'',''NeedsReview'',''Failed''));');

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'CK_VoiceGenerations_Status'
    )
        ALTER TABLE [vf].[VoiceGenerations] DROP CONSTRAINT [CK_VoiceGenerations_Status];

    ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
        ADD CONSTRAINT [CK_VoiceGenerations_Status]
        CHECK ([Status] IN
        ('Pending','Submitting','Generating','Completed','Approved','Superseded','Failed','Cancelled'));

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[VoiceGenerations]')
          AND [name] = N'FK_VoiceGenerations_VoiceProfileVersion'
    )
        EXEC(N'ALTER TABLE [vf].[VoiceGenerations] WITH CHECK
               ADD CONSTRAINT [FK_VoiceGenerations_VoiceProfileVersion]
               FOREIGN KEY ([VoiceProfileVersionId])
               REFERENCES [vf].[VoiceProfileVersions] ([VoiceProfileVersionId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
          AND [name] = N'FK_Projects_ApprovedNarratorVoiceProfileVersion'
    )
        EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK
               ADD CONSTRAINT [FK_Projects_ApprovedNarratorVoiceProfileVersion]
               FOREIGN KEY ([ApprovedNarratorVoiceProfileVersionId])
               REFERENCES [vf].[VoiceProfileVersions] ([VoiceProfileVersionId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Characters]')
          AND [name] = N'FK_Characters_ApprovedVoiceProfileVersion'
    )
        EXEC(N'ALTER TABLE [vf].[Characters] WITH CHECK
               ADD CONSTRAINT [FK_Characters_ApprovedVoiceProfileVersion]
               FOREIGN KEY ([ApprovedVoiceProfileVersionId])
               REFERENCES [vf].[VoiceProfileVersions] ([VoiceProfileVersionId]);');

    IF COL_LENGTH(N'vf.Scenes', N'ApprovedVoiceGenerationId') IS NULL
        ALTER TABLE [vf].[Scenes] ADD [ApprovedVoiceGenerationId] uniqueidentifier NULL;

    IF COL_LENGTH(N'vf.Scenes', N'ApprovedRenderMediaAssetId') IS NULL
        ALTER TABLE [vf].[Scenes] ADD [ApprovedRenderMediaAssetId] uniqueidentifier NULL;

    IF COL_LENGTH(N'vf.Scenes', N'SpeechStatus') IS NULL
        ALTER TABLE [vf].[Scenes] ADD
            [SpeechStatus] varchar(40) NOT NULL
                CONSTRAINT [DF_Scenes_SpeechStatus]
                DEFAULT ('SpeechNotRequired') WITH VALUES;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
          AND [name] = N'CK_Scenes_SpeechStatus'
    )
        EXEC(N'ALTER TABLE [vf].[Scenes] WITH CHECK
               ADD CONSTRAINT [CK_Scenes_SpeechStatus]
               CHECK ([SpeechStatus] IN
               (''SpeechNotRequired'',''SpeechMissing'',''SpeechGenerating'',
                ''SpeechVerificationRequired'',''SpeechReviewRequired'',''SpeechApproved'',
                ''SpeechReadyForLipSync'',''SpeechInvalid''));');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
          AND [name] = N'FK_Scenes_ApprovedVoiceGeneration'
    )
        EXEC(N'ALTER TABLE [vf].[Scenes] WITH CHECK
               ADD CONSTRAINT [FK_Scenes_ApprovedVoiceGeneration]
               FOREIGN KEY ([ApprovedVoiceGenerationId])
               REFERENCES [vf].[VoiceGenerations] ([VoiceGenerationId]);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
          AND [name] = N'FK_Scenes_ApprovedRenderMediaAsset'
    )
        EXEC(N'ALTER TABLE [vf].[Scenes] WITH CHECK
               ADD CONSTRAINT [FK_Scenes_ApprovedRenderMediaAsset]
               FOREIGN KEY ([ApprovedRenderMediaAssetId])
               REFERENCES [vf].[MediaAssets] ([MediaAssetId]);');

    IF OBJECT_ID(N'[vf].[SpeechVerificationReports]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[SpeechVerificationReports]
        (
            [SpeechVerificationReportId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId] uniqueidentifier NOT NULL,
            [SceneId] uniqueidentifier NOT NULL,
            [VoiceGenerationId] uniqueidentifier NULL,
            [SourceMediaAssetId] uniqueidentifier NOT NULL,
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [ExpectedSpeechHash] char(64) NOT NULL,
            [MediaSha256] char(64) NOT NULL,
            [Transcript] nvarchar(max) NOT NULL,
            [NormalizedTranscript] nvarchar(max) NOT NULL,
            [WordErrorRate] decimal(7,6) NOT NULL,
            [CharacterErrorRate] decimal(7,6) NOT NULL,
            [RequiredTermRecall] decimal(7,6) NOT NULL,
            [RequiredTermsJson] nvarchar(max) NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_RequiredTermsJson] DEFAULT ('[]'),
            [MissingTermsJson] nvarchar(max) NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_MissingTermsJson] DEFAULT ('[]'),
            [SpeechStartMs] bigint NULL,
            [SpeechEndMs] bigint NULL,
            [WordTimingsJson] nvarchar(max) NULL,
            [Status] varchar(30) NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_Status] DEFAULT ('Pending'),
            [CreatedAtUtc] datetime2(3) NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [CompletedAtUtc] datetime2(3) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT [PK_SpeechVerificationReports]
                PRIMARY KEY CLUSTERED ([SpeechVerificationReportId]),
            CONSTRAINT [FK_SpeechVerificationReports_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects] ([ProjectId]),
            CONSTRAINT [FK_SpeechVerificationReports_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes] ([SceneId]),
            CONSTRAINT [FK_SpeechVerificationReports_VoiceGenerations]
                FOREIGN KEY ([VoiceGenerationId]) REFERENCES [vf].[VoiceGenerations] ([VoiceGenerationId]),
            CONSTRAINT [FK_SpeechVerificationReports_SourceMediaAsset]
                FOREIGN KEY ([SourceMediaAssetId]) REFERENCES [vf].[MediaAssets] ([MediaAssetId]),
            CONSTRAINT [FK_SpeechVerificationReports_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests] ([ProviderRequestId]),
            CONSTRAINT [UQ_SpeechVerificationReports_ProviderRequest]
                UNIQUE ([ProviderRequestId]),
            CONSTRAINT [CK_SpeechVerificationReports_ExpectedSpeechHash]
                CHECK ([ExpectedSpeechHash] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([ExpectedSpeechHash]) = 64),
            CONSTRAINT [CK_SpeechVerificationReports_MediaSha256]
                CHECK ([MediaSha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([MediaSha256]) = 64),
            CONSTRAINT [CK_SpeechVerificationReports_Metrics]
                CHECK ([WordErrorRate] BETWEEN 0 AND 1 AND
                       [CharacterErrorRate] BETWEEN 0 AND 1 AND
                       [RequiredTermRecall] BETWEEN 0 AND 1),
            CONSTRAINT [CK_SpeechVerificationReports_Timing]
                CHECK (([SpeechStartMs] IS NULL AND [SpeechEndMs] IS NULL) OR
                       ([SpeechStartMs] >= 0 AND [SpeechEndMs] >= [SpeechStartMs])),
            CONSTRAINT [CK_SpeechVerificationReports_WordTimingsJson]
                CHECK ([WordTimingsJson] IS NULL OR ISJSON([WordTimingsJson]) = 1),
            CONSTRAINT [CK_SpeechVerificationReports_TermsJson]
                CHECK (ISJSON([RequiredTermsJson]) = 1 AND ISJSON([MissingTermsJson]) = 1),
            CONSTRAINT [CK_SpeechVerificationReports_Status]
                CHECK ([Status] IN ('Pending','Processing','Passed','NeedsReview','Failed'))
        );
    END;

    IF COL_LENGTH(N'vf.SpeechVerificationReports', N'RequiredTermsJson') IS NULL
        ALTER TABLE [vf].[SpeechVerificationReports] ADD
            [RequiredTermsJson] nvarchar(max) NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_RequiredTermsJson] DEFAULT ('[]') WITH VALUES;

    IF COL_LENGTH(N'vf.SpeechVerificationReports', N'MissingTermsJson') IS NULL
        ALTER TABLE [vf].[SpeechVerificationReports] ADD
            [MissingTermsJson] nvarchar(max) NOT NULL
                CONSTRAINT [DF_SpeechVerificationReports_MissingTermsJson] DEFAULT ('[]') WITH VALUES;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[SpeechVerificationReports]')
          AND [name] = N'CK_SpeechVerificationReports_TermsJson'
    )
        EXEC(N'ALTER TABLE [vf].[SpeechVerificationReports] WITH CHECK
               ADD CONSTRAINT [CK_SpeechVerificationReports_TermsJson]
               CHECK (ISJSON([RequiredTermsJson]) = 1 AND ISJSON([MissingTermsJson]) = 1);');

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[SpeechVerificationReports]')
          AND [name] = N'IX_SpeechVerificationReports_Scene_Created'
    )
        CREATE INDEX [IX_SpeechVerificationReports_Scene_Created]
            ON [vf].[SpeechVerificationReports] ([SceneId], [CreatedAtUtc] DESC);

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[vf].[SpeechVerificationReports]')
          AND [name] = N'IX_SpeechVerificationReports_Project_Snapshot'
    )
        CREATE INDEX [IX_SpeechVerificationReports_Project_Snapshot]
            ON [vf].[SpeechVerificationReports]
            ([ProjectId], [ExpectedSpeechHash], [MediaSha256])
            INCLUDE ([Status], [ProviderRequestId], [CompletedAtUtc]);

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderModels]')
          AND [name] = N'CK_ProviderModels_Modality'
    )
        ALTER TABLE [vf].[ProviderModels] DROP CONSTRAINT [CK_ProviderModels_Modality];

    ALTER TABLE [vf].[ProviderModels] WITH CHECK
        ADD CONSTRAINT [CK_ProviderModels_Modality]
        CHECK ([Modality] IN
        ('Text','TextRepair','Image','Video','Voice','Transcription','Search','Music','SoundEffect'));

    IF EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[ProviderRequests]')
          AND [name] = N'CK_ProviderRequests_Kind'
    )
        ALTER TABLE [vf].[ProviderRequests] DROP CONSTRAINT [CK_ProviderRequests_Kind];

    ALTER TABLE [vf].[ProviderRequests] WITH CHECK
        ADD CONSTRAINT [CK_ProviderRequests_Kind]
        CHECK ([RequestKind] IN
        ('Text','TextRepair','Image','Video','Voice','Transcription','Search','Music','SoundEffect'));

    IF OBJECT_ID(N'[ai].[SchemaVersions]', N'U') IS NULL
        THROW 51131, 'Organization AI Gateway schema version table is required.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM [ai].[SchemaVersions]
        WHERE [Version] = '4.1.3-speech-synchronization'
    )
        INSERT INTO [ai].[SchemaVersions] ([Version], [Description])
        VALUES
        (
            '4.1.3-speech-synchronization',
            N'Long-form speech policy, immutable voice snapshots, approved render audio linkage and transcript verification reports.'
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
    GRANT SELECT ON OBJECT::[vf].[SpeechVerificationReports] TO [VideoMakerDesktopRole];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[SpeechVerificationReports] TO [VideoMakerDesktopRole];
    GRANT SELECT ON OBJECT::[vf].[VoiceProfiles] TO [VideoMakerDesktopRole];
    GRANT SELECT ON OBJECT::[vf].[VoiceProfileVersions] TO [VideoMakerDesktopRole];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[VoiceProfiles] TO [VideoMakerDesktopRole];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[VoiceProfileVersions] TO [VideoMakerDesktopRole];
END;
GO

IF COL_LENGTH(N'vf.Projects', N'SpeechProductionPolicy') IS NULL OR
   COL_LENGTH(N'vf.Projects', N'ApprovedNarratorVoiceProfileVersionId') IS NULL OR
   COL_LENGTH(N'vf.Characters', N'VoiceCode') IS NULL OR
   COL_LENGTH(N'vf.Characters', N'ApprovedVoiceProfileVersionId') IS NULL OR
   COL_LENGTH(N'vf.VoiceGenerations', N'VoiceSnapshotHash') IS NULL OR
   COL_LENGTH(N'vf.VoiceGenerations', N'VoiceProfileVersionId') IS NULL OR
   COL_LENGTH(N'vf.Scenes', N'ApprovedVoiceGenerationId') IS NULL OR
   COL_LENGTH(N'vf.Scenes', N'ApprovedRenderMediaAssetId') IS NULL OR
   COL_LENGTH(N'vf.Scenes', N'SpeechStatus') IS NULL OR
   COL_LENGTH(N'vf.SpeechVerificationReports', N'RequiredTermsJson') IS NULL OR
   COL_LENGTH(N'vf.SpeechVerificationReports', N'MissingTermsJson') IS NULL OR
   OBJECT_ID(N'[vf].[SpeechVerificationReports]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[VoiceProfiles]', N'U') IS NULL OR
   OBJECT_ID(N'[vf].[VoiceProfileVersions]', N'U') IS NULL
BEGIN
    THROW 51132, 'Speech synchronization schema verification failed.', 1;
END;

IF NOT EXISTS
(
    SELECT 1 FROM [ai].[SchemaVersions]
    WHERE [Version] = '4.1.3-speech-synchronization'
)
BEGIN
    THROW 51133, 'Speech synchronization schema version was not recorded.', 1;
END;

PRINT N'VideoFactory speech synchronization schema 4.1.3 is ready.';
GO
