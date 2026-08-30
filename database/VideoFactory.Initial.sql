/*
    VideoFactory - Single database for Desktop + Account Server
    Target        : SQL Server Express / Developer Edition (SQL Server 2019+ recommended)
    Applications  : Local WinForms .NET 10 + ASP.NET Core Account Server .NET 10
    Base schema version: 3.0.0

    IMPORTANT: After this base script, run
      database/VideoFactory.4.0.0.OrganizationAiGateway.sql
      database/VideoFactory.4.0.1.VietnameseSeedTextRepair.sql
    before starting VideoMaker 4.0.

    Database:
      - [VideoFactory]

    Ownership inside the shared database:
      - [vf] schema   : local AI/video workflow used by TOOL-LOCAL.
      - [auth] schema : sessions, devices, licenses and account audit used by TOOL-SERVER.
      - [dbo] Identity tables: ASP.NET Core Identity used by TOOL-SERVER.

    How to run in SSMS:
      1. Connect with a Windows account that can create databases.
      2. Open this file.
      3. Change [VideoFactory] below if another database name is required.
      4. Execute the entire script.

    Safety:
      - This script does not DROP databases, tables, or user data.
      - Existing tables are not recreated.
      - Media binaries are not stored in SQL Server; only metadata and relative paths are stored.
      - The 4.0 migration stores provider credentials per organization on TOOL-SERVER only.
      - The legacy [vf].[ProviderCredentials] table remains for schema compatibility and is not used by the 4.0 runtime.
      - Refresh tokens are represented by hashes only; never store their plaintext value.
      - No default administrator/password is created by this script.
*/

USE [master];
GO

IF DB_ID(N'VideoFactory') IS NULL
BEGIN
    PRINT N'Creating database [VideoFactory]...';
    CREATE DATABASE [VideoFactory];
END
ELSE
BEGIN
    PRINT N'Database [VideoFactory] already exists.';
END;
GO

USE [VideoFactory];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'vf') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [vf] AUTHORIZATION [dbo];');
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       1. Schema and local application configuration
       ================================================================ */

    IF OBJECT_ID(N'[vf].[SchemaVersions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[SchemaVersions]
        (
            [SchemaVersionId]  int IDENTITY(1,1) NOT NULL,
            [Version]          varchar(50) NOT NULL,
            [Description]      nvarchar(500) NULL,
            [AppliedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_SchemaVersions_AppliedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_SchemaVersions] PRIMARY KEY CLUSTERED ([SchemaVersionId]),
            CONSTRAINT [UQ_SchemaVersions_Version] UNIQUE ([Version])
        );
    END;

    IF OBJECT_ID(N'[vf].[AppSettings]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[AppSettings]
        (
            [AppSettingId]     uniqueidentifier NOT NULL
                CONSTRAINT [DF_AppSettings_Id] DEFAULT NEWSEQUENTIALID(),
            [SettingKey]       nvarchar(200) NOT NULL,
            [ValueJson]        nvarchar(max) NULL,
            [Description]      nvarchar(1000) NULL,
            [UpdatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_AppSettings_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]       rowversion NOT NULL,
            CONSTRAINT [PK_AppSettings] PRIMARY KEY CLUSTERED ([AppSettingId]),
            CONSTRAINT [UQ_AppSettings_SettingKey] UNIQUE ([SettingKey]),
            CONSTRAINT [CK_AppSettings_ValueJson]
                CHECK ([ValueJson] IS NULL OR ISJSON([ValueJson]) = 1)
        );
    END;

    /* ================================================================
       2. Provider catalog and local rate cards
       ================================================================ */

    IF OBJECT_ID(N'[vf].[Providers]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Providers]
        (
            [ProviderId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_Providers_Id] DEFAULT NEWSEQUENTIALID(),
            [ProviderCode]     varchar(80) NOT NULL,
            [DisplayName]      nvarchar(200) NOT NULL,
            [BaseUrl]          nvarchar(1000) NULL,
            [IsEnabled]        bit NOT NULL CONSTRAINT [DF_Providers_IsEnabled] DEFAULT (1),
            [CapabilitiesJson] nvarchar(max) NULL,
            [SecretReference]  nvarchar(500) NULL,
            [CreatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_Providers_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_Providers_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]       rowversion NOT NULL,
            CONSTRAINT [PK_Providers] PRIMARY KEY CLUSTERED ([ProviderId]),
            CONSTRAINT [UQ_Providers_Code] UNIQUE ([ProviderCode]),
            CONSTRAINT [CK_Providers_CapabilitiesJson]
                CHECK ([CapabilitiesJson] IS NULL OR ISJSON([CapabilitiesJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[ProviderModels]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ProviderModels]
        (
            [ProviderModelId]  uniqueidentifier NOT NULL
                CONSTRAINT [DF_ProviderModels_Id] DEFAULT NEWSEQUENTIALID(),
            [ProviderId]       uniqueidentifier NOT NULL,
            [ModelCode]        nvarchar(200) NOT NULL,
            [DisplayName]      nvarchar(300) NOT NULL,
            [Modality]         varchar(30) NOT NULL,
            [IsEnabled]        bit NOT NULL CONSTRAINT [DF_ProviderModels_IsEnabled] DEFAULT (1),
            [IsDefault]        bit NOT NULL CONSTRAINT [DF_ProviderModels_IsDefault] DEFAULT (0),
            [CapabilitiesJson] nvarchar(max) NULL,
            [CreatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_ProviderModels_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_ProviderModels_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]       rowversion NOT NULL,
            CONSTRAINT [PK_ProviderModels] PRIMARY KEY CLUSTERED ([ProviderModelId]),
            CONSTRAINT [FK_ProviderModels_Providers]
                FOREIGN KEY ([ProviderId]) REFERENCES [vf].[Providers]([ProviderId]),
            CONSTRAINT [UQ_ProviderModels_Provider_Model]
                UNIQUE ([ProviderId], [ModelCode], [Modality]),
            CONSTRAINT [CK_ProviderModels_Modality]
                CHECK ([Modality] IN ('Text','Image','Video','Voice','Search','Music','SoundEffect')),
            CONSTRAINT [CK_ProviderModels_CapabilitiesJson]
                CHECK ([CapabilitiesJson] IS NULL OR ISJSON([CapabilitiesJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[CostRates]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[CostRates]
        (
            [CostRateId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_CostRates_Id] DEFAULT NEWSEQUENTIALID(),
            [ProviderModelId]  uniqueidentifier NOT NULL,
            [UsageType]        varchar(50) NOT NULL,
            [Unit]             varchar(30) NOT NULL,
            [UnitPrice]        decimal(19,8) NOT NULL,
            [CurrencyCode]     char(3) NOT NULL CONSTRAINT [DF_CostRates_Currency] DEFAULT ('USD'),
            [EffectiveFromUtc] datetime2(3) NOT NULL,
            [EffectiveToUtc]   datetime2(3) NULL,
            [IsActive]         bit NOT NULL CONSTRAINT [DF_CostRates_IsActive] DEFAULT (1),
            [MetadataJson]     nvarchar(max) NULL,
            [CreatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_CostRates_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_CostRates] PRIMARY KEY CLUSTERED ([CostRateId]),
            CONSTRAINT [FK_CostRates_ProviderModels]
                FOREIGN KEY ([ProviderModelId]) REFERENCES [vf].[ProviderModels]([ProviderModelId]),
            CONSTRAINT [CK_CostRates_UnitPrice] CHECK ([UnitPrice] >= 0),
            CONSTRAINT [CK_CostRates_EffectiveDates]
                CHECK ([EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]),
            CONSTRAINT [CK_CostRates_MetadataJson]
                CHECK ([MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[ProviderCredentials]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ProviderCredentials]
        (
            [ProviderCredentialId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_ProviderCredentials_Id] DEFAULT NEWSEQUENTIALID(),
            [ProviderId]           uniqueidentifier NOT NULL,
            [Name]                 nvarchar(100) NOT NULL,
            [AuthenticationType]   varchar(20) NOT NULL,
            [HeaderName]           varchar(100) NULL,
            [TestPath]             nvarchar(1000) NULL,
            [EncryptedPayload]     nvarchar(max) NOT NULL,
            [SecretHint]           varchar(16) NOT NULL,
            [IsActive]             bit NOT NULL CONSTRAINT [DF_ProviderCredentials_IsActive] DEFAULT (1),
            [TestStatus]           varchar(20) NOT NULL CONSTRAINT [DF_ProviderCredentials_TestStatus] DEFAULT ('Unknown'),
            [TestMessage]          nvarchar(1000) NULL,
            [LastTestedAtUtc]      datetime2(3) NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ProviderCredentials_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ProviderCredentials_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_ProviderCredentials] PRIMARY KEY CLUSTERED ([ProviderCredentialId]),
            CONSTRAINT [FK_ProviderCredentials_Providers]
                FOREIGN KEY ([ProviderId]) REFERENCES [vf].[Providers]([ProviderId]),
            CONSTRAINT [UQ_ProviderCredentials_Provider_Name] UNIQUE ([ProviderId], [Name]),
            CONSTRAINT [CK_ProviderCredentials_AuthenticationType]
                CHECK ([AuthenticationType] IN ('Bearer','Header')),
            CONSTRAINT [CK_ProviderCredentials_HeaderName]
                CHECK (([AuthenticationType] = 'Bearer' AND [HeaderName] IS NULL)
                    OR ([AuthenticationType] = 'Header' AND [HeaderName] IS NOT NULL)),
            CONSTRAINT [CK_ProviderCredentials_TestStatus]
                CHECK ([TestStatus] IN ('Unknown','Healthy','Failed'))
        );
    END;

    /* ================================================================
       3. Projects and AI planning versions
       ================================================================ */

    IF OBJECT_ID(N'[vf].[Projects]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Projects]
        (
            [ProjectId]                 uniqueidentifier NOT NULL
                CONSTRAINT [DF_Projects_Id] DEFAULT NEWSEQUENTIALID(),
            [RemoteUserId]              nvarchar(450) NULL,
            [RemoteDeviceId]            uniqueidentifier NULL,
            [OwnerDisplayNameSnapshot]  nvarchar(200) NULL,
            [Name]                      nvarchar(300) NOT NULL,
            [Topic]                     nvarchar(2000) NOT NULL,
            [LanguageCode]              varchar(10) NOT NULL CONSTRAINT [DF_Projects_Language] DEFAULT ('vi-VN'),
            [Platform]                  varchar(30) NOT NULL CONSTRAINT [DF_Projects_Platform] DEFAULT ('TikTok'),
            [AspectRatio]               varchar(10) NOT NULL CONSTRAINT [DF_Projects_AspectRatio] DEFAULT ('9:16'),
            [TargetDurationSeconds]     int NOT NULL CONSTRAINT [DF_Projects_TargetDuration] DEFAULT (30),
            [OutputWidth]               int NOT NULL CONSTRAINT [DF_Projects_OutputWidth] DEFAULT (1080),
            [OutputHeight]              int NOT NULL CONSTRAINT [DF_Projects_OutputHeight] DEFAULT (1920),
            [OutputFrameRate]           int NOT NULL CONSTRAINT [DF_Projects_OutputFrameRate] DEFAULT (30),
            [Status]                    varchar(40) NOT NULL CONSTRAINT [DF_Projects_Status] DEFAULT ('Draft'),
            [CurrentConceptVersion]     int NULL,
            [CurrentScriptVersion]      int NULL,
            [CurrentCharacterVersion]   int NULL,
            [CurrentStyleVersion]       int NULL,
            [CurrentScenePlanVersion]   int NULL,
            [RequireContentApproval]    bit NOT NULL CONSTRAINT [DF_Projects_RequireContentApproval] DEFAULT (0),
            [RequireStoryboardApproval] bit NOT NULL CONSTRAINT [DF_Projects_RequireStoryboardApproval] DEFAULT (0),
            [BudgetLimit]               decimal(19,6) NULL,
            [EstimatedCost]             decimal(19,6) NOT NULL CONSTRAINT [DF_Projects_EstimatedCost] DEFAULT (0),
            [ActualCost]                decimal(19,6) NOT NULL CONSTRAINT [DF_Projects_ActualCost] DEFAULT (0),
            [CurrencyCode]              char(3) NOT NULL CONSTRAINT [DF_Projects_Currency] DEFAULT ('USD'),
            [WorkspaceRelativePath]     nvarchar(500) NOT NULL,
            [LastErrorCode]             varchar(100) NULL,
            [LastErrorMessage]          nvarchar(4000) NULL,
            [CreatedAtUtc]              datetime2(3) NOT NULL
                CONSTRAINT [DF_Projects_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]              datetime2(3) NOT NULL
                CONSTRAINT [DF_Projects_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [CompletedAtUtc]            datetime2(3) NULL,
            [DeletedAtUtc]              datetime2(3) NULL,
            [RowVersion]                rowversion NOT NULL,
            CONSTRAINT [PK_Projects] PRIMARY KEY CLUSTERED ([ProjectId]),
            CONSTRAINT [CK_Projects_TargetDuration]
                CHECK ([TargetDurationSeconds] BETWEEN 5 AND 3600),
            CONSTRAINT [CK_Projects_OutputSize]
                CHECK ([OutputWidth] > 0 AND [OutputHeight] > 0 AND [OutputFrameRate] BETWEEN 1 AND 120),
            CONSTRAINT [CK_Projects_Budget]
                CHECK ([BudgetLimit] IS NULL OR [BudgetLimit] >= 0),
            CONSTRAINT [CK_Projects_Costs]
                CHECK ([EstimatedCost] >= 0 AND [ActualCost] >= 0),
            CONSTRAINT [CK_Projects_Status]
                CHECK ([Status] IN
                (
                    'Draft','ContentPlanning','AwaitingContentApproval',
                    'CharacterGenerating','AwaitingCharacterApproval',
                    'VoiceGenerating','ScenePlanning','GeneratingScenes',
                    'ValidatingScenes','ReadyToRender','Rendering',
                    'AwaitingFinalApproval','Completed','Paused','Failed','Cancelled'
                ))
        );
    END;

    /* Safe upgrade path if an earlier local Projects table already exists. */
    IF COL_LENGTH(N'[vf].[Projects]', N'Platform') IS NULL
        ALTER TABLE [vf].[Projects] ADD [Platform] varchar(30) NOT NULL
            CONSTRAINT [DF_Projects_Platform] DEFAULT ('TikTok') WITH VALUES;

    IF COL_LENGTH(N'[vf].[Projects]', N'RemoteUserId') IS NULL
        ALTER TABLE [vf].[Projects] ADD [RemoteUserId] nvarchar(450) NULL;

    IF COL_LENGTH(N'[vf].[Projects]', N'RemoteDeviceId') IS NULL
        ALTER TABLE [vf].[Projects] ADD [RemoteDeviceId] uniqueidentifier NULL;

    IF COL_LENGTH(N'[vf].[Projects]', N'OwnerDisplayNameSnapshot') IS NULL
        ALTER TABLE [vf].[Projects] ADD [OwnerDisplayNameSnapshot] nvarchar(200) NULL;

    IF OBJECT_ID(N'[vf].[Concepts]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Concepts]
        (
            [ConceptId]        uniqueidentifier NOT NULL
                CONSTRAINT [DF_Concepts_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]        uniqueidentifier NOT NULL,
            [Version]          int NOT NULL,
            [Title]            nvarchar(500) NOT NULL,
            [SelectedHook]     nvarchar(2000) NULL,
            [Angle]            nvarchar(2000) NULL,
            [Audience]         nvarchar(2000) NULL,
            [CallToAction]     nvarchar(2000) NULL,
            [ViralScore]       decimal(5,2) NULL,
            [HooksJson]        nvarchar(max) NULL,
            [StrategyJson]     nvarchar(max) NULL,
            [Status]           varchar(20) NOT NULL CONSTRAINT [DF_Concepts_Status] DEFAULT ('Draft'),
            [ProviderCode]     varchar(80) NULL,
            [ModelCode]        nvarchar(200) NULL,
            [CreatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_Concepts_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]    datetime2(3) NULL,
            [RowVersion]       rowversion NOT NULL,
            CONSTRAINT [PK_Concepts] PRIMARY KEY CLUSTERED ([ConceptId]),
            CONSTRAINT [FK_Concepts_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [UQ_Concepts_Project_Version] UNIQUE ([ProjectId], [Version]),
            CONSTRAINT [CK_Concepts_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_Concepts_ViralScore]
                CHECK ([ViralScore] IS NULL OR [ViralScore] BETWEEN 0 AND 100),
            CONSTRAINT [CK_Concepts_Status]
                CHECK ([Status] IN ('Draft','Approved','Rejected','Superseded')),
            CONSTRAINT [CK_Concepts_HooksJson]
                CHECK ([HooksJson] IS NULL OR ISJSON([HooksJson]) = 1),
            CONSTRAINT [CK_Concepts_StrategyJson]
                CHECK ([StrategyJson] IS NULL OR ISJSON([StrategyJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[Scripts]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Scripts]
        (
            [ScriptId]                 uniqueidentifier NOT NULL
                CONSTRAINT [DF_Scripts_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]                uniqueidentifier NOT NULL,
            [ConceptId]                uniqueidentifier NULL,
            [Version]                  int NOT NULL,
            [StructureType]            varchar(80) NOT NULL,
            [Title]                    nvarchar(500) NULL,
            [FullText]                 nvarchar(max) NOT NULL,
            [NarrationJson]            nvarchar(max) NULL,
            [DialogueJson]             nvarchar(max) NULL,
            [StoryBeatsJson]           nvarchar(max) NOT NULL,
            [EstimatedDurationMs]      bigint NULL,
            [MeasuredVoiceDurationMs]  bigint NULL,
            [QualityScore]             decimal(5,2) NULL,
            [QualityReportJson]        nvarchar(max) NULL,
            [Status]                   varchar(20) NOT NULL CONSTRAINT [DF_Scripts_Status] DEFAULT ('Draft'),
            [ProviderCode]             varchar(80) NULL,
            [ModelCode]                nvarchar(200) NULL,
            [CreatedAtUtc]             datetime2(3) NOT NULL
                CONSTRAINT [DF_Scripts_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]            datetime2(3) NULL,
            [RowVersion]               rowversion NOT NULL,
            CONSTRAINT [PK_Scripts] PRIMARY KEY CLUSTERED ([ScriptId]),
            CONSTRAINT [FK_Scripts_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_Scripts_Concepts]
                FOREIGN KEY ([ConceptId]) REFERENCES [vf].[Concepts]([ConceptId]),
            CONSTRAINT [UQ_Scripts_Project_Version] UNIQUE ([ProjectId], [Version]),
            CONSTRAINT [CK_Scripts_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_Scripts_Durations]
                CHECK (([EstimatedDurationMs] IS NULL OR [EstimatedDurationMs] >= 0)
                   AND ([MeasuredVoiceDurationMs] IS NULL OR [MeasuredVoiceDurationMs] >= 0)),
            CONSTRAINT [CK_Scripts_QualityScore]
                CHECK ([QualityScore] IS NULL OR [QualityScore] BETWEEN 0 AND 100),
            CONSTRAINT [CK_Scripts_Status]
                CHECK ([Status] IN ('Draft','Approved','Rejected','Superseded')),
            CONSTRAINT [CK_Scripts_NarrationJson]
                CHECK ([NarrationJson] IS NULL OR ISJSON([NarrationJson]) = 1),
            CONSTRAINT [CK_Scripts_DialogueJson]
                CHECK ([DialogueJson] IS NULL OR ISJSON([DialogueJson]) = 1),
            CONSTRAINT [CK_Scripts_StoryBeatsJson]
                CHECK (ISJSON([StoryBeatsJson]) = 1),
            CONSTRAINT [CK_Scripts_QualityReportJson]
                CHECK ([QualityReportJson] IS NULL OR ISJSON([QualityReportJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[Characters]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Characters]
        (
            [CharacterId]          uniqueidentifier NOT NULL
                CONSTRAINT [DF_Characters_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]            uniqueidentifier NOT NULL,
            [CharacterKey]         varchar(100) NOT NULL,
            [Version]              int NOT NULL,
            [Name]                 nvarchar(200) NOT NULL,
            [Role]                 nvarchar(200) NULL,
            [IdentityAnchor]       varchar(100) NULL,
            [ProfileJson]          nvarchar(max) NOT NULL,
            [WardrobeJson]         nvarchar(max) NULL,
            [ForbiddenChangesJson] nvarchar(max) NULL,
            [VisualIdentity]       nvarchar(max) NULL,
            [Status]               varchar(20) NOT NULL CONSTRAINT [DF_Characters_Status] DEFAULT ('Draft'),
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_Characters_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]        datetime2(3) NULL,
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_Characters] PRIMARY KEY CLUSTERED ([CharacterId]),
            CONSTRAINT [FK_Characters_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [UQ_Characters_Project_Key_Version]
                UNIQUE ([ProjectId], [CharacterKey], [Version]),
            CONSTRAINT [CK_Characters_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_Characters_Status]
                CHECK ([Status] IN ('Draft','GeneratingReferences','AwaitingApproval','Approved','Rejected','Superseded')),
            CONSTRAINT [CK_Characters_ProfileJson] CHECK (ISJSON([ProfileJson]) = 1),
            CONSTRAINT [CK_Characters_WardrobeJson]
                CHECK ([WardrobeJson] IS NULL OR ISJSON([WardrobeJson]) = 1),
            CONSTRAINT [CK_Characters_ForbiddenChangesJson]
                CHECK ([ForbiddenChangesJson] IS NULL OR ISJSON([ForbiddenChangesJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[StyleProfiles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[StyleProfiles]
        (
            [StyleProfileId]    uniqueidentifier NOT NULL
                CONSTRAINT [DF_StyleProfiles_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]         uniqueidentifier NOT NULL,
            [Version]           int NOT NULL,
            [Name]              nvarchar(200) NOT NULL,
            [VisualStyleJson]   nvarchar(max) NOT NULL,
            [ColorStyleJson]    nvarchar(max) NULL,
            [CameraStyleJson]   nvarchar(max) NULL,
            [LightingStyleJson] nvarchar(max) NULL,
            [EnvironmentJson]   nvarchar(max) NULL,
            [NegativeRulesJson] nvarchar(max) NULL,
            [Status]            varchar(20) NOT NULL CONSTRAINT [DF_StyleProfiles_Status] DEFAULT ('Draft'),
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_StyleProfiles_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]     datetime2(3) NULL,
            [RowVersion]        rowversion NOT NULL,
            CONSTRAINT [PK_StyleProfiles] PRIMARY KEY CLUSTERED ([StyleProfileId]),
            CONSTRAINT [FK_StyleProfiles_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [UQ_StyleProfiles_Project_Version] UNIQUE ([ProjectId], [Version]),
            CONSTRAINT [CK_StyleProfiles_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_StyleProfiles_Status]
                CHECK ([Status] IN ('Draft','Approved','Rejected','Superseded')),
            CONSTRAINT [CK_StyleProfiles_VisualJson] CHECK (ISJSON([VisualStyleJson]) = 1),
            CONSTRAINT [CK_StyleProfiles_ColorJson]
                CHECK ([ColorStyleJson] IS NULL OR ISJSON([ColorStyleJson]) = 1),
            CONSTRAINT [CK_StyleProfiles_CameraJson]
                CHECK ([CameraStyleJson] IS NULL OR ISJSON([CameraStyleJson]) = 1),
            CONSTRAINT [CK_StyleProfiles_LightingJson]
                CHECK ([LightingStyleJson] IS NULL OR ISJSON([LightingStyleJson]) = 1),
            CONSTRAINT [CK_StyleProfiles_EnvironmentJson]
                CHECK ([EnvironmentJson] IS NULL OR ISJSON([EnvironmentJson]) = 1),
            CONSTRAINT [CK_StyleProfiles_NegativeJson]
                CHECK ([NegativeRulesJson] IS NULL OR ISJSON([NegativeRulesJson]) = 1)
        );
    END;

    /* ================================================================
       4. Scenes, prompts, and local media metadata
       ================================================================ */

    IF OBJECT_ID(N'[vf].[Scenes]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Scenes]
        (
            [SceneId]                    uniqueidentifier NOT NULL
                CONSTRAINT [DF_Scenes_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]                  uniqueidentifier NOT NULL,
            [ScriptId]                   uniqueidentifier NOT NULL,
            [StyleProfileId]             uniqueidentifier NOT NULL,
            [ScenePlanVersion]           int NOT NULL,
            [SequenceNumber]             int NOT NULL,
            [ContinuityGroupKey]         varchar(100) NULL,
            [StoryBeatId]                varchar(100) NULL,
            [StoryPurpose]               nvarchar(1000) NOT NULL,
            [Narration]                  nvarchar(max) NULL,
            [Dialogue]                   nvarchar(max) NULL,
            [VisualDescription]          nvarchar(max) NOT NULL,
            [LocationKey]                varchar(100) NULL,
            [CameraDirection]            nvarchar(2000) NULL,
            [Lighting]                   nvarchar(2000) NULL,
            [Motion]                     nvarchar(2000) NULL,
            [Emotion]                    nvarchar(1000) NULL,
            [TransitionAfter]            nvarchar(1000) NULL,
            [ContentDurationMs]          bigint NOT NULL,
            [GenerationDurationMs]       bigint NOT NULL,
            [TimelineStartMs]            bigint NOT NULL,
            [TimelineEndMs]              bigint NOT NULL,
            [HeadTrimMs]                 bigint NOT NULL CONSTRAINT [DF_Scenes_HeadTrimMs] DEFAULT (0),
            [TailTrimMs]                 bigint NOT NULL CONSTRAINT [DF_Scenes_TailTrimMs] DEFAULT (0),
            [OverlapAfterMs]             bigint NOT NULL CONSTRAINT [DF_Scenes_OverlapAfterMs] DEFAULT (0),
            [PreviousSceneId]            uniqueidentifier NULL,
            [NextSceneId]                uniqueidentifier NULL,
            [GenerationDependencySceneId] uniqueidentifier NULL,
            [CharacterIdsJson]           nvarchar(max) NULL,
            [EntryStateJson]             nvarchar(max) NOT NULL,
            [ExitStateJson]              nvarchar(max) NOT NULL,
            [RequiredCapabilitiesJson]   nvarchar(max) NULL,
            [Status]                     varchar(30) NOT NULL CONSTRAINT [DF_Scenes_Status] DEFAULT ('Pending'),
            [ApprovedGenerationId]       uniqueidentifier NULL,
            [LastErrorCode]              varchar(100) NULL,
            [LastErrorMessage]           nvarchar(4000) NULL,
            [CreatedAtUtc]               datetime2(3) NOT NULL
                CONSTRAINT [DF_Scenes_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]               datetime2(3) NOT NULL
                CONSTRAINT [DF_Scenes_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]                 rowversion NOT NULL,
            CONSTRAINT [PK_Scenes] PRIMARY KEY CLUSTERED ([SceneId]),
            CONSTRAINT [FK_Scenes_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_Scenes_Scripts]
                FOREIGN KEY ([ScriptId]) REFERENCES [vf].[Scripts]([ScriptId]),
            CONSTRAINT [FK_Scenes_StyleProfiles]
                FOREIGN KEY ([StyleProfileId]) REFERENCES [vf].[StyleProfiles]([StyleProfileId]),
            CONSTRAINT [FK_Scenes_PreviousScene]
                FOREIGN KEY ([PreviousSceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_Scenes_NextScene]
                FOREIGN KEY ([NextSceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_Scenes_DependencyScene]
                FOREIGN KEY ([GenerationDependencySceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [UQ_Scenes_Project_Plan_Sequence]
                UNIQUE ([ProjectId], [ScenePlanVersion], [SequenceNumber]),
            CONSTRAINT [CK_Scenes_PositiveValues]
                CHECK ([ScenePlanVersion] > 0 AND [SequenceNumber] > 0
                   AND [ContentDurationMs] > 0 AND [GenerationDurationMs] > 0),
            CONSTRAINT [CK_Scenes_Timeline]
                CHECK ([TimelineStartMs] >= 0 AND [TimelineEndMs] > [TimelineStartMs]),
            CONSTRAINT [CK_Scenes_Trims]
                CHECK ([HeadTrimMs] >= 0 AND [TailTrimMs] >= 0 AND [OverlapAfterMs] >= 0),
            CONSTRAINT [CK_Scenes_Status]
                CHECK ([Status] IN
                (
                    'Pending','PromptReady','Generating','WaitingProvider','Generated',
                    'Validating','Approved','Failed','RetryScheduled','Cancelled'
                )),
            CONSTRAINT [CK_Scenes_CharacterIdsJson]
                CHECK ([CharacterIdsJson] IS NULL OR ISJSON([CharacterIdsJson]) = 1),
            CONSTRAINT [CK_Scenes_EntryStateJson] CHECK (ISJSON([EntryStateJson]) = 1),
            CONSTRAINT [CK_Scenes_ExitStateJson] CHECK (ISJSON([ExitStateJson]) = 1),
            CONSTRAINT [CK_Scenes_RequiredCapabilitiesJson]
                CHECK ([RequiredCapabilitiesJson] IS NULL OR ISJSON([RequiredCapabilitiesJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[MediaAssets]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[MediaAssets]
        (
            [MediaAssetId]          uniqueidentifier NOT NULL
                CONSTRAINT [DF_MediaAssets_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]             uniqueidentifier NOT NULL,
            [SceneId]               uniqueidentifier NULL,
            [AssetType]             varchar(60) NOT NULL,
            [DisplayName]           nvarchar(300) NULL,
            [RelativePath]          nvarchar(500) NOT NULL,
            [MimeType]              varchar(150) NOT NULL,
            [SizeBytes]             bigint NOT NULL,
            [Sha256]                char(64) NOT NULL,
            [Width]                 int NULL,
            [Height]                int NULL,
            [FrameRate]             decimal(9,3) NULL,
            [DurationMs]            bigint NULL,
            [AudioSampleRate]       int NULL,
            [Status]                varchar(20) NOT NULL CONSTRAINT [DF_MediaAssets_Status] DEFAULT ('Ready'),
            [SourceType]            varchar(30) NOT NULL CONSTRAINT [DF_MediaAssets_SourceType] DEFAULT ('Generated'),
            [SourceProviderCode]    varchar(80) NULL,
            [SourceExternalRequestId] nvarchar(300) NULL,
            [MetadataJson]          nvarchar(max) NULL,
            [CreatedAtUtc]          datetime2(3) NOT NULL
                CONSTRAINT [DF_MediaAssets_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [VerifiedAtUtc]         datetime2(3) NULL,
            [DeletedAtUtc]          datetime2(3) NULL,
            [RowVersion]            rowversion NOT NULL,
            CONSTRAINT [PK_MediaAssets] PRIMARY KEY CLUSTERED ([MediaAssetId]),
            CONSTRAINT [FK_MediaAssets_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_MediaAssets_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [UQ_MediaAssets_Project_Path] UNIQUE ([ProjectId], [RelativePath]),
            CONSTRAINT [CK_MediaAssets_Size] CHECK ([SizeBytes] >= 0),
            CONSTRAINT [CK_MediaAssets_Dimensions]
                CHECK (([Width] IS NULL OR [Width] > 0) AND ([Height] IS NULL OR [Height] > 0)),
            CONSTRAINT [CK_MediaAssets_Duration]
                CHECK ([DurationMs] IS NULL OR [DurationMs] >= 0),
            CONSTRAINT [CK_MediaAssets_Status]
                CHECK ([Status] IN ('Pending','Downloading','Ready','Invalid','Missing','Deleted')),
            CONSTRAINT [CK_MediaAssets_SourceType]
                CHECK ([SourceType] IN ('Generated','Uploaded','Derived','Rendered','Imported')),
            CONSTRAINT [CK_MediaAssets_MetadataJson]
                CHECK ([MetadataJson] IS NULL OR ISJSON([MetadataJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[CharacterReferences]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[CharacterReferences]
        (
            [CharacterReferenceId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_CharacterReferences_Id] DEFAULT NEWSEQUENTIALID(),
            [CharacterId]          uniqueidentifier NOT NULL,
            [MediaAssetId]         uniqueidentifier NOT NULL,
            [ReferenceType]        varchar(40) NOT NULL,
            [ProviderReferenceId]  nvarchar(300) NULL,
            [IsPrimary]            bit NOT NULL CONSTRAINT [DF_CharacterReferences_IsPrimary] DEFAULT (0),
            [ApprovalStatus]       varchar(20) NOT NULL CONSTRAINT [DF_CharacterReferences_Approval] DEFAULT ('Pending'),
            [ApprovalComment]      nvarchar(2000) NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_CharacterReferences_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]        datetime2(3) NULL,
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_CharacterReferences] PRIMARY KEY CLUSTERED ([CharacterReferenceId]),
            CONSTRAINT [FK_CharacterReferences_Characters]
                FOREIGN KEY ([CharacterId]) REFERENCES [vf].[Characters]([CharacterId]),
            CONSTRAINT [FK_CharacterReferences_MediaAssets]
                FOREIGN KEY ([MediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [UQ_CharacterReferences_Character_Asset]
                UNIQUE ([CharacterId], [MediaAssetId]),
            CONSTRAINT [CK_CharacterReferences_Type]
                CHECK ([ReferenceType] IN
                ('Front','ThreeQuarter','Side','FullBody','ExpressionSheet','WardrobeSheet','EnvironmentPose','Other')),
            CONSTRAINT [CK_CharacterReferences_Approval]
                CHECK ([ApprovalStatus] IN ('Pending','Approved','Rejected'))
        );
    END;

    IF OBJECT_ID(N'[vf].[ScenePrompts]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ScenePrompts]
        (
            [ScenePromptId]        uniqueidentifier NOT NULL
                CONSTRAINT [DF_ScenePrompts_Id] DEFAULT NEWSEQUENTIALID(),
            [SceneId]              uniqueidentifier NOT NULL,
            [Version]              int NOT NULL,
            [PromptTemplateName]   varchar(150) NOT NULL,
            [PromptTemplateVersion] varchar(50) NOT NULL,
            [CanonicalInputJson]   nvarchar(max) NOT NULL,
            [FinalPrompt]          nvarchar(max) NOT NULL,
            [NegativePrompt]       nvarchar(max) NULL,
            [ProviderCode]         varchar(80) NULL,
            [ModelCode]            nvarchar(200) NULL,
            [ProviderPayloadJson]  nvarchar(max) NULL,
            [PromptHash]           char(64) NOT NULL,
            [Status]               varchar(20) NOT NULL CONSTRAINT [DF_ScenePrompts_Status] DEFAULT ('Draft'),
            [QualityReportJson]    nvarchar(max) NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ScenePrompts_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]        datetime2(3) NULL,
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_ScenePrompts] PRIMARY KEY CLUSTERED ([ScenePromptId]),
            CONSTRAINT [FK_ScenePrompts_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [UQ_ScenePrompts_Scene_Version] UNIQUE ([SceneId], [Version]),
            CONSTRAINT [CK_ScenePrompts_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_ScenePrompts_Status]
                CHECK ([Status] IN ('Draft','Ready','Approved','Rejected','Superseded')),
            CONSTRAINT [CK_ScenePrompts_CanonicalJson] CHECK (ISJSON([CanonicalInputJson]) = 1),
            CONSTRAINT [CK_ScenePrompts_ProviderPayloadJson]
                CHECK ([ProviderPayloadJson] IS NULL OR ISJSON([ProviderPayloadJson]) = 1),
            CONSTRAINT [CK_ScenePrompts_QualityReportJson]
                CHECK ([QualityReportJson] IS NULL OR ISJSON([QualityReportJson]) = 1)
        );
    END;

    /* ================================================================
       5. Persistent local jobs and job dependencies
       ================================================================ */

    IF OBJECT_ID(N'[vf].[Jobs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Jobs]
        (
            [JobId]              uniqueidentifier NOT NULL
                CONSTRAINT [DF_Jobs_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]          uniqueidentifier NOT NULL,
            [SceneId]            uniqueidentifier NULL,
            [ParentJobId]        uniqueidentifier NULL,
            [JobType]            varchar(100) NOT NULL,
            [Status]             varchar(30) NOT NULL CONSTRAINT [DF_Jobs_Status] DEFAULT ('Pending'),
            [Priority]           int NOT NULL CONSTRAINT [DF_Jobs_Priority] DEFAULT (0),
            [Attempt]            int NOT NULL CONSTRAINT [DF_Jobs_Attempt] DEFAULT (0),
            [MaxAttempts]        int NOT NULL CONSTRAINT [DF_Jobs_MaxAttempts] DEFAULT (3),
            [ProgressPercent]    decimal(5,2) NOT NULL CONSTRAINT [DF_Jobs_Progress] DEFAULT (0),
            [AvailableAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_Jobs_AvailableAtUtc] DEFAULT SYSUTCDATETIME(),
            [LockedBy]           nvarchar(200) NULL,
            [LockedAtUtc]        datetime2(3) NULL,
            [HeartbeatAtUtc]     datetime2(3) NULL,
            [LeaseExpiresAtUtc]  datetime2(3) NULL,
            [StartedAtUtc]       datetime2(3) NULL,
            [CompletedAtUtc]     datetime2(3) NULL,
            [IdempotencyKey]     nvarchar(450) NULL,
            [PayloadJson]        nvarchar(max) NULL,
            [ResultJson]         nvarchar(max) NULL,
            [ErrorCode]          varchar(100) NULL,
            [ErrorMessage]       nvarchar(4000) NULL,
            [CreatedAtUtc]       datetime2(3) NOT NULL
                CONSTRAINT [DF_Jobs_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]       datetime2(3) NOT NULL
                CONSTRAINT [DF_Jobs_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]         rowversion NOT NULL,
            CONSTRAINT [PK_Jobs] PRIMARY KEY CLUSTERED ([JobId]),
            CONSTRAINT [FK_Jobs_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_Jobs_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_Jobs_ParentJob]
                FOREIGN KEY ([ParentJobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [CK_Jobs_Status]
                CHECK ([Status] IN
                ('Pending','Running','WaitingProvider','RetryScheduled','Completed','Failed','Cancelled','Interrupted')),
            CONSTRAINT [CK_Jobs_Attempts]
                CHECK ([Attempt] >= 0 AND [MaxAttempts] > 0 AND [Attempt] <= [MaxAttempts]),
            CONSTRAINT [CK_Jobs_Progress]
                CHECK ([ProgressPercent] BETWEEN 0 AND 100),
            CONSTRAINT [CK_Jobs_PayloadJson]
                CHECK ([PayloadJson] IS NULL OR ISJSON([PayloadJson]) = 1),
            CONSTRAINT [CK_Jobs_ResultJson]
                CHECK ([ResultJson] IS NULL OR ISJSON([ResultJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[JobDependencies]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[JobDependencies]
        (
            [JobDependencyId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_JobDependencies_Id] DEFAULT NEWSEQUENTIALID(),
            [JobId]           uniqueidentifier NOT NULL,
            [DependsOnJobId]  uniqueidentifier NOT NULL,
            [CreatedAtUtc]    datetime2(3) NOT NULL
                CONSTRAINT [DF_JobDependencies_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_JobDependencies] PRIMARY KEY CLUSTERED ([JobDependencyId]),
            CONSTRAINT [FK_JobDependencies_Job]
                FOREIGN KEY ([JobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [FK_JobDependencies_DependsOn]
                FOREIGN KEY ([DependsOnJobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [UQ_JobDependencies_Pair] UNIQUE ([JobId], [DependsOnJobId]),
            CONSTRAINT [CK_JobDependencies_NotSelf] CHECK ([JobId] <> [DependsOnJobId])
        );
    END;

    IF OBJECT_ID(N'[vf].[JobEvents]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[JobEvents]
        (
            [JobEventId]       bigint IDENTITY(1,1) NOT NULL,
            [JobId]            uniqueidentifier NOT NULL,
            [EventType]        varchar(80) NOT NULL,
            [FromStatus]       varchar(30) NULL,
            [ToStatus]         varchar(30) NULL,
            [Message]          nvarchar(4000) NULL,
            [DataJson]         nvarchar(max) NULL,
            [OccurredAtUtc]    datetime2(3) NOT NULL
                CONSTRAINT [DF_JobEvents_OccurredAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_JobEvents] PRIMARY KEY CLUSTERED ([JobEventId]),
            CONSTRAINT [FK_JobEvents_Jobs]
                FOREIGN KEY ([JobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [CK_JobEvents_DataJson]
                CHECK ([DataJson] IS NULL OR ISJSON([DataJson]) = 1)
        );
    END;

    /* ================================================================
       6. Provider calls and generated artifacts
       ================================================================ */

    IF OBJECT_ID(N'[vf].[ProviderRequests]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[ProviderRequests]
        (
            [ProviderRequestId]    uniqueidentifier NOT NULL
                CONSTRAINT [DF_ProviderRequests_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]            uniqueidentifier NOT NULL,
            [SceneId]              uniqueidentifier NULL,
            [JobId]                uniqueidentifier NULL,
            [ProviderId]           uniqueidentifier NULL,
            [ProviderModelId]      uniqueidentifier NULL,
            [RequestKind]          varchar(40) NOT NULL,
            [ProviderCode]         varchar(80) NOT NULL,
            [ModelCode]            nvarchar(200) NOT NULL,
            [ExternalRequestId]    nvarchar(300) NULL,
            [IdempotencyKey]       nvarchar(450) NOT NULL,
            [Status]               varchar(30) NOT NULL CONSTRAINT [DF_ProviderRequests_Status] DEFAULT ('Created'),
            [RequestJson]          nvarchar(max) NOT NULL,
            [ResponseJson]         nvarchar(max) NULL,
            [PollCount]            int NOT NULL CONSTRAINT [DF_ProviderRequests_PollCount] DEFAULT (0),
            [LastPolledAtUtc]      datetime2(3) NULL,
            [NextPollAtUtc]        datetime2(3) NULL,
            [SubmittedAtUtc]       datetime2(3) NULL,
            [CompletedAtUtc]       datetime2(3) NULL,
            [EstimatedCost]        decimal(19,6) NOT NULL CONSTRAINT [DF_ProviderRequests_EstimatedCost] DEFAULT (0),
            [ActualCost]           decimal(19,6) NOT NULL CONSTRAINT [DF_ProviderRequests_ActualCost] DEFAULT (0),
            [CurrencyCode]         char(3) NOT NULL CONSTRAINT [DF_ProviderRequests_Currency] DEFAULT ('USD'),
            [ErrorCode]            varchar(100) NULL,
            [ErrorMessage]         nvarchar(4000) NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ProviderRequests_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_ProviderRequests_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_ProviderRequests] PRIMARY KEY CLUSTERED ([ProviderRequestId]),
            CONSTRAINT [FK_ProviderRequests_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_ProviderRequests_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_ProviderRequests_Jobs]
                FOREIGN KEY ([JobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [FK_ProviderRequests_Providers]
                FOREIGN KEY ([ProviderId]) REFERENCES [vf].[Providers]([ProviderId]),
            CONSTRAINT [FK_ProviderRequests_ProviderModels]
                FOREIGN KEY ([ProviderModelId]) REFERENCES [vf].[ProviderModels]([ProviderModelId]),
            CONSTRAINT [UQ_ProviderRequests_IdempotencyKey] UNIQUE ([IdempotencyKey]),
            CONSTRAINT [CK_ProviderRequests_Kind]
                CHECK ([RequestKind] IN ('Text','Image','Video','Voice','Search','Music','SoundEffect')),
            CONSTRAINT [CK_ProviderRequests_Status]
                CHECK ([Status] IN
                ('Created','Submitting','Submitted','Queued','Processing','Completed','Failed','Cancelled','Unknown')),
            CONSTRAINT [CK_ProviderRequests_Costs]
                CHECK ([EstimatedCost] >= 0 AND [ActualCost] >= 0),
            CONSTRAINT [CK_ProviderRequests_RequestJson] CHECK (ISJSON([RequestJson]) = 1),
            CONSTRAINT [CK_ProviderRequests_ResponseJson]
                CHECK ([ResponseJson] IS NULL OR ISJSON([ResponseJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[VideoGenerations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[VideoGenerations]
        (
            [VideoGenerationId]    uniqueidentifier NOT NULL
                CONSTRAINT [DF_VideoGenerations_Id] DEFAULT NEWSEQUENTIALID(),
            [SceneId]              uniqueidentifier NOT NULL,
            [ScenePromptId]        uniqueidentifier NOT NULL,
            [JobId]                uniqueidentifier NULL,
            [ProviderRequestId]    uniqueidentifier NOT NULL,
            [AttemptNumber]        int NOT NULL,
            [Status]               varchar(30) NOT NULL CONSTRAINT [DF_VideoGenerations_Status] DEFAULT ('Pending'),
            [Seed]                 bigint NULL,
            [RequestedDurationMs]  bigint NOT NULL,
            [ActualDurationMs]     bigint NULL,
            [InputReferenceAssetIdsJson] nvarchar(max) NULL,
            [OutputMediaAssetId]   uniqueidentifier NULL,
            [QualityScore]         decimal(5,2) NULL,
            [QualityReportJson]    nvarchar(max) NULL,
            [RegenerationFeedbackJson] nvarchar(max) NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_VideoGenerations_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [CompletedAtUtc]       datetime2(3) NULL,
            [RowVersion]           rowversion NOT NULL,
            CONSTRAINT [PK_VideoGenerations] PRIMARY KEY CLUSTERED ([VideoGenerationId]),
            CONSTRAINT [FK_VideoGenerations_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_VideoGenerations_ScenePrompts]
                FOREIGN KEY ([ScenePromptId]) REFERENCES [vf].[ScenePrompts]([ScenePromptId]),
            CONSTRAINT [FK_VideoGenerations_Jobs]
                FOREIGN KEY ([JobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [FK_VideoGenerations_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests]([ProviderRequestId]),
            CONSTRAINT [FK_VideoGenerations_OutputAsset]
                FOREIGN KEY ([OutputMediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [UQ_VideoGenerations_Scene_Attempt] UNIQUE ([SceneId], [AttemptNumber]),
            CONSTRAINT [CK_VideoGenerations_Attempt] CHECK ([AttemptNumber] > 0),
            CONSTRAINT [CK_VideoGenerations_Durations]
                CHECK ([RequestedDurationMs] > 0 AND ([ActualDurationMs] IS NULL OR [ActualDurationMs] > 0)),
            CONSTRAINT [CK_VideoGenerations_Status]
                CHECK ([Status] IN
                ('Pending','Submitting','WaitingProvider','Downloading','Generated','Validating','Approved','Failed','Cancelled')),
            CONSTRAINT [CK_VideoGenerations_QualityScore]
                CHECK ([QualityScore] IS NULL OR [QualityScore] BETWEEN 0 AND 100),
            CONSTRAINT [CK_VideoGenerations_ReferenceJson]
                CHECK ([InputReferenceAssetIdsJson] IS NULL OR ISJSON([InputReferenceAssetIdsJson]) = 1),
            CONSTRAINT [CK_VideoGenerations_QualityJson]
                CHECK ([QualityReportJson] IS NULL OR ISJSON([QualityReportJson]) = 1),
            CONSTRAINT [CK_VideoGenerations_FeedbackJson]
                CHECK ([RegenerationFeedbackJson] IS NULL OR ISJSON([RegenerationFeedbackJson]) = 1)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Scenes]')
          AND [name] = N'FK_Scenes_ApprovedGeneration'
    )
    BEGIN
        ALTER TABLE [vf].[Scenes] WITH CHECK
        ADD CONSTRAINT [FK_Scenes_ApprovedGeneration]
            FOREIGN KEY ([ApprovedGenerationId])
            REFERENCES [vf].[VideoGenerations]([VideoGenerationId]);
    END;

    IF OBJECT_ID(N'[vf].[VoiceGenerations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[VoiceGenerations]
        (
            [VoiceGenerationId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_VoiceGenerations_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]         uniqueidentifier NOT NULL,
            [ScriptId]          uniqueidentifier NOT NULL,
            [ProviderRequestId] uniqueidentifier NOT NULL,
            [Version]           int NOT NULL,
            [VoiceCode]         nvarchar(200) NOT NULL,
            [LanguageCode]      varchar(10) NOT NULL,
            [SpeakingRate]      decimal(6,3) NOT NULL CONSTRAINT [DF_VoiceGenerations_Rate] DEFAULT (1),
            [Status]            varchar(30) NOT NULL CONSTRAINT [DF_VoiceGenerations_Status] DEFAULT ('Pending'),
            [DurationMs]        bigint NULL,
            [WordTimingsJson]   nvarchar(max) NULL,
            [OutputMediaAssetId] uniqueidentifier NULL,
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_VoiceGenerations_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [CompletedAtUtc]    datetime2(3) NULL,
            [RowVersion]        rowversion NOT NULL,
            CONSTRAINT [PK_VoiceGenerations] PRIMARY KEY CLUSTERED ([VoiceGenerationId]),
            CONSTRAINT [FK_VoiceGenerations_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_VoiceGenerations_Scripts]
                FOREIGN KEY ([ScriptId]) REFERENCES [vf].[Scripts]([ScriptId]),
            CONSTRAINT [FK_VoiceGenerations_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests]([ProviderRequestId]),
            CONSTRAINT [FK_VoiceGenerations_OutputAsset]
                FOREIGN KEY ([OutputMediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [UQ_VoiceGenerations_Project_Version] UNIQUE ([ProjectId], [Version]),
            CONSTRAINT [CK_VoiceGenerations_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_VoiceGenerations_Rate] CHECK ([SpeakingRate] BETWEEN 0.5 AND 2.0),
            CONSTRAINT [CK_VoiceGenerations_Duration]
                CHECK ([DurationMs] IS NULL OR [DurationMs] > 0),
            CONSTRAINT [CK_VoiceGenerations_Status]
                CHECK ([Status] IN ('Pending','Generating','Completed','Approved','Failed','Cancelled')),
            CONSTRAINT [CK_VoiceGenerations_TimingsJson]
                CHECK ([WordTimingsJson] IS NULL OR ISJSON([WordTimingsJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[Subtitles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Subtitles]
        (
            [SubtitleId]        uniqueidentifier NOT NULL
                CONSTRAINT [DF_Subtitles_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]         uniqueidentifier NOT NULL,
            [VoiceGenerationId] uniqueidentifier NULL,
            [Version]           int NOT NULL,
            [Format]            varchar(10) NOT NULL,
            [LanguageCode]      varchar(10) NOT NULL,
            [StyleJson]         nvarchar(max) NULL,
            [MediaAssetId]      uniqueidentifier NOT NULL,
            [Status]            varchar(20) NOT NULL CONSTRAINT [DF_Subtitles_Status] DEFAULT ('Ready'),
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_Subtitles_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]        rowversion NOT NULL,
            CONSTRAINT [PK_Subtitles] PRIMARY KEY CLUSTERED ([SubtitleId]),
            CONSTRAINT [FK_Subtitles_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_Subtitles_VoiceGenerations]
                FOREIGN KEY ([VoiceGenerationId]) REFERENCES [vf].[VoiceGenerations]([VoiceGenerationId]),
            CONSTRAINT [FK_Subtitles_MediaAssets]
                FOREIGN KEY ([MediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [UQ_Subtitles_Project_Version_Format]
                UNIQUE ([ProjectId], [Version], [Format]),
            CONSTRAINT [CK_Subtitles_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_Subtitles_Format] CHECK ([Format] IN ('SRT','VTT','ASS')),
            CONSTRAINT [CK_Subtitles_Status] CHECK ([Status] IN ('Draft','Ready','Approved','Invalid')),
            CONSTRAINT [CK_Subtitles_StyleJson]
                CHECK ([StyleJson] IS NULL OR ISJSON([StyleJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[MusicAssets]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[MusicAssets]
        (
            [MusicAssetId]      uniqueidentifier NOT NULL
                CONSTRAINT [DF_MusicAssets_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]         uniqueidentifier NOT NULL,
            [MediaAssetId]      uniqueidentifier NOT NULL,
            [Title]             nvarchar(300) NULL,
            [SourceType]        varchar(30) NOT NULL,
            [LicenseInfoJson]   nvarchar(max) NULL,
            [TimelineStartMs]   bigint NOT NULL CONSTRAINT [DF_MusicAssets_Start] DEFAULT (0),
            [GainDb]            decimal(7,3) NOT NULL CONSTRAINT [DF_MusicAssets_Gain] DEFAULT (-18),
            [LoopEnabled]       bit NOT NULL CONSTRAINT [DF_MusicAssets_Loop] DEFAULT (1),
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_MusicAssets_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_MusicAssets] PRIMARY KEY CLUSTERED ([MusicAssetId]),
            CONSTRAINT [FK_MusicAssets_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_MusicAssets_MediaAssets]
                FOREIGN KEY ([MediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [CK_MusicAssets_SourceType]
                CHECK ([SourceType] IN ('Local','Uploaded','Generated','Stock')),
            CONSTRAINT [CK_MusicAssets_Start] CHECK ([TimelineStartMs] >= 0),
            CONSTRAINT [CK_MusicAssets_LicenseJson]
                CHECK ([LicenseInfoJson] IS NULL OR ISJSON([LicenseInfoJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[SoundEffects]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[SoundEffects]
        (
            [SoundEffectId]  uniqueidentifier NOT NULL
                CONSTRAINT [DF_SoundEffects_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]      uniqueidentifier NOT NULL,
            [SceneId]        uniqueidentifier NULL,
            [MediaAssetId]   uniqueidentifier NOT NULL,
            [CueTimeMs]      bigint NOT NULL,
            [GainDb]         decimal(7,3) NOT NULL CONSTRAINT [DF_SoundEffects_Gain] DEFAULT (0),
            [Description]    nvarchar(1000) NULL,
            [CreatedAtUtc]   datetime2(3) NOT NULL
                CONSTRAINT [DF_SoundEffects_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_SoundEffects] PRIMARY KEY CLUSTERED ([SoundEffectId]),
            CONSTRAINT [FK_SoundEffects_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_SoundEffects_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_SoundEffects_MediaAssets]
                FOREIGN KEY ([MediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [CK_SoundEffects_CueTime] CHECK ([CueTimeMs] >= 0)
        );
    END;

    /* ================================================================
       7. Rendering and final outputs
       ================================================================ */

    IF OBJECT_ID(N'[vf].[RenderJobs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[RenderJobs]
        (
            [RenderJobId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_RenderJobs_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]         uniqueidentifier NOT NULL,
            [JobId]             uniqueidentifier NULL,
            [Version]           int NOT NULL,
            [Status]            varchar(30) NOT NULL CONSTRAINT [DF_RenderJobs_Status] DEFAULT ('Pending'),
            [ManifestJson]      nvarchar(max) NOT NULL,
            [ManifestHash]      char(64) NOT NULL,
            [FfmpegVersion]     nvarchar(200) NULL,
            [ProgressPercent]   decimal(5,2) NOT NULL CONSTRAINT [DF_RenderJobs_Progress] DEFAULT (0),
            [OutputMediaAssetId] uniqueidentifier NULL,
            [TechnicalReportJson] nvarchar(max) NULL,
            [ErrorCode]         varchar(100) NULL,
            [ErrorMessage]      nvarchar(4000) NULL,
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_RenderJobs_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [StartedAtUtc]      datetime2(3) NULL,
            [CompletedAtUtc]    datetime2(3) NULL,
            [RowVersion]        rowversion NOT NULL,
            CONSTRAINT [PK_RenderJobs] PRIMARY KEY CLUSTERED ([RenderJobId]),
            CONSTRAINT [FK_RenderJobs_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_RenderJobs_Jobs]
                FOREIGN KEY ([JobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [FK_RenderJobs_OutputAsset]
                FOREIGN KEY ([OutputMediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [UQ_RenderJobs_Project_Version] UNIQUE ([ProjectId], [Version]),
            CONSTRAINT [CK_RenderJobs_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_RenderJobs_Status]
                CHECK ([Status] IN ('Pending','ValidatingInputs','Rendering','ValidatingOutput','Completed','Failed','Cancelled')),
            CONSTRAINT [CK_RenderJobs_Progress]
                CHECK ([ProgressPercent] BETWEEN 0 AND 100),
            CONSTRAINT [CK_RenderJobs_ManifestJson] CHECK (ISJSON([ManifestJson]) = 1),
            CONSTRAINT [CK_RenderJobs_TechnicalReportJson]
                CHECK ([TechnicalReportJson] IS NULL OR ISJSON([TechnicalReportJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[FinalVideos]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[FinalVideos]
        (
            [FinalVideoId]     uniqueidentifier NOT NULL
                CONSTRAINT [DF_FinalVideos_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]        uniqueidentifier NOT NULL,
            [RenderJobId]      uniqueidentifier NOT NULL,
            [MediaAssetId]     uniqueidentifier NOT NULL,
            [Version]          int NOT NULL,
            [Status]           varchar(20) NOT NULL CONSTRAINT [DF_FinalVideos_Status] DEFAULT ('AwaitingApproval'),
            [QualityScore]     decimal(5,2) NULL,
            [QualityReportJson] nvarchar(max) NULL,
            [ExportedPath]     nvarchar(1000) NULL,
            [CreatedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_FinalVideos_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ApprovedAtUtc]    datetime2(3) NULL,
            [ExportedAtUtc]    datetime2(3) NULL,
            [RowVersion]       rowversion NOT NULL,
            CONSTRAINT [PK_FinalVideos] PRIMARY KEY CLUSTERED ([FinalVideoId]),
            CONSTRAINT [FK_FinalVideos_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_FinalVideos_RenderJobs]
                FOREIGN KEY ([RenderJobId]) REFERENCES [vf].[RenderJobs]([RenderJobId]),
            CONSTRAINT [FK_FinalVideos_MediaAssets]
                FOREIGN KEY ([MediaAssetId]) REFERENCES [vf].[MediaAssets]([MediaAssetId]),
            CONSTRAINT [UQ_FinalVideos_Project_Version] UNIQUE ([ProjectId], [Version]),
            CONSTRAINT [CK_FinalVideos_Version] CHECK ([Version] > 0),
            CONSTRAINT [CK_FinalVideos_Status]
                CHECK ([Status] IN ('AwaitingApproval','Approved','Rejected','Exported','Invalid')),
            CONSTRAINT [CK_FinalVideos_QualityScore]
                CHECK ([QualityScore] IS NULL OR [QualityScore] BETWEEN 0 AND 100),
            CONSTRAINT [CK_FinalVideos_QualityReportJson]
                CHECK ([QualityReportJson] IS NULL OR ISJSON([QualityReportJson]) = 1)
        );
    END;

    /* ================================================================
       8. Cost ledger and approvals
       ================================================================ */

    IF OBJECT_ID(N'[vf].[UsageCosts]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[UsageCosts]
        (
            [UsageCostId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_UsageCosts_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]         uniqueidentifier NOT NULL,
            [SceneId]           uniqueidentifier NULL,
            [JobId]             uniqueidentifier NULL,
            [ProviderRequestId] uniqueidentifier NULL,
            [UsageKey]          nvarchar(450) NOT NULL,
            [CostKind]          varchar(20) NOT NULL,
            [ProviderCode]      varchar(80) NULL,
            [ModelCode]         nvarchar(200) NULL,
            [UsageType]         varchar(50) NOT NULL,
            [Quantity]          decimal(19,6) NOT NULL,
            [Unit]              varchar(30) NOT NULL,
            [UnitPrice]         decimal(19,8) NOT NULL,
            [TotalCost]         decimal(19,6) NOT NULL,
            [CurrencyCode]      char(3) NOT NULL CONSTRAINT [DF_UsageCosts_Currency] DEFAULT ('USD'),
            [RateSnapshotJson]  nvarchar(max) NULL,
            [OccurredAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_UsageCosts_OccurredAtUtc] DEFAULT SYSUTCDATETIME(),
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_UsageCosts_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_UsageCosts] PRIMARY KEY CLUSTERED ([UsageCostId]),
            CONSTRAINT [FK_UsageCosts_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [FK_UsageCosts_Scenes]
                FOREIGN KEY ([SceneId]) REFERENCES [vf].[Scenes]([SceneId]),
            CONSTRAINT [FK_UsageCosts_Jobs]
                FOREIGN KEY ([JobId]) REFERENCES [vf].[Jobs]([JobId]),
            CONSTRAINT [FK_UsageCosts_ProviderRequests]
                FOREIGN KEY ([ProviderRequestId]) REFERENCES [vf].[ProviderRequests]([ProviderRequestId]),
            CONSTRAINT [UQ_UsageCosts_UsageKey] UNIQUE ([UsageKey]),
            CONSTRAINT [CK_UsageCosts_CostKind]
                CHECK ([CostKind] IN ('Estimate','Reservation','Actual','Adjustment','Refund','Release')),
            CONSTRAINT [CK_UsageCosts_Quantity] CHECK ([Quantity] >= 0),
            CONSTRAINT [CK_UsageCosts_UnitPrice] CHECK ([UnitPrice] >= 0),
            CONSTRAINT [CK_UsageCosts_RateSnapshotJson]
                CHECK ([RateSnapshotJson] IS NULL OR ISJSON([RateSnapshotJson]) = 1)
        );
    END;

    IF OBJECT_ID(N'[vf].[Approvals]', N'U') IS NULL
    BEGIN
        CREATE TABLE [vf].[Approvals]
        (
            [ApprovalId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_Approvals_Id] DEFAULT NEWSEQUENTIALID(),
            [ProjectId]        uniqueidentifier NOT NULL,
            [TargetType]       varchar(40) NOT NULL,
            [TargetId]         uniqueidentifier NOT NULL,
            [TargetVersion]    int NULL,
            [Decision]         varchar(20) NOT NULL,
            [Comment]          nvarchar(2000) NULL,
            [ApprovedBy]       nvarchar(200) NULL,
            [DecidedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_Approvals_DecidedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_Approvals] PRIMARY KEY CLUSTERED ([ApprovalId]),
            CONSTRAINT [FK_Approvals_Projects]
                FOREIGN KEY ([ProjectId]) REFERENCES [vf].[Projects]([ProjectId]),
            CONSTRAINT [CK_Approvals_TargetType]
                CHECK ([TargetType] IN ('Concept','Script','Character','Style','Storyboard','Scene','FinalVideo')),
            CONSTRAINT [CK_Approvals_Decision]
                CHECK ([Decision] IN ('Approved','Rejected','Revoked'))
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [vf].[SchemaVersions] WHERE [Version] = '2.1.0-video-schema'
    )
    BEGIN
        INSERT INTO [vf].[SchemaVersions] ([Version], [Description])
        VALUES ('2.1.0-video-schema', N'WinForms AI/video workflow in the shared VideoFactory database.');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [vf].[SchemaVersions] WHERE [Version] = '2.2.0-provider-admin'
    )
    BEGIN
        INSERT INTO [vf].[SchemaVersions] ([Version], [Description])
        VALUES ('2.2.0-provider-admin', N'Encrypted provider credential management and Admin portal support.');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [vf].[SchemaVersions] WHERE [Version] = '3.0.0-license-byok'
    )
    BEGIN
        INSERT INTO [vf].[SchemaVersions] ([Version], [Description])
        VALUES ('3.0.0-license-byok', N'Desktop BYOK provider runtime with backward-compatible project schema upgrades.');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

/* ====================================================================
   9. Performance and uniqueness indexes
   ==================================================================== */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Projects]') AND name = N'IX_Projects_Status_UpdatedAt')
    CREATE INDEX [IX_Projects_Status_UpdatedAt]
        ON [vf].[Projects] ([Status], [UpdatedAtUtc] DESC)
        INCLUDE ([Name], [Topic], [ActualCost], [BudgetLimit]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Projects]') AND name = N'IX_Projects_RemoteUser_Status')
    CREATE INDEX [IX_Projects_RemoteUser_Status]
        ON [vf].[Projects] ([RemoteUserId], [Status], [UpdatedAtUtc] DESC)
        INCLUDE ([Name], [Topic], [RemoteDeviceId], [ActualCost])
        WHERE [RemoteUserId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Scenes]') AND name = N'IX_Scenes_Project_Status_Sequence')
    CREATE INDEX [IX_Scenes_Project_Status_Sequence]
        ON [vf].[Scenes] ([ProjectId], [Status], [SequenceNumber])
        INCLUDE ([ScenePlanVersion], [ContentDurationMs], [GenerationDurationMs]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Scenes]') AND name = N'IX_Scenes_Dependency')
    CREATE INDEX [IX_Scenes_Dependency]
        ON [vf].[Scenes] ([GenerationDependencySceneId])
        WHERE [GenerationDependencySceneId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[MediaAssets]') AND name = N'IX_MediaAssets_Project_Type_Status')
    CREATE INDEX [IX_MediaAssets_Project_Type_Status]
        ON [vf].[MediaAssets] ([ProjectId], [AssetType], [Status])
        INCLUDE ([SceneId], [RelativePath], [DurationMs], [SizeBytes]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Jobs]') AND name = N'UX_Jobs_IdempotencyKey')
    CREATE UNIQUE INDEX [UX_Jobs_IdempotencyKey]
        ON [vf].[Jobs] ([IdempotencyKey])
        WHERE [IdempotencyKey] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Jobs]') AND name = N'IX_Jobs_ClaimQueue')
    CREATE INDEX [IX_Jobs_ClaimQueue]
        ON [vf].[Jobs] ([Status], [AvailableAtUtc], [Priority] DESC, [CreatedAtUtc])
        INCLUDE ([ProjectId], [SceneId], [JobType], [Attempt], [MaxAttempts], [LeaseExpiresAtUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Jobs]') AND name = N'IX_Jobs_Project_Status')
    CREATE INDEX [IX_Jobs_Project_Status]
        ON [vf].[Jobs] ([ProjectId], [Status])
        INCLUDE ([JobType], [ProgressPercent], [AvailableAtUtc], [ErrorCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[JobDependencies]') AND name = N'IX_JobDependencies_DependsOn')
    CREATE INDEX [IX_JobDependencies_DependsOn]
        ON [vf].[JobDependencies] ([DependsOnJobId], [JobId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[JobEvents]') AND name = N'IX_JobEvents_Job_OccurredAt')
    CREATE INDEX [IX_JobEvents_Job_OccurredAt]
        ON [vf].[JobEvents] ([JobId], [OccurredAtUtc] DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[ProviderRequests]') AND name = N'UX_ProviderRequests_ExternalRequest')
    CREATE UNIQUE INDEX [UX_ProviderRequests_ExternalRequest]
        ON [vf].[ProviderRequests] ([ProviderCode], [ExternalRequestId])
        WHERE [ExternalRequestId] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[ProviderRequests]') AND name = N'IX_ProviderRequests_PollDue')
    CREATE INDEX [IX_ProviderRequests_PollDue]
        ON [vf].[ProviderRequests] ([Status], [NextPollAtUtc])
        INCLUDE ([ProjectId], [SceneId], [JobId], [ProviderCode], [ExternalRequestId], [PollCount]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[ProviderCredentials]') AND name = N'UX_ProviderCredentials_ActiveProvider')
    CREATE UNIQUE INDEX [UX_ProviderCredentials_ActiveProvider]
        ON [vf].[ProviderCredentials] ([ProviderId])
        WHERE [IsActive] = 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[ProviderCredentials]') AND name = N'IX_ProviderCredentials_TestStatus')
    CREATE INDEX [IX_ProviderCredentials_TestStatus]
        ON [vf].[ProviderCredentials] ([TestStatus], [LastTestedAtUtc] DESC)
        INCLUDE ([ProviderId], [Name], [IsActive], [SecretHint]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[VideoGenerations]') AND name = N'IX_VideoGenerations_Scene_Status')
    CREATE INDEX [IX_VideoGenerations_Scene_Status]
        ON [vf].[VideoGenerations] ([SceneId], [Status], [AttemptNumber] DESC)
        INCLUDE ([ProviderRequestId], [OutputMediaAssetId], [QualityScore]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[UsageCosts]') AND name = N'IX_UsageCosts_Project_OccurredAt')
    CREATE INDEX [IX_UsageCosts_Project_OccurredAt]
        ON [vf].[UsageCosts] ([ProjectId], [OccurredAtUtc] DESC)
        INCLUDE ([CostKind], [ProviderCode], [ModelCode], [UsageType], [TotalCost], [CurrencyCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[vf].[Approvals]') AND name = N'IX_Approvals_Target')
    CREATE INDEX [IX_Approvals_Target]
        ON [vf].[Approvals] ([ProjectId], [TargetType], [TargetId], [DecidedAtUtc] DESC)
        INCLUDE ([TargetVersion], [Decision]);
GO

/* ====================================================================
   10. Stored procedures for the persistent local job runner
   ==================================================================== */

CREATE OR ALTER PROCEDURE [vf].[usp_ClaimNextJob]
    @WorkerId     nvarchar(200),
    @LeaseSeconds int = 120
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(RTRIM(@WorkerId)), N'') IS NULL
        THROW 50001, 'WorkerId is required.', 1;

    IF @LeaseSeconds < 10 OR @LeaseSeconds > 3600
        THROW 50002, 'LeaseSeconds must be between 10 and 3600.', 1;

    DECLARE @NowUtc datetime2(3) = SYSUTCDATETIME();
    DECLARE @JobId uniqueidentifier;
    DECLARE @PreviousStatus varchar(30);

    BEGIN TRANSACTION;

    SELECT TOP (1)
        @JobId = [j].[JobId],
        @PreviousStatus = [j].[Status]
    FROM [vf].[Jobs] AS [j] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE [j].[Status] IN ('Pending','RetryScheduled','Interrupted')
      AND [j].[AvailableAtUtc] <= @NowUtc
      AND
      (
          ([j].[Status] IN ('Pending','RetryScheduled') AND [j].[Attempt] < [j].[MaxAttempts])
          OR
          ([j].[Status] = 'Interrupted' AND [j].[Attempt] <= [j].[MaxAttempts])
      )
      AND ([j].[LeaseExpiresAtUtc] IS NULL OR [j].[LeaseExpiresAtUtc] <= @NowUtc)
      AND NOT EXISTS
      (
          SELECT 1
          FROM [vf].[JobDependencies] AS [d]
          INNER JOIN [vf].[Jobs] AS [required]
              ON [required].[JobId] = [d].[DependsOnJobId]
          WHERE [d].[JobId] = [j].[JobId]
            AND [required].[Status] <> 'Completed'
      )
      AND EXISTS
      (
          SELECT 1
          FROM [vf].[Projects] AS [p]
          WHERE [p].[ProjectId] = [j].[ProjectId]
            AND [p].[Status] NOT IN ('Paused','Failed','Cancelled','Completed')
            AND [p].[DeletedAtUtc] IS NULL
      )
    ORDER BY [j].[Priority] DESC, [j].[AvailableAtUtc], [j].[CreatedAtUtc];

    IF @JobId IS NOT NULL
    BEGIN
        UPDATE [vf].[Jobs]
        SET [Status]            = 'Running',
            [Attempt]           = CASE WHEN @PreviousStatus = 'Interrupted' THEN [Attempt] ELSE [Attempt] + 1 END,
            [LockedBy]          = @WorkerId,
            [LockedAtUtc]       = @NowUtc,
            [HeartbeatAtUtc]    = @NowUtc,
            [LeaseExpiresAtUtc] = DATEADD(SECOND, @LeaseSeconds, @NowUtc),
            [StartedAtUtc]      = COALESCE([StartedAtUtc], @NowUtc),
            [UpdatedAtUtc]      = @NowUtc,
            [ErrorCode]         = NULL,
            [ErrorMessage]      = NULL
        OUTPUT
            [inserted].[JobId],
            [inserted].[ProjectId],
            [inserted].[SceneId],
            [inserted].[ParentJobId],
            [inserted].[JobType],
            [inserted].[Status],
            [inserted].[Priority],
            [inserted].[Attempt],
            [inserted].[MaxAttempts],
            [inserted].[ProgressPercent],
            [inserted].[AvailableAtUtc],
            [inserted].[LockedBy],
            [inserted].[LockedAtUtc],
            [inserted].[LeaseExpiresAtUtc],
            [inserted].[IdempotencyKey],
            [inserted].[PayloadJson],
            [inserted].[RowVersion]
        WHERE [JobId] = @JobId;

        INSERT INTO [vf].[JobEvents]
            ([JobId], [EventType], [FromStatus], [ToStatus], [Message])
        VALUES
            (@JobId, 'Claimed', @PreviousStatus, 'Running', CONCAT(N'Claimed by worker ', @WorkerId));
    END;

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE [vf].[usp_HeartbeatJob]
    @JobId        uniqueidentifier,
    @WorkerId     nvarchar(200),
    @LeaseSeconds int = 120,
    @ProgressPercent decimal(5,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @LeaseSeconds < 10 OR @LeaseSeconds > 3600
        THROW 50003, 'LeaseSeconds must be between 10 and 3600.', 1;

    IF @ProgressPercent IS NOT NULL AND (@ProgressPercent < 0 OR @ProgressPercent > 100)
        THROW 50004, 'ProgressPercent must be between 0 and 100.', 1;

    DECLARE @NowUtc datetime2(3) = SYSUTCDATETIME();

    UPDATE [vf].[Jobs]
    SET [HeartbeatAtUtc]     = @NowUtc,
        [LeaseExpiresAtUtc]  = DATEADD(SECOND, @LeaseSeconds, @NowUtc),
        [ProgressPercent]    = COALESCE(@ProgressPercent, [ProgressPercent]),
        [UpdatedAtUtc]       = @NowUtc
    WHERE [JobId] = @JobId
      AND [Status] = 'Running'
      AND [LockedBy] = @WorkerId;

    SELECT @@ROWCOUNT AS [UpdatedRows];
END;
GO

CREATE OR ALTER PROCEDURE [vf].[usp_RecoverExpiredJobs]
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NowUtc datetime2(3) = SYSUTCDATETIME();
    DECLARE @Recovered TABLE ([JobId] uniqueidentifier NOT NULL);

    BEGIN TRANSACTION;

    UPDATE [vf].[Jobs]
    SET [Status]            = 'Interrupted',
        [AvailableAtUtc]    = @NowUtc,
        [LockedBy]          = NULL,
        [LockedAtUtc]       = NULL,
        [HeartbeatAtUtc]    = NULL,
        [LeaseExpiresAtUtc] = NULL,
        [ErrorCode]         = 'WORKER_LEASE_EXPIRED',
        [ErrorMessage]      = N'The previous local worker stopped before completing the job.',
        [UpdatedAtUtc]      = @NowUtc
    OUTPUT [inserted].[JobId] INTO @Recovered ([JobId])
    WHERE [Status] = 'Running'
      AND [LeaseExpiresAtUtc] IS NOT NULL
      AND [LeaseExpiresAtUtc] < @NowUtc;

    INSERT INTO [vf].[JobEvents]
        ([JobId], [EventType], [FromStatus], [ToStatus], [Message])
    SELECT
        [JobId], 'Recovered', 'Running', 'Interrupted',
        N'Recovered after the local worker lease expired.'
    FROM @Recovered;

    COMMIT TRANSACTION;

    SELECT [JobId] FROM @Recovered;
END;
GO

CREATE OR ALTER PROCEDURE [vf].[usp_RecalculateProjectCosts]
    @ProjectId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Estimated decimal(19,6);
    DECLARE @Actual decimal(19,6);

    SELECT
        @Estimated = COALESCE(SUM(CASE WHEN [CostKind] = 'Estimate' THEN [TotalCost] ELSE 0 END), 0),
        @Actual = COALESCE(SUM(CASE WHEN [CostKind] IN ('Actual','Adjustment','Refund') THEN [TotalCost] ELSE 0 END), 0)
    FROM [vf].[UsageCosts]
    WHERE [ProjectId] = @ProjectId;

    UPDATE [vf].[Projects]
    SET [EstimatedCost] = CASE WHEN @Estimated < 0 THEN 0 ELSE @Estimated END,
        [ActualCost]    = CASE WHEN @Actual < 0 THEN 0 ELSE @Actual END,
        [UpdatedAtUtc]  = SYSUTCDATETIME()
    WHERE [ProjectId] = @ProjectId;

    SELECT [ProjectId], [EstimatedCost], [ActualCost], [BudgetLimit], [CurrencyCode]
    FROM [vf].[Projects]
    WHERE [ProjectId] = @ProjectId;
END;
GO

/* ====================================================================
   11. Read model for the WinForms dashboard
   ==================================================================== */

CREATE OR ALTER VIEW [vf].[vw_ProjectProgress]
AS
    SELECT
        [p].[ProjectId],
        [p].[RemoteUserId],
        [p].[RemoteDeviceId],
        [p].[Name],
        [p].[Topic],
        [p].[Status],
        [p].[TargetDurationSeconds],
        [p].[EstimatedCost],
        [p].[ActualCost],
        [p].[BudgetLimit],
        [p].[CurrencyCode],
        ISNULL([s].[TotalScenes], 0) AS [TotalScenes],
        ISNULL([s].[ApprovedScenes], 0) AS [ApprovedScenes],
        ISNULL([s].[FailedScenes], 0) AS [FailedScenes],
        ISNULL([j].[PendingJobs], 0) AS [PendingJobs],
        ISNULL([j].[RunningJobs], 0) AS [RunningJobs],
        ISNULL([j].[WaitingProviderJobs], 0) AS [WaitingProviderJobs],
        ISNULL([j].[FailedJobs], 0) AS [FailedJobs],
        [p].[CreatedAtUtc],
        [p].[UpdatedAtUtc]
    FROM [vf].[Projects] AS [p]
    OUTER APPLY
    (
        SELECT
            COUNT_BIG(*) AS [TotalScenes],
            SUM(CASE WHEN [Status] = 'Approved' THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [ApprovedScenes],
            SUM(CASE WHEN [Status] = 'Failed' THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [FailedScenes]
        FROM [vf].[Scenes]
        WHERE [ProjectId] = [p].[ProjectId]
          AND [ScenePlanVersion] = [p].[CurrentScenePlanVersion]
    ) AS [s]
    OUTER APPLY
    (
        SELECT
            SUM(CASE WHEN [Status] IN ('Pending','RetryScheduled','Interrupted') THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [PendingJobs],
            SUM(CASE WHEN [Status] = 'Running' THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [RunningJobs],
            SUM(CASE WHEN [Status] = 'WaitingProvider' THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [WaitingProviderJobs],
            SUM(CASE WHEN [Status] = 'Failed' THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [FailedJobs]
        FROM [vf].[Jobs]
        WHERE [ProjectId] = [p].[ProjectId]
    ) AS [j]
    WHERE [p].[DeletedAtUtc] IS NULL;
GO

PRINT N'VideoFactory [vf] video schema 3.0.0-license-byok is ready.';

SELECT
    DB_NAME() AS [DatabaseName],
    (SELECT COUNT(*) FROM sys.tables WHERE [schema_id] = SCHEMA_ID(N'vf')) AS [ApplicationTableCount],
    (SELECT COUNT(*) FROM sys.procedures WHERE [schema_id] = SCHEMA_ID(N'vf')) AS [ApplicationProcedureCount],
    (SELECT COUNT(*) FROM sys.views WHERE [schema_id] = SCHEMA_ID(N'vf')) AS [ApplicationViewCount];

SELECT [Version], [Description], [AppliedAtUtc]
FROM [vf].[SchemaVersions]
ORDER BY [SchemaVersionId] DESC;
GO

/* ====================================================================
   ACCOUNT SERVER SCHEMAS IN THE SHARED DATABASE

   Ownership boundary:
     - TOOL-LOCAL owns [vf] video data.
     - TOOL-SERVER owns [auth] and ASP.NET Identity [dbo] tables.
     - TOOL-LOCAL still calls TOOL-SERVER through HTTPS for authentication;
       sharing one physical database does not authorize Desktop code to read
       or update Identity tables directly.
   ==================================================================== */

USE [VideoFactory];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'auth') IS NULL
BEGIN
    EXEC(N'CREATE SCHEMA [auth] AUTHORIZATION [dbo];');
END;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* ================================================================
       1. Account schema version and non-secret server configuration
       ================================================================ */

    IF OBJECT_ID(N'[auth].[SchemaVersions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[SchemaVersions]
        (
            [SchemaVersionId] int IDENTITY(1,1) NOT NULL,
            [Version]         varchar(50) NOT NULL,
            [Description]     nvarchar(500) NULL,
            [AppliedAtUtc]    datetime2(3) NOT NULL
                CONSTRAINT [DF_AuthSchemaVersions_AppliedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_AuthSchemaVersions] PRIMARY KEY CLUSTERED ([SchemaVersionId]),
            CONSTRAINT [UQ_AuthSchemaVersions_Version] UNIQUE ([Version])
        );
    END;

    IF OBJECT_ID(N'[auth].[ServerSettings]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[ServerSettings]
        (
            [ServerSettingId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_ServerSettings_Id] DEFAULT NEWSEQUENTIALID(),
            [SettingKey]      nvarchar(200) NOT NULL,
            [ValueJson]       nvarchar(max) NULL,
            [Description]     nvarchar(1000) NULL,
            [UpdatedAtUtc]    datetime2(3) NOT NULL
                CONSTRAINT [DF_ServerSettings_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]      rowversion NOT NULL,
            CONSTRAINT [PK_ServerSettings] PRIMARY KEY CLUSTERED ([ServerSettingId]),
            CONSTRAINT [UQ_ServerSettings_SettingKey] UNIQUE ([SettingKey]),
            CONSTRAINT [CK_ServerSettings_ValueJson]
                CHECK ([ValueJson] IS NULL OR ISJSON([ValueJson]) = 1)
        );
    END;

    /* Secrets such as JWT signing keys, SMTP passwords and connection
       strings must be supplied by environment/secret storage, not here. */

    /* ================================================================
       2. ASP.NET Core Identity-compatible tables
       ================================================================ */

    IF OBJECT_ID(N'[dbo].[AspNetRoles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetRoles]
        (
            [Id]               nvarchar(450) NOT NULL,
            [Name]             nvarchar(256) NULL,
            [NormalizedName]   nvarchar(256) NULL,
            [ConcurrencyStamp] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetRoles] PRIMARY KEY CLUSTERED ([Id])
        );
    END;

    IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetUsers]
        (
            [Id]                       nvarchar(450) NOT NULL,
            [UserName]                 nvarchar(256) NULL,
            [NormalizedUserName]       nvarchar(256) NULL,
            [Email]                    nvarchar(256) NULL,
            [NormalizedEmail]          nvarchar(256) NULL,
            [EmailConfirmed]           bit NOT NULL,
            [PasswordHash]             nvarchar(max) NULL,
            [SecurityStamp]            nvarchar(max) NULL,
            [ConcurrencyStamp]         nvarchar(max) NULL,
            [PhoneNumber]              nvarchar(max) NULL,
            [PhoneNumberConfirmed]     bit NOT NULL,
            [TwoFactorEnabled]         bit NOT NULL,
            [LockoutEnd]               datetimeoffset(7) NULL,
            [LockoutEnabled]           bit NOT NULL,
            [AccessFailedCount]        int NOT NULL,
            [DisplayName]              nvarchar(200) NULL,
            [AccountStatus]            varchar(30) NOT NULL
                CONSTRAINT [DF_AspNetUsers_AccountStatus] DEFAULT ('Active'),
            [PreferredLanguageCode]    varchar(10) NOT NULL
                CONSTRAINT [DF_AspNetUsers_Language] DEFAULT ('vi-VN'),
            [TimeZoneId]               nvarchar(100) NOT NULL
                CONSTRAINT [DF_AspNetUsers_TimeZone] DEFAULT ('SE Asia Standard Time'),
            [LastLoginAtUtc]           datetime2(3) NULL,
            [PasswordChangedAtUtc]     datetime2(3) NULL,
            [TermsAcceptedAtUtc]       datetime2(3) NULL,
            [CreatedAtUtc]             datetime2(3) NOT NULL
                CONSTRAINT [DF_AspNetUsers_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]             datetime2(3) NOT NULL
                CONSTRAINT [DF_AspNetUsers_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [DeletedAtUtc]             datetime2(3) NULL,
            [RowVersion]               rowversion NOT NULL,
            CONSTRAINT [PK_AspNetUsers] PRIMARY KEY CLUSTERED ([Id]),
            CONSTRAINT [CK_AspNetUsers_AccountStatus]
                CHECK ([AccountStatus] IN ('PendingVerification','Active','Locked','Suspended','Deleted')),
            CONSTRAINT [CK_AspNetUsers_AccessFailedCount]
                CHECK ([AccessFailedCount] >= 0)
        );
    END;

    /* Extend an existing standard ASP.NET Identity user table safely. */
    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'DisplayName') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [DisplayName] nvarchar(200) NULL;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'AccountStatus') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [AccountStatus] varchar(30) NOT NULL
            CONSTRAINT [DF_AspNetUsers_AccountStatus] DEFAULT ('Active') WITH VALUES;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'PreferredLanguageCode') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [PreferredLanguageCode] varchar(10) NOT NULL
            CONSTRAINT [DF_AspNetUsers_Language] DEFAULT ('vi-VN') WITH VALUES;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'TimeZoneId') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [TimeZoneId] nvarchar(100) NOT NULL
            CONSTRAINT [DF_AspNetUsers_TimeZone] DEFAULT ('SE Asia Standard Time') WITH VALUES;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'LastLoginAtUtc') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [LastLoginAtUtc] datetime2(3) NULL;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'PasswordChangedAtUtc') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [PasswordChangedAtUtc] datetime2(3) NULL;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'TermsAcceptedAtUtc') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [TermsAcceptedAtUtc] datetime2(3) NULL;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'CreatedAtUtc') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [CreatedAtUtc] datetime2(3) NOT NULL
            CONSTRAINT [DF_AspNetUsers_CreatedAtUtc] DEFAULT SYSUTCDATETIME() WITH VALUES;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'UpdatedAtUtc') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [UpdatedAtUtc] datetime2(3) NOT NULL
            CONSTRAINT [DF_AspNetUsers_UpdatedAtUtc] DEFAULT SYSUTCDATETIME() WITH VALUES;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'DeletedAtUtc') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [DeletedAtUtc] datetime2(3) NULL;

    IF COL_LENGTH(N'[dbo].[AspNetUsers]', N'RowVersion') IS NULL
        ALTER TABLE [dbo].[AspNetUsers] ADD [RowVersion] rowversion NOT NULL;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[AspNetUsers]')
          AND [name] = N'CK_AspNetUsers_AccountStatus'
    )
        ALTER TABLE [dbo].[AspNetUsers] WITH CHECK
            ADD CONSTRAINT [CK_AspNetUsers_AccountStatus]
            CHECK ([AccountStatus] IN ('PendingVerification','Active','Locked','Suspended','Deleted'));

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.check_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[dbo].[AspNetUsers]')
          AND [name] = N'CK_AspNetUsers_AccessFailedCount'
    )
        ALTER TABLE [dbo].[AspNetUsers] WITH CHECK
            ADD CONSTRAINT [CK_AspNetUsers_AccessFailedCount]
            CHECK ([AccessFailedCount] >= 0);

    IF OBJECT_ID(N'[dbo].[AspNetRoleClaims]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetRoleClaims]
        (
            [Id]        int IDENTITY(1,1) NOT NULL,
            [RoleId]    nvarchar(450) NOT NULL,
            [ClaimType] nvarchar(max) NULL,
            [ClaimValue] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY CLUSTERED ([Id]),
            CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
                FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles]([Id]) ON DELETE CASCADE
        );
    END;

    IF OBJECT_ID(N'[dbo].[AspNetUserClaims]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetUserClaims]
        (
            [Id]         int IDENTITY(1,1) NOT NULL,
            [UserId]     nvarchar(450) NOT NULL,
            [ClaimType]  nvarchar(max) NULL,
            [ClaimValue] nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY CLUSTERED ([Id]),
            CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE
        );
    END;

    IF OBJECT_ID(N'[dbo].[AspNetUserLogins]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetUserLogins]
        (
            [LoginProvider]       nvarchar(128) NOT NULL,
            [ProviderKey]         nvarchar(128) NOT NULL,
            [ProviderDisplayName] nvarchar(max) NULL,
            [UserId]              nvarchar(450) NOT NULL,
            CONSTRAINT [PK_AspNetUserLogins]
                PRIMARY KEY CLUSTERED ([LoginProvider], [ProviderKey]),
            CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE
        );
    END;

    IF OBJECT_ID(N'[dbo].[AspNetUserRoles]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetUserRoles]
        (
            [UserId] nvarchar(450) NOT NULL,
            [RoleId] nvarchar(450) NOT NULL,
            CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY CLUSTERED ([UserId], [RoleId]),
            CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE,
            CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId]
                FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles]([Id]) ON DELETE CASCADE
        );
    END;

    IF OBJECT_ID(N'[dbo].[AspNetUserTokens]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AspNetUserTokens]
        (
            [UserId]       nvarchar(450) NOT NULL,
            [LoginProvider] nvarchar(128) NOT NULL,
            [Name]          nvarchar(128) NOT NULL,
            [Value]         nvarchar(max) NULL,
            CONSTRAINT [PK_AspNetUserTokens]
                PRIMARY KEY CLUSTERED ([UserId], [LoginProvider], [Name]),
            CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]) ON DELETE CASCADE
        );
    END;

    /* Used when ASP.NET Core Data Protection keys are persisted with EF.
       Protect this database and its backups as sensitive data. */
    IF OBJECT_ID(N'[dbo].[DataProtectionKeys]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[DataProtectionKeys]
        (
            [Id]           int IDENTITY(1,1) NOT NULL,
            [FriendlyName] nvarchar(max) NULL,
            [Xml]          nvarchar(max) NULL,
            CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY CLUSTERED ([Id])
        );
    END;

    /* ================================================================
       3. Desktop devices, login sessions and refresh-token rotation
       ================================================================ */

    IF OBJECT_ID(N'[auth].[RegisteredDevices]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[RegisteredDevices]
        (
            [DeviceId]              uniqueidentifier NOT NULL
                CONSTRAINT [DF_RegisteredDevices_Id] DEFAULT NEWSEQUENTIALID(),
            [UserId]                nvarchar(450) NOT NULL,
            [DeviceFingerprintHash] binary(32) NOT NULL,
            [DeviceName]            nvarchar(200) NOT NULL,
            [OperatingSystem]       nvarchar(200) NULL,
            [ApplicationVersion]    nvarchar(50) NULL,
            [IsTrusted]             bit NOT NULL
                CONSTRAINT [DF_RegisteredDevices_IsTrusted] DEFAULT (0),
            [IsRevoked]             bit NOT NULL
                CONSTRAINT [DF_RegisteredDevices_IsRevoked] DEFAULT (0),
            [RevokedReason]         nvarchar(500) NULL,
            [FirstSeenAtUtc]        datetime2(3) NOT NULL
                CONSTRAINT [DF_RegisteredDevices_FirstSeenAtUtc] DEFAULT SYSUTCDATETIME(),
            [LastSeenAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_RegisteredDevices_LastSeenAtUtc] DEFAULT SYSUTCDATETIME(),
            [RevokedAtUtc]          datetime2(3) NULL,
            [RowVersion]            rowversion NOT NULL,
            CONSTRAINT [PK_RegisteredDevices] PRIMARY KEY CLUSTERED ([DeviceId]),
            CONSTRAINT [FK_RegisteredDevices_AspNetUsers]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [UQ_RegisteredDevices_User_Fingerprint]
                UNIQUE ([UserId], [DeviceFingerprintHash]),
            CONSTRAINT [CK_RegisteredDevices_Revocation]
                CHECK (([IsRevoked] = 0 AND [RevokedAtUtc] IS NULL)
                    OR ([IsRevoked] = 1 AND [RevokedAtUtc] IS NOT NULL))
        );
    END;

    IF OBJECT_ID(N'[auth].[UserSessions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[UserSessions]
        (
            [SessionId]        uniqueidentifier NOT NULL
                CONSTRAINT [DF_UserSessions_Id] DEFAULT NEWSEQUENTIALID(),
            [UserId]           nvarchar(450) NOT NULL,
            [DeviceId]         uniqueidentifier NULL,
            [Status]           varchar(20) NOT NULL
                CONSTRAINT [DF_UserSessions_Status] DEFAULT ('Active'),
            [StartedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_UserSessions_StartedAtUtc] DEFAULT SYSUTCDATETIME(),
            [LastSeenAtUtc]    datetime2(3) NOT NULL
                CONSTRAINT [DF_UserSessions_LastSeenAtUtc] DEFAULT SYSUTCDATETIME(),
            [AbsoluteExpiresAtUtc] datetime2(3) NOT NULL,
            [RevokedAtUtc]     datetime2(3) NULL,
            [RevokedReason]    nvarchar(500) NULL,
            [IpAddress]        varchar(45) NULL,
            [UserAgent]        nvarchar(1000) NULL,
            [ApplicationVersion] nvarchar(50) NULL,
            [RowVersion]       rowversion NOT NULL,
            CONSTRAINT [PK_UserSessions] PRIMARY KEY CLUSTERED ([SessionId]),
            CONSTRAINT [FK_UserSessions_AspNetUsers]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_UserSessions_RegisteredDevices]
                FOREIGN KEY ([DeviceId]) REFERENCES [auth].[RegisteredDevices]([DeviceId]),
            CONSTRAINT [CK_UserSessions_Status]
                CHECK ([Status] IN ('Active','Revoked','Expired')),
            CONSTRAINT [CK_UserSessions_Expiry]
                CHECK ([AbsoluteExpiresAtUtc] > [StartedAtUtc]),
            CONSTRAINT [CK_UserSessions_Revocation]
                CHECK (([Status] <> 'Revoked') OR ([RevokedAtUtc] IS NOT NULL))
        );
    END;

    IF OBJECT_ID(N'[auth].[RefreshTokens]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[RefreshTokens]
        (
            [RefreshTokenId]    uniqueidentifier NOT NULL
                CONSTRAINT [DF_RefreshTokens_Id] DEFAULT NEWSEQUENTIALID(),
            [UserId]            nvarchar(450) NOT NULL,
            [SessionId]         uniqueidentifier NOT NULL,
            [TokenFamilyId]     uniqueidentifier NOT NULL,
            [TokenHash]         binary(32) NOT NULL,
            [TokenPrefix]       varchar(16) NULL,
            [JwtId]             nvarchar(100) NULL,
            [CreatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_RefreshTokens_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [ExpiresAtUtc]      datetime2(3) NOT NULL,
            [UsedAtUtc]         datetime2(3) NULL,
            [RevokedAtUtc]      datetime2(3) NULL,
            [RevokedReason]     nvarchar(500) NULL,
            [ReplacedByTokenId] uniqueidentifier NULL,
            [CreatedByIpAddress] varchar(45) NULL,
            CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([RefreshTokenId]),
            CONSTRAINT [UQ_RefreshTokens_TokenHash] UNIQUE ([TokenHash]),
            CONSTRAINT [FK_RefreshTokens_AspNetUsers]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_RefreshTokens_UserSessions]
                FOREIGN KEY ([SessionId]) REFERENCES [auth].[UserSessions]([SessionId]),
            CONSTRAINT [FK_RefreshTokens_ReplacedBy]
                FOREIGN KEY ([ReplacedByTokenId]) REFERENCES [auth].[RefreshTokens]([RefreshTokenId]),
            CONSTRAINT [CK_RefreshTokens_Expiry]
                CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
            CONSTRAINT [CK_RefreshTokens_UsedOrRevoked]
                CHECK ([UsedAtUtc] IS NULL OR [UsedAtUtc] >= [CreatedAtUtc]),
            CONSTRAINT [CK_RefreshTokens_Revoked]
                CHECK ([RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc])
        );
    END;

    /* ================================================================
       4. License plans, user licenses and device activations
       ================================================================ */

    IF OBJECT_ID(N'[auth].[LicensePlans]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[LicensePlans]
        (
            [LicensePlanId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_LicensePlans_Id] DEFAULT NEWSEQUENTIALID(),
            [PlanCode]            varchar(50) NOT NULL,
            [Name]                nvarchar(200) NOT NULL,
            [Description]         nvarchar(1000) NULL,
            [MaxActivatedDevices] int NOT NULL
                CONSTRAINT [DF_LicensePlans_MaxDevices] DEFAULT (1),
            [OfflineGraceHours]   int NOT NULL
                CONSTRAINT [DF_LicensePlans_OfflineGrace] DEFAULT (24),
            [DefaultDurationDays] int NULL,
            [FeatureFlagsJson]    nvarchar(max) NULL,
            [IsActive]            bit NOT NULL
                CONSTRAINT [DF_LicensePlans_IsActive] DEFAULT (1),
            [CreatedAtUtc]        datetime2(3) NOT NULL
                CONSTRAINT [DF_LicensePlans_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]        datetime2(3) NOT NULL
                CONSTRAINT [DF_LicensePlans_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RowVersion]          rowversion NOT NULL,
            CONSTRAINT [PK_LicensePlans] PRIMARY KEY CLUSTERED ([LicensePlanId]),
            CONSTRAINT [UQ_LicensePlans_PlanCode] UNIQUE ([PlanCode]),
            CONSTRAINT [CK_LicensePlans_MaxDevices]
                CHECK ([MaxActivatedDevices] BETWEEN 1 AND 1000),
            CONSTRAINT [CK_LicensePlans_OfflineGrace]
                CHECK ([OfflineGraceHours] BETWEEN 0 AND 8760),
            CONSTRAINT [CK_LicensePlans_Duration]
                CHECK ([DefaultDurationDays] IS NULL OR [DefaultDurationDays] > 0),
            CONSTRAINT [CK_LicensePlans_FeatureFlagsJson]
                CHECK ([FeatureFlagsJson] IS NULL OR ISJSON([FeatureFlagsJson]) = 1)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM [auth].[LicensePlans] WHERE [PlanCode] = 'trial-7')
        INSERT INTO [auth].[LicensePlans]
            ([PlanCode], [Name], [Description], [MaxActivatedDevices], [OfflineGraceHours], [DefaultDurationDays], [FeatureFlagsJson], [IsActive])
        VALUES
            ('trial-7', N'Dùng thử 7 ngày', N'Quyền sử dụng VideoMaker trong 7 ngày.', 1, 0, 7, N'{"maxConcurrentSessions":1}', 1);

    IF NOT EXISTS (SELECT 1 FROM [auth].[LicensePlans] WHERE [PlanCode] = 'monthly-30')
        INSERT INTO [auth].[LicensePlans]
            ([PlanCode], [Name], [Description], [MaxActivatedDevices], [OfflineGraceHours], [DefaultDurationDays], [FeatureFlagsJson], [IsActive])
        VALUES
            ('monthly-30', N'Gói 30 ngày', N'Quyền sử dụng VideoMaker trong 30 ngày.', 1, 0, 30, N'{"maxConcurrentSessions":1}', 1);

    IF NOT EXISTS (SELECT 1 FROM [auth].[LicensePlans] WHERE [PlanCode] = 'half-year-180')
        INSERT INTO [auth].[LicensePlans]
            ([PlanCode], [Name], [Description], [MaxActivatedDevices], [OfflineGraceHours], [DefaultDurationDays], [FeatureFlagsJson], [IsActive])
        VALUES
            ('half-year-180', N'Gói 180 ngày', N'Quyền sử dụng VideoMaker trong 180 ngày.', 1, 0, 180, N'{"maxConcurrentSessions":1}', 1);

    IF OBJECT_ID(N'[auth].[UserLicenses]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[UserLicenses]
        (
            [UserLicenseId]      uniqueidentifier NOT NULL
                CONSTRAINT [DF_UserLicenses_Id] DEFAULT NEWSEQUENTIALID(),
            [UserId]             nvarchar(450) NOT NULL,
            [LicensePlanId]      uniqueidentifier NOT NULL,
            [LicenseKeyHash]     binary(32) NULL,
            [Status]             varchar(20) NOT NULL
                CONSTRAINT [DF_UserLicenses_Status] DEFAULT ('Active'),
            [StartsAtUtc]        datetime2(3) NOT NULL,
            [ExpiresAtUtc]       datetime2(3) NULL,
            [EntitlementSnapshotJson] nvarchar(max) NULL,
            [GrantedByUserId]    nvarchar(450) NULL,
            [CreatedAtUtc]       datetime2(3) NOT NULL
                CONSTRAINT [DF_UserLicenses_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [UpdatedAtUtc]       datetime2(3) NOT NULL
                CONSTRAINT [DF_UserLicenses_UpdatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RevokedAtUtc]       datetime2(3) NULL,
            [RevokedReason]      nvarchar(500) NULL,
            [RowVersion]         rowversion NOT NULL,
            CONSTRAINT [PK_UserLicenses] PRIMARY KEY CLUSTERED ([UserLicenseId]),
            CONSTRAINT [FK_UserLicenses_AspNetUsers]
                FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [FK_UserLicenses_LicensePlans]
                FOREIGN KEY ([LicensePlanId]) REFERENCES [auth].[LicensePlans]([LicensePlanId]),
            CONSTRAINT [FK_UserLicenses_GrantedBy]
                FOREIGN KEY ([GrantedByUserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
            CONSTRAINT [CK_UserLicenses_Status]
                CHECK ([Status] IN ('Trial','Active','Suspended','Expired','Revoked')),
            CONSTRAINT [CK_UserLicenses_Expiry]
                CHECK ([ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > [StartsAtUtc]),
            CONSTRAINT [CK_UserLicenses_EntitlementSnapshotJson]
                CHECK ([EntitlementSnapshotJson] IS NULL OR ISJSON([EntitlementSnapshotJson]) = 1),
            CONSTRAINT [CK_UserLicenses_Revocation]
                CHECK (([Status] <> 'Revoked') OR ([RevokedAtUtc] IS NOT NULL))
        );
    END;

    IF OBJECT_ID(N'[auth].[LicenseActivations]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[LicenseActivations]
        (
            [LicenseActivationId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_LicenseActivations_Id] DEFAULT NEWSEQUENTIALID(),
            [UserLicenseId]       uniqueidentifier NOT NULL,
            [DeviceId]            uniqueidentifier NOT NULL,
            [Status]              varchar(20) NOT NULL
                CONSTRAINT [DF_LicenseActivations_Status] DEFAULT ('Active'),
            [ActivatedAtUtc]      datetime2(3) NOT NULL
                CONSTRAINT [DF_LicenseActivations_ActivatedAtUtc] DEFAULT SYSUTCDATETIME(),
            [LastVerifiedAtUtc]   datetime2(3) NOT NULL
                CONSTRAINT [DF_LicenseActivations_LastVerifiedAtUtc] DEFAULT SYSUTCDATETIME(),
            [RevokedAtUtc]        datetime2(3) NULL,
            [RevokedReason]       nvarchar(500) NULL,
            [RowVersion]          rowversion NOT NULL,
            CONSTRAINT [PK_LicenseActivations] PRIMARY KEY CLUSTERED ([LicenseActivationId]),
            CONSTRAINT [UQ_LicenseActivations_License_Device]
                UNIQUE ([UserLicenseId], [DeviceId]),
            CONSTRAINT [FK_LicenseActivations_UserLicenses]
                FOREIGN KEY ([UserLicenseId]) REFERENCES [auth].[UserLicenses]([UserLicenseId]),
            CONSTRAINT [FK_LicenseActivations_RegisteredDevices]
                FOREIGN KEY ([DeviceId]) REFERENCES [auth].[RegisteredDevices]([DeviceId]),
            CONSTRAINT [CK_LicenseActivations_Status]
                CHECK ([Status] IN ('Active','Revoked')),
            CONSTRAINT [CK_LicenseActivations_Revocation]
                CHECK (([Status] <> 'Revoked') OR ([RevokedAtUtc] IS NOT NULL))
        );
    END;

    /* ================================================================
       5. Security audit and desktop release policy
       ================================================================ */

    IF OBJECT_ID(N'[auth].[AccountAuditLogs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[AccountAuditLogs]
        (
            [AccountAuditLogId] bigint IDENTITY(1,1) NOT NULL,
            [UserId]            nvarchar(450) NULL,
            [EventType]         varchar(100) NOT NULL,
            [Succeeded]         bit NOT NULL,
            [IpAddress]         varchar(45) NULL,
            [UserAgent]         nvarchar(1000) NULL,
            [CorrelationId]     varchar(100) NULL,
            [DetailsJson]       nvarchar(max) NULL,
            [OccurredAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_AccountAuditLogs_OccurredAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_AccountAuditLogs] PRIMARY KEY CLUSTERED ([AccountAuditLogId]),
            CONSTRAINT [CK_AccountAuditLogs_DetailsJson]
                CHECK ([DetailsJson] IS NULL OR ISJSON([DetailsJson]) = 1)
        );
        /* Deliberately no FK to AspNetUsers: security audit must survive
           anonymization or hard deletion of an account. */
    END;

    IF OBJECT_ID(N'[auth].[AppReleases]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[AppReleases]
        (
            [AppReleaseId]       uniqueidentifier NOT NULL
                CONSTRAINT [DF_AppReleases_Id] DEFAULT NEWSEQUENTIALID(),
            [Version]            varchar(50) NOT NULL,
            [Channel]            varchar(20) NOT NULL
                CONSTRAINT [DF_AppReleases_Channel] DEFAULT ('Stable'),
            [MinimumSupportedDesktopVersion] varchar(50) NULL,
            [DownloadUrl]        nvarchar(2000) NULL,
            [Sha256]             char(64) NULL,
            [ReleaseNotes]       nvarchar(max) NULL,
            [IsMandatory]        bit NOT NULL
                CONSTRAINT [DF_AppReleases_IsMandatory] DEFAULT (0),
            [IsActive]           bit NOT NULL
                CONSTRAINT [DF_AppReleases_IsActive] DEFAULT (1),
            [PublishedAtUtc]     datetime2(3) NOT NULL
                CONSTRAINT [DF_AppReleases_PublishedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_AppReleases] PRIMARY KEY CLUSTERED ([AppReleaseId]),
            CONSTRAINT [UQ_AppReleases_Version_Channel] UNIQUE ([Version], [Channel]),
            CONSTRAINT [CK_AppReleases_Channel]
                CHECK ([Channel] IN ('Stable','Beta','Development')),
            CONSTRAINT [CK_AppReleases_Sha256]
                CHECK ([Sha256] IS NULL OR [Sha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([Sha256]) = 64)
        );
    END;

    IF COL_LENGTH(N'[auth].[AppReleases]', N'BuildNumber') IS NULL
        ALTER TABLE [auth].[AppReleases] ADD [BuildNumber] int NOT NULL
            CONSTRAINT [DF_AppReleases_BuildNumber] DEFAULT (1) WITH VALUES;

    IF COL_LENGTH(N'[auth].[AppReleases]', N'Platform') IS NULL
        ALTER TABLE [auth].[AppReleases] ADD [Platform] varchar(20) NOT NULL
            CONSTRAINT [DF_AppReleases_Platform] DEFAULT ('win-x64') WITH VALUES;

    IF EXISTS
    (
        SELECT 1 FROM sys.key_constraints
        WHERE [parent_object_id] = OBJECT_ID(N'[auth].[AppReleases]')
          AND [name] = N'UQ_AppReleases_Version_Channel'
    )
        ALTER TABLE [auth].[AppReleases] DROP CONSTRAINT [UQ_AppReleases_Version_Channel];

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[AppReleases]')
          AND [name] = N'UQ_AppReleases_Version_Build_Channel_Platform'
    )
        CREATE UNIQUE INDEX [UQ_AppReleases_Version_Build_Channel_Platform]
            ON [auth].[AppReleases] ([Version], [BuildNumber], [Channel], [Platform]);

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[auth].[AppReleases]')
          AND [name] = N'IX_AppReleases_Latest'
    )
        CREATE INDEX [IX_AppReleases_Latest]
            ON [auth].[AppReleases] ([Platform], [Channel], [IsActive], [PublishedAtUtc] DESC, [BuildNumber] DESC);

    IF OBJECT_ID(N'[auth].[AppReleaseArtifacts]', N'U') IS NULL
    BEGIN
        CREATE TABLE [auth].[AppReleaseArtifacts]
        (
            [AppReleaseArtifactId] uniqueidentifier NOT NULL
                CONSTRAINT [DF_AppReleaseArtifacts_Id] DEFAULT NEWSEQUENTIALID(),
            [AppReleaseId]         uniqueidentifier NOT NULL,
            [Kind]                 varchar(30) NOT NULL,
            [FileName]             nvarchar(260) NOT NULL,
            [RelativePath]         nvarchar(1000) NOT NULL,
            [SizeBytes]            bigint NOT NULL,
            [Sha256]               char(64) NOT NULL,
            [CreatedAtUtc]         datetime2(3) NOT NULL
                CONSTRAINT [DF_AppReleaseArtifacts_CreatedAtUtc] DEFAULT SYSUTCDATETIME(),
            CONSTRAINT [PK_AppReleaseArtifacts] PRIMARY KEY CLUSTERED ([AppReleaseArtifactId]),
            CONSTRAINT [FK_AppReleaseArtifacts_AppReleases]
                FOREIGN KEY ([AppReleaseId]) REFERENCES [auth].[AppReleases] ([AppReleaseId]) ON DELETE CASCADE,
            CONSTRAINT [UQ_AppReleaseArtifacts_Release_Kind] UNIQUE ([AppReleaseId], [Kind]),
            CONSTRAINT [CK_AppReleaseArtifacts_Kind]
                CHECK ([Kind] IN ('DesktopPackage','Setup')),
            CONSTRAINT [CK_AppReleaseArtifacts_Size] CHECK ([SizeBytes] > 0),
            CONSTRAINT [CK_AppReleaseArtifacts_Sha256]
                CHECK ([Sha256] NOT LIKE '%[^0-9A-Fa-f]%' AND LEN([Sha256]) = 64)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [auth].[SchemaVersions] WHERE [Version] = '2.3.0-desktop-update'
    )
    BEGIN
        INSERT INTO [auth].[SchemaVersions] ([Version], [Description])
        VALUES ('2.3.0-desktop-update', N'Versioned desktop packages, setup artifacts and automatic update policy.');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [auth].[SchemaVersions] WHERE [Version] = '2.1.0-identity-schema'
    )
    BEGIN
        INSERT INTO [auth].[SchemaVersions] ([Version], [Description])
        VALUES ('2.1.0-identity-schema', N'ASP.NET Core Identity, secure sessions, devices, licenses and audit in the shared database.');
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM [auth].[SchemaVersions] WHERE [Version] = '3.0.0-license-control'
    )
    BEGIN
        INSERT INTO [auth].[SchemaVersions] ([Version], [Description])
        VALUES ('3.0.0-license-control', N'Admin license plans, device activation, session limits and heartbeat leases.');
    END;

    /* Role IDs are stable so later application seeding remains idempotent.
       No user/admin password is ever seeded by SQL. */
    IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'USER')
    BEGIN
        INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
        VALUES (N'role-user', N'User', N'USER', CONVERT(nvarchar(36), NEWID()));
    END;

    IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'ADMIN')
    BEGIN
        INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
        VALUES (N'role-admin', N'Admin', N'ADMIN', CONVERT(nvarchar(36), NEWID()));
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

/* ====================================================================
   6. Shared-database integrity between video projects and accounts
   ==================================================================== */

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
      AND [name] = N'CK_Projects_RemoteDeviceRequiresUser'
)
    ALTER TABLE [vf].[Projects] WITH CHECK
        ADD CONSTRAINT [CK_Projects_RemoteDeviceRequiresUser]
        CHECK ([RemoteDeviceId] IS NULL OR [RemoteUserId] IS NOT NULL);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[auth].[RegisteredDevices]')
      AND [name] = N'UX_RegisteredDevices_Device_User'
)
    CREATE UNIQUE INDEX [UX_RegisteredDevices_Device_User]
        ON [auth].[RegisteredDevices] ([DeviceId], [UserId]);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
      AND [name] = N'FK_Projects_AspNetUsers_RemoteUserId'
)
BEGIN
    ALTER TABLE [vf].[Projects] WITH CHECK
        ADD CONSTRAINT [FK_Projects_AspNetUsers_RemoteUserId]
        FOREIGN KEY ([RemoteUserId]) REFERENCES [dbo].[AspNetUsers]([Id]);

    ALTER TABLE [vf].[Projects]
        CHECK CONSTRAINT [FK_Projects_AspNetUsers_RemoteUserId];
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE [parent_object_id] = OBJECT_ID(N'[vf].[Projects]')
      AND [name] = N'FK_Projects_RegisteredDevices_RemoteOwner'
)
BEGIN
    ALTER TABLE [vf].[Projects] WITH CHECK
        ADD CONSTRAINT [FK_Projects_RegisteredDevices_RemoteOwner]
        FOREIGN KEY ([RemoteDeviceId], [RemoteUserId])
        REFERENCES [auth].[RegisteredDevices]([DeviceId], [UserId]);

    ALTER TABLE [vf].[Projects]
        CHECK CONSTRAINT [FK_Projects_RegisteredDevices_RemoteOwner];
END;
GO

/* ====================================================================
   7. Account indexes
   ==================================================================== */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetRoles]') AND name = N'RoleNameIndex')
    CREATE UNIQUE INDEX [RoleNameIndex]
        ON [dbo].[AspNetRoles] ([NormalizedName])
        WHERE [NormalizedName] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = N'EmailIndex')
    CREATE INDEX [EmailIndex]
        ON [dbo].[AspNetUsers] ([NormalizedEmail]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUsers]') AND name = N'UserNameIndex')
    CREATE UNIQUE INDEX [UserNameIndex]
        ON [dbo].[AspNetUsers] ([NormalizedUserName])
        WHERE [NormalizedUserName] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetRoleClaims]') AND name = N'IX_AspNetRoleClaims_RoleId')
    CREATE INDEX [IX_AspNetRoleClaims_RoleId]
        ON [dbo].[AspNetRoleClaims] ([RoleId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUserClaims]') AND name = N'IX_AspNetUserClaims_UserId')
    CREATE INDEX [IX_AspNetUserClaims_UserId]
        ON [dbo].[AspNetUserClaims] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUserLogins]') AND name = N'IX_AspNetUserLogins_UserId')
    CREATE INDEX [IX_AspNetUserLogins_UserId]
        ON [dbo].[AspNetUserLogins] ([UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[AspNetUserRoles]') AND name = N'IX_AspNetUserRoles_RoleId')
    CREATE INDEX [IX_AspNetUserRoles_RoleId]
        ON [dbo].[AspNetUserRoles] ([RoleId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[RegisteredDevices]') AND name = N'IX_RegisteredDevices_User_Status')
    CREATE INDEX [IX_RegisteredDevices_User_Status]
        ON [auth].[RegisteredDevices] ([UserId], [IsRevoked], [LastSeenAtUtc] DESC)
        INCLUDE ([DeviceName], [ApplicationVersion], [IsTrusted]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[UserSessions]') AND name = N'IX_UserSessions_User_Status')
    CREATE INDEX [IX_UserSessions_User_Status]
        ON [auth].[UserSessions] ([UserId], [Status], [LastSeenAtUtc] DESC)
        INCLUDE ([DeviceId], [AbsoluteExpiresAtUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[UserSessions]') AND name = N'IX_UserSessions_Expiry')
    CREATE INDEX [IX_UserSessions_Expiry]
        ON [auth].[UserSessions] ([Status], [AbsoluteExpiresAtUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[RefreshTokens]') AND name = N'IX_RefreshTokens_Session_Expiry')
    CREATE INDEX [IX_RefreshTokens_Session_Expiry]
        ON [auth].[RefreshTokens] ([SessionId], [ExpiresAtUtc] DESC)
        INCLUDE ([TokenFamilyId], [UsedAtUtc], [RevokedAtUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[RefreshTokens]') AND name = N'IX_RefreshTokens_User_Family')
    CREATE INDEX [IX_RefreshTokens_User_Family]
        ON [auth].[RefreshTokens] ([UserId], [TokenFamilyId], [CreatedAtUtc] DESC)
        INCLUDE ([SessionId], [ExpiresAtUtc], [RevokedAtUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[UserLicenses]') AND name = N'UX_UserLicenses_LicenseKeyHash')
    CREATE UNIQUE INDEX [UX_UserLicenses_LicenseKeyHash]
        ON [auth].[UserLicenses] ([LicenseKeyHash])
        WHERE [LicenseKeyHash] IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[UserLicenses]') AND name = N'IX_UserLicenses_User_Status')
    CREATE INDEX [IX_UserLicenses_User_Status]
        ON [auth].[UserLicenses] ([UserId], [Status], [ExpiresAtUtc])
        INCLUDE ([LicensePlanId], [StartsAtUtc]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[LicenseActivations]') AND name = N'IX_LicenseActivations_Device_Status')
    CREATE INDEX [IX_LicenseActivations_Device_Status]
        ON [auth].[LicenseActivations] ([DeviceId], [Status], [LastVerifiedAtUtc] DESC)
        INCLUDE ([UserLicenseId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[AccountAuditLogs]') AND name = N'IX_AccountAuditLogs_User_OccurredAt')
    CREATE INDEX [IX_AccountAuditLogs_User_OccurredAt]
        ON [auth].[AccountAuditLogs] ([UserId], [OccurredAtUtc] DESC)
        INCLUDE ([EventType], [Succeeded], [CorrelationId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[auth].[AccountAuditLogs]') AND name = N'IX_AccountAuditLogs_Event_OccurredAt')
    CREATE INDEX [IX_AccountAuditLogs_Event_OccurredAt]
        ON [auth].[AccountAuditLogs] ([EventType], [OccurredAtUtc] DESC)
        INCLUDE ([UserId], [Succeeded], [IpAddress]);
GO

/* ====================================================================
   8. Atomic security procedures
   ==================================================================== */

CREATE OR ALTER PROCEDURE [auth].[usp_RevokeSession]
    @SessionId uniqueidentifier,
    @Reason    nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE [auth].[UserSessions]
    SET [Status]        = 'Revoked',
        [RevokedAtUtc]  = COALESCE([RevokedAtUtc], SYSUTCDATETIME()),
        [RevokedReason] = COALESCE(@Reason, [RevokedReason], N'Session revoked')
    WHERE [SessionId] = @SessionId
      AND [Status] <> 'Revoked';

    UPDATE [auth].[RefreshTokens]
    SET [RevokedAtUtc]  = COALESCE([RevokedAtUtc], SYSUTCDATETIME()),
        [RevokedReason] = COALESCE(@Reason, [RevokedReason], N'Parent session revoked')
    WHERE [SessionId] = @SessionId
      AND [RevokedAtUtc] IS NULL;

    COMMIT TRANSACTION;

    SELECT [SessionId], [UserId], [DeviceId], [Status], [RevokedAtUtc], [RevokedReason]
    FROM [auth].[UserSessions]
    WHERE [SessionId] = @SessionId;
END;
GO

CREATE OR ALTER PROCEDURE [auth].[usp_RevokeAllUserSessions]
    @UserId        nvarchar(450),
    @ExceptSessionId uniqueidentifier = NULL,
    @Reason        nvarchar(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NowUtc datetime2(3) = SYSUTCDATETIME();

    BEGIN TRANSACTION;

    UPDATE [auth].[UserSessions]
    SET [Status]        = 'Revoked',
        [RevokedAtUtc]  = COALESCE([RevokedAtUtc], @NowUtc),
        [RevokedReason] = COALESCE(@Reason, [RevokedReason], N'All user sessions revoked')
    WHERE [UserId] = @UserId
      AND [Status] <> 'Revoked'
      AND (@ExceptSessionId IS NULL OR [SessionId] <> @ExceptSessionId);

    UPDATE [rt]
    SET [rt].[RevokedAtUtc]  = COALESCE([rt].[RevokedAtUtc], @NowUtc),
        [rt].[RevokedReason] = COALESCE(@Reason, [rt].[RevokedReason], N'All user sessions revoked')
    FROM [auth].[RefreshTokens] AS [rt]
    INNER JOIN [auth].[UserSessions] AS [s]
        ON [s].[SessionId] = [rt].[SessionId]
    WHERE [s].[UserId] = @UserId
      AND [rt].[RevokedAtUtc] IS NULL
      AND (@ExceptSessionId IS NULL OR [s].[SessionId] <> @ExceptSessionId);

    COMMIT TRANSACTION;

    SELECT COUNT_BIG(*) AS [RemainingActiveSessions]
    FROM [auth].[UserSessions]
    WHERE [UserId] = @UserId
      AND [Status] = 'Active'
      AND [AbsoluteExpiresAtUtc] > @NowUtc;
END;
GO

/* ====================================================================
   9. Read model for account administration
   ==================================================================== */

CREATE OR ALTER VIEW [auth].[vw_UserAccountSummary]
AS
    SELECT
        [u].[Id] AS [UserId],
        [u].[UserName],
        [u].[Email],
        [u].[EmailConfirmed],
        [u].[DisplayName],
        [u].[AccountStatus],
        [u].[LastLoginAtUtc],
        ISNULL([d].[RegisteredDeviceCount], 0) AS [RegisteredDeviceCount],
        ISNULL([d].[ActiveDeviceCount], 0) AS [ActiveDeviceCount],
        ISNULL([s].[ActiveSessionCount], 0) AS [ActiveSessionCount],
        ISNULL([l].[ActiveLicenseCount], 0) AS [ActiveLicenseCount],
        [l].[NearestLicenseExpiryUtc],
        [u].[CreatedAtUtc],
        [u].[UpdatedAtUtc]
    FROM [dbo].[AspNetUsers] AS [u]
    OUTER APPLY
    (
        SELECT
            COUNT_BIG(*) AS [RegisteredDeviceCount],
            SUM(CASE WHEN [IsRevoked] = 0 THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS [ActiveDeviceCount]
        FROM [auth].[RegisteredDevices]
        WHERE [UserId] = [u].[Id]
    ) AS [d]
    OUTER APPLY
    (
        SELECT COUNT_BIG(*) AS [ActiveSessionCount]
        FROM [auth].[UserSessions]
        WHERE [UserId] = [u].[Id]
          AND [Status] = 'Active'
          AND [AbsoluteExpiresAtUtc] > SYSUTCDATETIME()
    ) AS [s]
    OUTER APPLY
    (
        SELECT
            COUNT_BIG(*) AS [ActiveLicenseCount],
            MIN([ExpiresAtUtc]) AS [NearestLicenseExpiryUtc]
        FROM [auth].[UserLicenses]
        WHERE [UserId] = [u].[Id]
          AND [Status] IN ('Trial','Active')
          AND ([ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > SYSUTCDATETIME())
    ) AS [l]
    WHERE [u].[DeletedAtUtc] IS NULL;
GO

PRINT N'VideoFactory [auth]/Identity schema 3.0.0-license-control is ready.';

SELECT
    DB_NAME() AS [DatabaseName],
    (SELECT COUNT(*) FROM sys.tables WHERE [schema_id] IN (SCHEMA_ID(N'dbo'), SCHEMA_ID(N'auth'))) AS [ApplicationTableCount],
    (SELECT COUNT(*) FROM sys.procedures WHERE [schema_id] = SCHEMA_ID(N'auth')) AS [ApplicationProcedureCount],
    (SELECT COUNT(*) FROM sys.views WHERE [schema_id] = SCHEMA_ID(N'auth')) AS [ApplicationViewCount];

SELECT [Version], [Description], [AppliedAtUtc]
FROM [auth].[SchemaVersions]
ORDER BY [SchemaVersionId] DESC;
GO
