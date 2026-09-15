-- Review and rehearse on a disposable database before applying to an authorized environment.
-- Server-owned schedules, source images, execution checkpoints and publishing connections.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'vf.ShortVideoOperations', N'U') IS NULL OR OBJECT_ID(N'ai.SchemaVersions', N'U') IS NULL
    THROW 51910, 'Apply the current workflow and short-video migrations first.', 1;
 IF SCHEMA_ID(N'social') IS NULL EXEC(N'CREATE SCHEMA social');

 IF OBJECT_ID(N'social.PublishingSchedules', N'U') IS NULL
 CREATE TABLE social.PublishingSchedules (
    ScheduleId uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL REFERENCES ai.Organizations(OrganizationId),
    UserId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
    DeviceId uniqueidentifier NOT NULL REFERENCES auth.RegisteredDevices(DeviceId),
    SessionId uniqueidentifier NOT NULL,
    Revision int NOT NULL CHECK (Revision > 0),
    Status nvarchar(32) NOT NULL CHECK (Status IN ('Draft','Active','Paused','Cancelled','Completed')),
    InputJson nvarchar(max) NOT NULL CHECK (ISJSON(InputJson)=1),
    InputHash nvarchar(64) NOT NULL,
    NextPublishAtUtc datetime2 NULL,
    ConsentAtUtc datetime2 NULL,
    CreatedAtUtc datetime2 NOT NULL,
    UpdatedAtUtc datetime2 NOT NULL
 );

 IF OBJECT_ID(N'social.PublishingImages', N'U') IS NULL
 CREATE TABLE social.PublishingImages (
    ImageId uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL REFERENCES ai.Organizations(OrganizationId),
    UserId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
    Role nvarchar(32) NOT NULL CHECK (Role IN ('Character','Product')),
    InfoJson nvarchar(max) NOT NULL CHECK (ISJSON(InfoJson)=1),
    ProtectedPayload varbinary(max) NOT NULL,
    CreatedAtUtc datetime2 NOT NULL,
    ExpiresAtUtc datetime2 NOT NULL
 );
 IF OBJECT_ID(N'social.PublishingRuns', N'U') IS NULL
 CREATE TABLE social.PublishingRuns (
    RunId uniqueidentifier NOT NULL PRIMARY KEY,
    ScheduleId uniqueidentifier NOT NULL REFERENCES social.PublishingSchedules(ScheduleId),
    OrganizationId uniqueidentifier NOT NULL REFERENCES ai.Organizations(OrganizationId),
    UserId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
    DeviceId uniqueidentifier NOT NULL,
    SessionId uniqueidentifier NOT NULL,
    ScheduleRevision int NOT NULL,
    ProductionConsentAtUtc datetime2 NOT NULL,
    InputJson nvarchar(max) NOT NULL CHECK (ISJSON(InputJson)=1),
    GenerateAtUtc datetime2 NOT NULL,
    PublishAtUtc datetime2 NOT NULL,
    DeadlineAtUtc datetime2 NOT NULL,
    Status nvarchar(32) NOT NULL,
    ErrorCode nvarchar(100) NULL,
    Message nvarchar(1000) NULL,
    ResumeStatus nvarchar(max) NULL,
    -- Reserved before the workflow transaction creates the project; access is always checked server-side.
    ProjectId uniqueidentifier NULL,
    SceneId uniqueidentifier NULL,
    ImageQuoteId uniqueidentifier NULL,
    VideoQuoteId uniqueidentifier NULL,
    ProviderRequestId uniqueidentifier NULL,
    EstimatedCost decimal(19,6) NOT NULL CHECK (EstimatedCost >= 0),
    MediaSha256 nvarchar(64) NULL,
    MediaSizeBytes bigint NOT NULL CHECK (MediaSizeBytes >= 0),
    ReviewedAtUtc datetime2 NULL,
    ReviewedByUserId nvarchar(450) NULL,
    LeaseId uniqueidentifier NULL,
    LeaseUntilUtc datetime2 NULL,
    NextCheckAtUtc datetime2 NOT NULL,
    CreatedAtUtc datetime2 NOT NULL,
    UpdatedAtUtc datetime2 NOT NULL,
    CONSTRAINT CK_PublishingRuns_Times CHECK (GenerateAtUtc <= PublishAtUtc AND PublishAtUtc < DeadlineAtUtc),
    CONSTRAINT CK_PublishingRuns_Review CHECK (ReviewedAtUtc IS NULL OR (ReviewedByUserId IS NOT NULL AND MediaSha256 IS NOT NULL))
 );

 IF OBJECT_ID(N'social.PublishingDeliveries', N'U') IS NULL
 CREATE TABLE social.PublishingDeliveries (
    DeliveryId uniqueidentifier NOT NULL PRIMARY KEY,
    RunId uniqueidentifier NOT NULL REFERENCES social.PublishingRuns(RunId),
    Platform nvarchar(32) NOT NULL CHECK (Platform IN ('TikTok','Facebook','YouTube')),
    ConnectionId uniqueidentifier NOT NULL,
    AccountName nvarchar(200) NOT NULL,
    SettingsJson nvarchar(max) NOT NULL CHECK (ISJSON(SettingsJson)=1),
    Status nvarchar(32) NOT NULL,
    ExternalId nvarchar(150) NULL,
    ProtectedUploadUrl nvarchar(max) NULL,
    PostUrl nvarchar(512) NULL,
    Message nvarchar(1000) NULL,
    UpdatedAtUtc datetime2 NOT NULL
 );
 IF OBJECT_ID(N'social.PublishingConnections', N'U') IS NULL
 CREATE TABLE social.PublishingConnections (
    ConnectionId uniqueidentifier NOT NULL PRIMARY KEY,
    UserId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
    Platform nvarchar(32) NOT NULL CHECK (Platform IN ('Facebook','YouTube')),
    ExternalId nvarchar(150) NOT NULL,
    DisplayName nvarchar(200) NOT NULL,
    Status nvarchar(32) NOT NULL CHECK (Status IN ('Connected','Disconnected')),
    ProtectedTokens nvarchar(max) NOT NULL,
    UpdatedAtUtc datetime2 NOT NULL
 );
 IF OBJECT_ID(N'social.PublishingOAuthSessions', N'U') IS NULL
 CREATE TABLE social.PublishingOAuthSessions (
    OAuthSessionId uniqueidentifier NOT NULL PRIMARY KEY,
    UserId nvarchar(450) NOT NULL REFERENCES dbo.AspNetUsers(Id),
    DeviceId uniqueidentifier NOT NULL,
    SessionId uniqueidentifier NOT NULL,
    OrganizationId uniqueidentifier NOT NULL REFERENCES ai.Organizations(OrganizationId),
    Platform nvarchar(32) NOT NULL CHECK (Platform IN ('Facebook','YouTube')),
    StateHash nvarchar(64) NOT NULL,
    ProtectedVerifier nvarchar(max) NOT NULL,
    ExpiresAtUtc datetime2 NOT NULL,
    Consumed bit NOT NULL
 );

 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'social.PublishingSchedules') AND name=N'IX_PublishingSchedules_Status_NextPublishAtUtc')
 CREATE INDEX IX_PublishingSchedules_Status_NextPublishAtUtc ON social.PublishingSchedules(Status,NextPublishAtUtc);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'social.PublishingRuns') AND name=N'IX_PublishingRuns_ScheduleId_PublishAtUtc')
 CREATE UNIQUE INDEX IX_PublishingRuns_ScheduleId_PublishAtUtc ON social.PublishingRuns(ScheduleId,PublishAtUtc);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'social.PublishingRuns') AND name=N'IX_PublishingRuns_Status_NextCheckAtUtc')
 CREATE INDEX IX_PublishingRuns_Status_NextCheckAtUtc ON social.PublishingRuns(Status,NextCheckAtUtc);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'social.PublishingDeliveries') AND name=N'IX_PublishingDeliveries_RunId_Platform_ConnectionId')
 CREATE UNIQUE INDEX IX_PublishingDeliveries_RunId_Platform_ConnectionId ON social.PublishingDeliveries(RunId,Platform,ConnectionId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'social.PublishingConnections') AND name=N'IX_PublishingConnections_UserId_Platform_ExternalId')
 CREATE UNIQUE INDEX IX_PublishingConnections_UserId_Platform_ExternalId ON social.PublishingConnections(UserId,Platform,ExternalId);
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'social.PublishingOAuthSessions') AND name=N'IX_PublishingOAuthSessions_StateHash')
 CREATE UNIQUE INDEX IX_PublishingOAuthSessions_StateHash ON social.PublishingOAuthSessions(StateHash);

 IF DATABASE_PRINCIPAL_ID(N'VideoMakerDesktopRole') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::social TO VideoMakerDesktopRole;
 IF NOT EXISTS (SELECT 1 FROM ai.SchemaVersions WHERE Version='4.1.10-publishing-schedules')
    INSERT INTO ai.SchemaVersions(Version,Description) VALUES ('4.1.10-publishing-schedules',N'Server-owned video production schedules and social publication checkpoints.');
 COMMIT;
END TRY
BEGIN CATCH
 IF XACT_STATE() <> 0 ROLLBACK;
 THROW;
END CATCH;
